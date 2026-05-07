"""Cloud coach proxy — talks to chiefracing.com instead of Anthropic directly.

Used by the .exe build so users never need their own API key.  Falls back to the
direct Claude path in core/coach.py if no token is present (developer mode).

Endpoints used:
    POST /api/companion/coach    — { event_summary, race_context }
    POST /api/companion/ptt      — { transcript, race_context }
    POST /api/companion/heartbeat — { hostname, os, app_version, ... }
    GET  /api/companion/version  — { latest, download_url }
"""
import logging
import os
import platform
import socket
import threading
import time
from typing import Optional

import requests

from companion import token_store

log = logging.getLogger("chief.cloud_coach")

CLOUD_BASE = os.getenv("CHIEF_CLOUD_URL", "https://chiefracing.com").rstrip("/")
APP_VERSION = "1.0.0"


def has_token() -> bool:
    return bool(token_store.get_token())


def _auth_headers() -> dict:
    return {
        "Authorization": f"Bearer {token_store.get_token() or ''}",
        "Content-Type": "application/json",
    }


def coach_call(event_summary: str, race_context: dict) -> Optional[str]:
    """Cloud-proxied event coaching call. Returns spoken phrase or None.

    On network failure, the event is queued for retry by core.offline_buffer.
    """
    try:
        r = requests.post(
            f"{CLOUD_BASE}/api/companion/coach",
            json={"event_summary": event_summary, "race_context": race_context},
            headers=_auth_headers(),
            timeout=12,
        )
        if r.status_code != 200:
            log.warning(f"cloud coach {r.status_code}: {r.text[:200]}")
            _queue_offline(event_summary, race_context)
            return None
        data = r.json()
        if not data.get("ok"):
            return None
        return (data.get("phrase") or "").strip() or None
    except requests.RequestException as e:
        log.warning(f"cloud coach failed: {e}")
        _queue_offline(event_summary, race_context)
        return None


def _queue_offline(event_summary: str, race_context: dict) -> None:
    try:
        from core import offline_buffer
        offline_buffer.enqueue({"summary": event_summary, "ctx": race_context})
    except Exception:
        pass


def coach_question(transcript: str, race_context: dict) -> Optional[str]:
    """Cloud-proxied PTT answer. Returns spoken phrase or None."""
    try:
        r = requests.post(
            f"{CLOUD_BASE}/api/companion/ptt",
            json={"transcript": transcript, "race_context": race_context},
            headers=_auth_headers(),
            timeout=18,
        )
        if r.status_code != 200:
            log.warning(f"cloud ptt {r.status_code}: {r.text[:200]}")
            return None
        data = r.json()
        if not data.get("ok"):
            return None
        return (data.get("phrase") or "").strip() or None
    except requests.RequestException as e:
        log.warning(f"cloud ptt failed: {e}")
        return None


def heartbeat(extra: dict | None = None) -> Optional[dict]:
    """Pings /heartbeat with machine info. Returns server flags or None."""
    if not has_token():
        return None
    body = {
        "hostname": socket.gethostname(),
        "os": platform.platform(),
        "app_version": APP_VERSION,
    }
    if extra:
        body.update(extra)
    try:
        r = requests.post(
            f"{CLOUD_BASE}/api/companion/heartbeat",
            json=body,
            headers=_auth_headers(),
            timeout=8,
        )
        if r.status_code == 200:
            return r.json()
    except requests.RequestException:
        pass
    return None


def latest_version() -> Optional[dict]:
    try:
        r = requests.get(f"{CLOUD_BASE}/api/companion/version", timeout=8)
        if r.status_code == 200:
            return r.json()
    except requests.RequestException:
        pass
    return None


# ---------- Background heartbeat thread ----------
class HeartbeatThread(threading.Thread):
    """Pings /heartbeat every interval seconds in a daemon thread.

    The state_provider() callback returns dict of live flags
    (iracing_connected, telemetry_active, mic_ok, audio_ok) which we
    forward to the server so the diagnostics page can show them.
    """

    def __init__(self, state_provider=None, interval: int = 30):
        super().__init__(daemon=True, name="ChiefHeartbeat")
        self._stop = threading.Event()
        self._state_provider = state_provider
        self.interval = interval
        self.last_response: Optional[dict] = None

    def run(self):
        # Brief startup delay so the logger has a chance to come up first
        time.sleep(2)
        while not self._stop.is_set():
            extra = {}
            try:
                if self._state_provider:
                    extra = self._state_provider() or {}
            except Exception as e:
                log.debug(f"state_provider raised: {e}")
            self.last_response = heartbeat(extra) or self.last_response
            self._stop.wait(self.interval)

    def stop(self):
        self._stop.set()
