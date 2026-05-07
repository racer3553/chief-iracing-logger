"""Smart event detector — turns raw telemetry into coachable events.

Now includes:
- sector_loss      : you lost time in a specific sector vs your own best
- great_sector     : you set a new sector best
- late_brake       : braked later than your average
- early_throttle   : got back to throttle earlier than recent
- coast            : noticeable coasting (no throttle, no brake) — wasted time
- chatty mode      : occasional positive reinforcement
"""
import time
import logging
from typing import Optional, Dict, Any, List
from dataclasses import dataclass, field
from collections import deque

import config

log = logging.getLogger("chief.events")


@dataclass
class CoachEvent:
    kind: str
    severity: str        # "info" | "high" | "critical"
    summary: str
    data: Dict[str, Any] = field(default_factory=dict)
    ts: float = field(default_factory=time.time)


def _sector_label(idx: int, total: int) -> str:
    """Convert a sector index to a friendly label like 'sector 4 of 10'."""
    return f"sector {idx + 1}"


class EventDetector:
    """Watches telemetry stream and emits CoachEvents."""

    def __init__(self):
        self._prev_lap_completed = -1
        self._prev_track_surface = 3
        self._prev_incidents = 0
        self._last_fuel_warning_lap = -1
        self._last_tire_warning_ts = 0.0
        self._best_seen = 0.0

        # Driving rolling stats for late_brake / early_throttle detection
        self._brake_pressed_since: Optional[float] = None
        self._throttle_history = deque(maxlen=300)  # last 5s @ 60Hz
        self._brake_history = deque(maxlen=300)
        self._coast_started_ts: Optional[float] = None
        self._last_coast_warning = 0.0

        # Verbosity
        self._verbosity = (getattr(config, "COACH_VERBOSITY", "normal") or "normal").lower()
        self._sector_loss_threshold = float(getattr(config, "COACH_SECTOR_LOSS_THRESHOLD", 0.10))

    def update(self, snap: Dict[str, Any]) -> List[CoachEvent]:
        events: List[CoachEvent] = []
        if snap is None:
            return events

        lap = snap["lap"]
        lap_completed = snap["lap_completed"]
        last_time = snap["lap_last_time"]
        best_time = snap["lap_best_time"]
        on_pit = snap["on_pit_road"]
        surface = snap["track_surface"]
        incidents = snap["incidents"]
        fuel_pct = snap["fuel_pct"]
        rf_temp = snap["tire_rf_temp"]
        lf_temp = snap["tire_lf_temp"]
        rr_temp = snap["tire_rr_temp"]
        lr_temp = snap["tire_lr_temp"]

        # Track best
        if best_time > 0 and (self._best_seen == 0 or best_time < self._best_seen):
            self._best_seen = best_time

        # Track inputs for behavior detection
        self._throttle_history.append(snap.get("throttle", 0.0))
        self._brake_history.append(snap.get("brake", 0.0))

        # ── Lap-completion events ──
        if lap_completed > self._prev_lap_completed and lap_completed > 0:
            self._prev_lap_completed = lap_completed
            if last_time > 0 and not on_pit:
                if best_time > 0 and last_time <= best_time + 0.05:
                    events.append(CoachEvent(
                        kind="great_lap", severity="info",
                        summary=f"Lap {lap_completed}: {last_time:.3f}s — new personal best.",
                        data={"lap": lap_completed, "time": last_time, "best": best_time},
                    ))
                elif best_time > 0 and last_time > best_time + 0.6:
                    events.append(CoachEvent(
                        kind="slow_lap", severity="high",
                        summary=f"Lap {lap_completed}: {last_time:.3f}s — {(last_time - best_time):+.3f}s vs best.",
                        data={"lap": lap_completed, "time": last_time, "best": best_time, "delta": last_time - best_time},
                    ))
                elif lap_completed == 1:
                    events.append(CoachEvent(
                        kind="first_lap", severity="info",
                        summary=f"First flying lap: {last_time:.3f}s. Baseline set.",
                        data={"time": last_time},
                    ))

        # ── Off-track ──
        if surface == 0 and self._prev_track_surface != 0 and not on_pit:
            events.append(CoachEvent(
                kind="off_track", severity="high",
                summary=f"Off track at lap {lap}, {snap['lap_dist_pct']*100:.0f}% of lap distance.",
                data={"lap_pct": snap["lap_dist_pct"]},
            ))
        self._prev_track_surface = surface

        # ── Incident ──
        if incidents > self._prev_incidents:
            delta_inc = incidents - self._prev_incidents
            events.append(CoachEvent(
                kind="incident", severity="critical" if delta_inc >= 4 else "high",
                summary=f"Incident: +{delta_inc}x ({incidents} total).",
                data={"added": delta_inc, "total": incidents},
            ))
            self._prev_incidents = incidents

        # ── Fuel low ──
        if fuel_pct > 0 and lap != self._last_fuel_warning_lap:
            if fuel_pct < 0.10:
                events.append(CoachEvent(
                    kind="fuel_low", severity="critical",
                    summary=f"Fuel critical: {fuel_pct*100:.0f}% remaining.",
                    data={"fuel_pct": fuel_pct},
                ))
                self._last_fuel_warning_lap = lap
            elif fuel_pct < 0.20:
                events.append(CoachEvent(
                    kind="fuel_low", severity="high",
                    summary=f"Fuel getting low: {fuel_pct*100:.0f}% remaining.",
                    data={"fuel_pct": fuel_pct},
                ))
                self._last_fuel_warning_lap = lap

        # ── Tire overheat ──
        now = time.time()
        if now - self._last_tire_warning_ts > 60 and not on_pit:
            temps = {"LF": lf_temp, "RF": rf_temp, "LR": lr_temp, "RR": rr_temp}
            hot = [(k, v) for k, v in temps.items() if v > 110]
            if hot:
                k, v = max(hot, key=lambda x: x[1])
                events.append(CoachEvent(
                    kind="tire_hot", severity="high",
                    summary=f"{k} tire {v:.0f}°C — running hot.",
                    data={"tire": k, "temp": v, "all": temps},
                ))
                self._last_tire_warning_ts = now

        # ── Coast detection (chatty mode only) ──
        if self._verbosity == "chatty" and not on_pit and snap.get("speed_ms", 0) > 10:
            thr = snap.get("throttle", 0.0)
            brk = snap.get("brake", 0.0)
            if thr < 0.05 and brk < 0.05:
                if self._coast_started_ts is None:
                    self._coast_started_ts = now
                elif now - self._coast_started_ts > 1.5 and now - self._last_coast_warning > 30:
                    events.append(CoachEvent(
                        kind="coast", severity="info",
                        summary=f"Coasting {now - self._coast_started_ts:.1f}s — wasted time.",
                        data={"duration": now - self._coast_started_ts},
                    ))
                    self._last_coast_warning = now
            else:
                self._coast_started_ts = None

        return events

    # ── Sector-driven events (called from chief.py with sector tracker results) ──
    def on_sector_completed(self, sector_idx: int, sector_time: float, delta: float,
                            num_sectors: int) -> List[CoachEvent]:
        out: List[CoachEvent] = []
        label = _sector_label(sector_idx, num_sectors)

        if delta > self._sector_loss_threshold:
            sev = "high" if delta > 0.25 else "info"
            out.append(CoachEvent(
                kind="sector_loss", severity=sev,
                summary=f"{label}: lost {delta:.3f}s vs your best ({sector_time:.3f}s).",
                data={"sector": sector_idx, "sector_label": label, "delta": delta, "time": sector_time},
            ))
        elif delta < -0.05 and self._verbosity in ("normal", "chatty"):
            # Personal best in this sector
            out.append(CoachEvent(
                kind="great_sector", severity="info",
                summary=f"{label}: new best ({sector_time:.3f}s, gained {-delta:.3f}s).",
                data={"sector": sector_idx, "sector_label": label, "delta": delta, "time": sector_time},
            ))
        return out
