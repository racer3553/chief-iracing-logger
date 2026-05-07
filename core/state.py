"""Shared, thread-safe runtime state.

The HTTP server, telemetry loop, voice engine, and coach all read/write here.
Web app polls /telemetry/latest and /coaching/latest off this state.
"""
import threading
import time
from collections import deque
from typing import Optional, Dict, Any, List


class State:
    def __init__(self):
        self._lock = threading.Lock()

        # Lifecycle
        self.logger_started_at: float = time.time()
        self.iracing_connected: bool = False
        self.iracing_in_car: bool = False
        self.last_telemetry_ts: Optional[float] = None
        self.session_id: Optional[str] = None

        # Latest telemetry snapshot
        self.latest_snap: Optional[Dict[str, Any]] = None

        # Session info (car/track/etc)
        self.session_info: Dict[str, Any] = {}

        # Coaching history (rolling)
        self.coaching_events: deque = deque(maxlen=50)

        # Voice queue / latest spoken
        self.last_voice_queued: Optional[Dict[str, Any]] = None  # {ts, text}
        self.last_voice_spoken: Optional[Dict[str, Any]] = None  # {ts, text}

        # Failure / status messages
        self.last_error: Optional[str] = None
        self.status_message: str = "Logger started, waiting for iRacing"

    # ── Telemetry ─────────────────────────────────────────
    def update_telemetry(self, snap: Dict[str, Any]):
        with self._lock:
            self.latest_snap = snap
            self.last_telemetry_ts = time.time()
            # in_car heuristic: on track and a positive lap time or moving
            self.iracing_in_car = bool(
                snap.get("is_on_track")
                or snap.get("speed_ms", 0) > 0.5
                or snap.get("lap", 0) > 0
            )

    def set_iracing_connected(self, connected: bool):
        with self._lock:
            self.iracing_connected = connected
            if connected:
                self.status_message = "iRacing connected, waiting for in-car telemetry"
            else:
                self.status_message = "iRacing disconnected"
                self.iracing_in_car = False

    def set_session_info(self, info: Dict[str, Any]):
        with self._lock:
            self.session_info = info or {}

    def set_session_id(self, sid: str):
        with self._lock:
            self.session_id = sid

    # ── Coaching ──────────────────────────────────────────
    def push_coaching(self, kind: str, severity: str, summary: str,
                      spoken: Optional[str] = None, source: str = "auto"):
        with self._lock:
            self.coaching_events.appendleft({
                "ts": time.time(),
                "kind": kind,
                "severity": severity,
                "summary": summary,
                "spoken": spoken,
                "source": source,  # "auto" | "test" | "manual"
            })

    # ── Voice ─────────────────────────────────────────────
    def voice_queued(self, text: str):
        with self._lock:
            self.last_voice_queued = {"ts": time.time(), "text": text}

    def voice_spoken(self, text: str):
        with self._lock:
            self.last_voice_spoken = {"ts": time.time(), "text": text}

    # ── Errors ────────────────────────────────────────────
    def set_error(self, msg: Optional[str]):
        with self._lock:
            self.last_error = msg

    def set_status(self, msg: str):
        with self._lock:
            self.status_message = msg

    # ── Snapshot for HTTP ─────────────────────────────────
    def health_snapshot(self) -> Dict[str, Any]:
        with self._lock:
            return {
                "ok": True,
                "logger_started_at": self.logger_started_at,
                "uptime_sec": time.time() - self.logger_started_at,
                "iracing_connected": self.iracing_connected,
                "iracing_in_car": self.iracing_in_car,
                "last_telemetry_ts": self.last_telemetry_ts,
                "telemetry_age_sec": (time.time() - self.last_telemetry_ts) if self.last_telemetry_ts else None,
                "session_id": self.session_id,
                "session_info": self.session_info,
                "status_message": self.status_message,
                "last_error": self.last_error,
                "coaching_count": len(self.coaching_events),
                "last_voice_queued": self.last_voice_queued,
                "last_voice_spoken": self.last_voice_spoken,
            }

    def telemetry_snapshot(self) -> Dict[str, Any]:
        with self._lock:
            return {
                "ts": time.time(),
                "iracing_connected": self.iracing_connected,
                "iracing_in_car": self.iracing_in_car,
                "last_telemetry_ts": self.last_telemetry_ts,
                "telemetry_age_sec": (time.time() - self.last_telemetry_ts) if self.last_telemetry_ts else None,
                "session_info": self.session_info,
                "snap": self.latest_snap,
            }

    def coaching_snapshot(self, limit: int = 10) -> Dict[str, Any]:
        with self._lock:
            events = list(self.coaching_events)[:limit]
            return {
                "ts": time.time(),
                "count": len(events),
                "events": events,
                "last_voice_queued": self.last_voice_queued,
                "last_voice_spoken": self.last_voice_spoken,
            }


# ── Singleton ─────────────────────────────────────────────
STATE = State()
