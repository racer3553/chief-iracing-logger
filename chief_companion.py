"""Chief Companion — single-binary entrypoint.

Behavior:
    * If no token saved → run setup wizard (Tk dialog) → save token → enable autostart → start tray
    * If token saved → fetch cloud config → start logger in background → start system tray
    * Tray icon (green flag) gives user: Open Live Status, Pause, Auto-start toggle, Quit

This is the file PyInstaller bundles into ChiefCompanion.exe.
"""
import logging
import os
import sys
import threading
import time
from pathlib import Path

# Make sure ./ (the install dir) is on sys.path so `import chief` and `import core` work
HERE = Path(__file__).parent.resolve()
sys.path.insert(0, str(HERE))

from companion import token_store, setup_wizard, tray, cloud_config, autostart

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)
log = logging.getLogger("chief.companion")


def _apply_cloud_config(cfg: dict) -> None:
    """Inject cloud config values into env so config.py picks them up on import."""
    if not cfg:
        return
    mapping = {
        "anthropic_api_key": "ANTHROPIC_API_KEY",
        "coach_verbosity": "COACH_VERBOSITY",
        "coach_min_interval_sec": "COACH_MIN_INTERVAL_SEC",
        "coach_max_calls_per_lap": "COACH_MAX_CALLS_PER_LAP",
        "voice_rate": "VOICE_RATE",
        "voice_volume": "VOICE_VOLUME",
        "voice_index": "VOICE_INDEX",
        "ptt_hotkey": "PTT_HOTKEY",
        "ptt_enabled": "PTT_ENABLED",
        "sector_count": "SECTOR_COUNT",
    }
    for k, env_key in mapping.items():
        v = cfg.get(k)
        if v is not None and str(v) != "":
            os.environ[env_key] = str(v)


def _start_logger():
    """Imports and runs the existing chief.py main loop in this thread."""
    # Lazy import so cloud config + env vars are applied first
    import chief
    # Neutralize argv so chief's argparse uses defaults (live mode, port 5188).
    saved = sys.argv
    sys.argv = ["chief"]
    try:
        chief.main()
    except SystemExit:
        log.info("Chief logger exited cleanly.")
    except Exception:
        log.exception("Chief logger crashed.")
    finally:
        sys.argv = saved


def main():
    # Step 1 — first-run gate
    if not token_store.get_token():
        log.info("No token found — launching setup wizard.")
        ok = setup_wizard.run()
        if not ok:
            log.info("Setup cancelled — exiting.")
            sys.exit(0)

    # Make sure autostart is on (idempotent, harmless if user had toggled it off they can re-toggle from tray)
    if not autostart.is_enabled():
        autostart.enable()

    # Step 2 — pull cloud config
    cfg = cloud_config.fetch_config() or {}
    _apply_cloud_config(cfg)
    log.info(f"Cloud config loaded ({len(cfg)} keys).")

    # Step 3 — boot the logger in a background thread
    logger_thread = threading.Thread(
        target=_start_logger, daemon=True, name="ChiefLoggerMain"
    )
    logger_thread.start()
    log.info("Chief logger started in background.")

    # tiny grace period so the logger's HTTP server is up before tray opens "Live Status"
    time.sleep(2)

    # Step 4 — start heartbeat + diagnostics-feeding state provider
    from core import cloud_coach as _cc
    from core import health as _health
    from core.state import STATE as _STATE

    # State provider for both the heartbeat (sent to server) and the tray icon.
    def _diag():
        try:
            return _health.collect(_STATE)
        except Exception:
            return {}

    hb = _cc.HeartbeatThread(state_provider=lambda: {
        "iracing_connected": (_diag().get("iracing") or {}).get("ok", False),
        "telemetry_active":  (_diag().get("telemetry") or {}).get("ok", False),
    })
    hb.start()
    log.info("Heartbeat thread started.")

    # Offline buffer flusher — drains queued coaching events whenever cloud is back.
    from core import offline_buffer
    flusher = offline_buffer.FlushThread()
    flusher.start()
    log.info("Offline flush thread started.")

    def on_quit():
        log.info("Tray quit — shutting down.")
        try: hb.stop()
        except Exception: pass
        try: flusher.stop()
        except Exception: pass
        os._exit(0)

    tray.run_tray(
        state_provider=_diag,
        version_provider=_cc.latest_version,
        on_quit=on_quit,
    )


if __name__ == "__main__":
    main()
