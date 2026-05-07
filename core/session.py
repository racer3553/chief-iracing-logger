"""Session state — tracks laps, best, deltas, and applies coaching guardrails."""
import time
import uuid
import logging
from typing import Dict, Any, List, Optional
from dataclasses import dataclass, field

import config

log = logging.getLogger("chief.session")


@dataclass
class LapRow:
    lap_number: int
    lap_time: float
    is_valid: bool = True
    incidents: int = 0
    fuel_used: float = 0.0
    fuel_remaining: float = 0.0
    max_speed: float = 0.0
    min_speed: float = 0.0
    avg_throttle: float = 0.0
    avg_brake: float = 0.0
    max_lat_g: float = 0.0
    max_long_g: float = 0.0


class Session:
    """Tracks the current iRacing session for post-session sync + coach guardrails."""

    def __init__(self):
        self.session_id = str(uuid.uuid4())
        self.started_at = time.time()
        self.ended_at: Optional[float] = None
        self.info: Dict[str, Any] = {}
        self.laps: List[LapRow] = []
        self._lap_accum: Dict[str, Any] = {}
        self._current_lap = 0
        self._fuel_at_lap_start = 0.0

        # Coach guardrails
        self._last_call_ts = 0.0
        self._calls_this_lap = 0
        self._lap_for_call_count = 0

    def attach_info(self, info: Dict[str, Any]):
        self.info = info

    def update(self, snap: Dict[str, Any]):
        if snap is None:
            return
        lap = snap["lap"]
        if lap != self._current_lap:
            self._roll_lap(snap)
            self._current_lap = lap

        # Reset per-lap call count when lap changes
        if lap != self._lap_for_call_count:
            self._calls_this_lap = 0
            self._lap_for_call_count = lap

        # Accumulate samples for the current lap
        a = self._lap_accum
        a.setdefault("throttle_sum", 0.0)
        a.setdefault("brake_sum", 0.0)
        a.setdefault("samples", 0)
        a.setdefault("max_speed", 0.0)
        a.setdefault("min_speed", 1e9)
        a.setdefault("max_lat_g", 0.0)
        a.setdefault("max_long_g", 0.0)
        a.setdefault("incidents_at_start", snap["incidents"])

        a["throttle_sum"] += snap["throttle"]
        a["brake_sum"] += snap["brake"]
        a["samples"] += 1
        a["max_speed"] = max(a["max_speed"], snap["speed_ms"])
        a["min_speed"] = min(a["min_speed"], snap["speed_ms"])
        a["max_lat_g"] = max(a["max_lat_g"], abs(snap["lat_g"]))
        a["max_long_g"] = max(a["max_long_g"], abs(snap["long_g"]))

    def _roll_lap(self, snap: Dict[str, Any]):
        last_time = snap["lap_last_time"] or 0.0
        if last_time > 0 and self._current_lap > 0:
            a = self._lap_accum
            samples = max(a.get("samples", 1), 1)
            row = LapRow(
                lap_number=self._current_lap,
                lap_time=last_time,
                incidents=snap["incidents"] - a.get("incidents_at_start", snap["incidents"]),
                fuel_used=max(0.0, self._fuel_at_lap_start - snap["fuel_level"]),
                fuel_remaining=snap["fuel_level"],
                max_speed=a.get("max_speed", 0.0),
                min_speed=a.get("min_speed", 0.0) if a.get("min_speed", 1e9) < 1e9 else 0.0,
                avg_throttle=a.get("throttle_sum", 0.0) / samples,
                avg_brake=a.get("brake_sum", 0.0) / samples,
                max_lat_g=a.get("max_lat_g", 0.0),
                max_long_g=a.get("max_long_g", 0.0),
            )
            self.laps.append(row)
            log.info(f"Lap {row.lap_number} logged: {row.lap_time:.3f}s")
        # Reset
        self._lap_accum = {}
        self._fuel_at_lap_start = snap["fuel_level"]

    # ── Coach guardrails ──────────────────────────────────
    def can_call(self) -> bool:
        """Throttle: respect min interval + max calls per lap."""
        now = time.time()
        if now - self._last_call_ts < config.COACH_MIN_INTERVAL_SEC:
            return False
        if self._calls_this_lap >= config.COACH_MAX_CALLS_PER_LAP:
            return False
        return True

    def mark_call(self):
        self._last_call_ts = time.time()
        self._calls_this_lap += 1

    def best_lap_time(self) -> float:
        valid = [l.lap_time for l in self.laps if l.is_valid and l.lap_time > 0]
        return min(valid) if valid else 0.0

    def to_sync_payload(self) -> Dict[str, Any]:
        """Format for chief-final /api/sim/sync-session."""
        best = self.best_lap_time()
        best_lap_num = next((l.lap_number for l in self.laps if l.lap_time == best), 0)
        return {
            "session": {
                "session_id": self.session_id,
                "iracing_sub_session_id": self.info.get("sub_session_id", 0),
                "driver_name": self.info.get("driver_name", ""),
                "driver_id": self.info.get("driver_id", 0),
                "car_name": self.info.get("car_path", ""),
                "car_screen_name": self.info.get("car_name", ""),
                "track_name": self.info.get("track_name", ""),
                "track_display_name": self.info.get("track_name", ""),
                "track_id": self.info.get("track_id", 0),
                "track_config": self.info.get("track_config", ""),
                "session_type": "Practice",
                "skies": self.info.get("skies", ""),
                "started_at": _iso(self.started_at),
                "ended_at": _iso(self.ended_at) if self.ended_at else None,
                "total_laps": len(self.laps),
                "best_lap_time": best,
                "best_lap_number": best_lap_num,
                "incident_count": sum(l.incidents for l in self.laps),
                "fuel_used_total": sum(l.fuel_used for l in self.laps),
                "fuel_per_lap": (sum(l.fuel_used for l in self.laps) / len(self.laps)) if self.laps else 0,
            },
            "laps": [l.__dict__ for l in self.laps],
        }


def _iso(ts: float) -> str:
    from datetime import datetime, timezone
    return datetime.fromtimestamp(ts, tz=timezone.utc).isoformat()
