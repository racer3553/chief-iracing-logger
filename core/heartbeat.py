"""Heartbeat: logs system state every 30 sec so we can see if/when things stop."""
import logging, threading, time, os

log = logging.getLogger("chief.heartbeat")
_started = False


def start():
    global _started
    if _started: return
    _started = True
    interval = int(os.getenv("HEARTBEAT_SEC", "30"))
    def run():
        i = 0
        while True:
            i += 1
            try:
                from core import state
                snap = getattr(state, "STATE", None)
                if snap:
                    si = getattr(snap, "session_info", {}) or {}
                    laps = len(getattr(snap, "laps", []) or [])
                    last = getattr(snap, "latest_snap", None)
                    log.info("HB#%d laps=%d connected=%s lap=%s speed=%.1f",
                             i, laps, bool(last),
                             si.get("lap", "?"),
                             (last or {}).get("speed", 0) if isinstance(last, dict) else 0)
                else:
                    log.info("HB#%d running", i)
            except Exception as e:
                log.info("HB#%d (%s)", i, e)
            time.sleep(interval)
    t = threading.Thread(target=run, daemon=True, name="heartbeat")
    t.start()