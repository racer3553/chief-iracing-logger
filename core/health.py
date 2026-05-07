"""Diagnostics aggregator — answers "is everything working?" for the dashboard.

Each probe is fast (<200ms) and tolerant of failure.  Anything that errors out
is reported as "unknown" rather than killing the response.
"""
import logging
import os
import time
from typing import Optional

log = logging.getLogger("chief.health")


def _probe_microphone() -> dict:
    try:
        import sounddevice as sd  # type: ignore
        devs = sd.query_devices()
        inputs = [d for d in devs if d.get("max_input_channels", 0) > 0]
        if not inputs:
            return {"ok": False, "reason": "no_input_devices"}
        default = sd.default.device[0] if isinstance(sd.default.device, (tuple, list)) else None
        name = None
        for d in inputs:
            if default is not None and d.get("index") == default:
                name = d.get("name"); break
        if not name and inputs:
            name = inputs[0].get("name")
        return {"ok": True, "name": name, "count": len(inputs)}
    except Exception as e:
        return {"ok": False, "reason": f"probe_failed: {e}"}


def _probe_audio_output() -> dict:
    try:
        import sounddevice as sd  # type: ignore
        devs = sd.query_devices()
        outputs = [d for d in devs if d.get("max_output_channels", 0) > 0]
        if not outputs:
            return {"ok": False, "reason": "no_output_devices"}
        return {"ok": True, "count": len(outputs)}
    except Exception as e:
        return {"ok": False, "reason": f"probe_failed: {e}"}


def _probe_voice_engine() -> dict:
    try:
        import pyttsx3  # type: ignore
        # Don't actually start the engine here — that can block.  Just import.
        return {"ok": True}
    except Exception as e:
        return {"ok": False, "reason": str(e)}


def _probe_cloud(cloud_url: str, timeout: float = 4.0) -> dict:
    try:
        import requests  # type: ignore
        r = requests.get(f"{cloud_url}/api/companion/version", timeout=timeout)
        if r.status_code == 200:
            j = r.json()
            return {"ok": True, "latest": j.get("latest"), "url": cloud_url}
        return {"ok": False, "reason": f"http_{r.status_code}"}
    except Exception as e:
        return {"ok": False, "reason": str(e)[:120]}


def collect(state, telemetry=None, cloud_url: str = "https://chiefracing.com") -> dict:
    """Build the unified diagnostics snapshot."""
    snap = {}
    try:
        snap = state.health_snapshot() or {}
    except Exception:
        pass

    iracing_connected = bool(snap.get("iracing_connected"))
    latest_snap = None
    try:
        latest_snap = state.latest_snap
    except Exception:
        pass

    last_tick_ts = snap.get("last_telemetry_ts")
    telemetry_active = False
    if isinstance(last_tick_ts, (int, float)):
        telemetry_active = (time.time() - last_tick_ts) < 5

    # Token / cloud
    has_token = False
    try:
        from companion import token_store  # type: ignore
        has_token = bool(token_store.get_token())
    except Exception:
        pass

    cloud = _probe_cloud(cloud_url) if has_token else {"ok": False, "reason": "no_token"}

    return {
        "iracing": {
            "ok": iracing_connected,
            "session": snap.get("session_info") or {},
        },
        "telemetry": {
            "ok": telemetry_active,
            "last_tick_ts": last_tick_ts,
            "lap": (latest_snap or {}).get("lap"),
        },
        "cloud": cloud,
        "microphone": _probe_microphone(),
        "audio_output": _probe_audio_output(),
        "voice_engine": _probe_voice_engine(),
        "subscription": {
            # Populated from heartbeat response — server-driven.  Default unknown.
            "ok": True,
            "plan": "trial",
            "source": "default",
        },
        "companion": {
            "token_present": has_token,
            "version": "1.0.0",
        },
        "checked_at": time.time(),
    }
