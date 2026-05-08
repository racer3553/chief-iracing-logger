"""CHIEF -- live in-ear AI crew chief for iRacing.

v3 features:
- 10-sector live tracking with per-sector deltas
- chatty coach mode (configurable verbosity)
- push-to-talk: hold F8, ask Chief any question, get spoken answer
- automatic iRacing setup capture: every saved .sto auto-archived
- automatic SimuCube + SimMagic profile capture (NEW in v3)
- HTTP endpoints for web app polling

Usage:
    python chief.py              # live mode
    python chief.py --simulate   # fake events
    python chief.py --no-voice
    python chief.py --no-claude
    python chief.py --no-ptt
    python chief.py --no-setup-watch
    python chief.py --no-hw-watch
    python chief.py --port 5188
"""
import argparse
import logging
import os
import signal
import sys
import time
import requests

import config
from core.telemetry import Telemetry
from core.events import EventDetector, CoachEvent
from core.voice import Voice
from core.coach import coach_call, coach_question
from core.session import Session
from core.api_server import ApiServer
from core.state import STATE
from core.sectors import SectorTracker
from core.setup_archive import SetupArchive
from core.setup_watcher import SetupWatcher, default_setups_path
from core.ptt import PushToTalk
from core.hardware_archive import HardwareArchive
from core.hardware_watcher import HardwareWatcher, detect_all as detect_hw_dirs

LOGGER_PORT = 5188


def setup_logging():
    logging.basicConfig(
        level=getattr(logging, config.LOG_LEVEL, logging.INFO),
        format="%(asctime)s | %(name)-18s | %(levelname)-7s | %(message)s",
        datefmt="%H:%M:%S",
    )


def build_context(session, snap, sector_tracker=None, setup_archive=None):
    info = session.info
    ctx = {"car": info.get("car_name") or "", "track": info.get("track_name") or "", "session_type": "Practice"}
    if snap:
        ctx.update({
            "lap": snap.get("lap"), "position": snap.get("position"),
            "best_lap": snap.get("lap_best_time") or session.best_lap_time(),
            "last_lap": snap.get("lap_last_time"), "delta": snap.get("lap_delta_to_best"),
            "fuel_pct": snap.get("fuel_pct"), "fuel_per_lap": snap.get("fuel_use_per_hour"),
            "incidents": snap.get("incidents"),
            "tire_temps": {"LF": snap.get("tire_lf_temp", 0), "RF": snap.get("tire_rf_temp", 0),
                           "LR": snap.get("tire_lr_temp", 0), "RR": snap.get("tire_rr_temp", 0)},
        })
    if sector_tracker is not None:
        s = sector_tracker.snapshot()
        ctx["theoretical_best"] = s.get("theoretical_best", 0.0)
        if s.get("last_completed_sector") is not None:
            ctx["recent_sector"] = {"sector": s["last_completed_sector"],
                                    "time": s["last_sector_time"], "delta": s["last_sector_delta"]}
        best = s.get("best_sector_times", {}) or {}
        worst = (None, 0.0)
        for k, t in (s.get("lap_sector_times_so_far", {}) or {}).items():
            d = t - best.get(k, t)
            if d > worst[1]: worst = (k, d)
        if worst[0] is not None:
            ctx["biggest_loss_sector"] = {"sector": worst[0], "delta": worst[1]}
    if setup_archive is not None:
        rows = setup_archive.for_car_track(car_path=info.get("car_path", ""), track_name=info.get("track_name", ""))
        ctx["saved_setups_for_combo"] = len(rows)
    return ctx


def handle_event(ev, session, voice, snap, use_claude, log,
                 source="auto", sector_tracker=None, setup_archive=None):
    if source == "auto" and not session.can_call() and ev.severity != "critical":
        return
    log.info(f"COACH EVENT [{ev.severity}] {ev.kind}: {ev.summary}")
    spoken = None
    if use_claude:
        spoken = coach_call(ev.summary, build_context(session, snap, sector_tracker, setup_archive))
    if not spoken:
        spoken = _fallback_phrase(ev)
    STATE.push_coaching(ev.kind, ev.severity, ev.summary, spoken=spoken, source=source)
    voice.say(spoken)
    if source == "auto":
        session.mark_call()


def _fallback_phrase(ev):
    table = {
        "great_lap":     "Good lap. Keep it there.",
        "first_lap":     "Baseline set. Build from here.",
        "slow_lap":      "Lap was off. Stay smooth, build it back.",
        "off_track":     "Off track. Bring it back clean.",
        "incident":      "Incident. Stay focused.",
        "fuel_low":      "Fuel is low. Save what you can.",
        "tire_hot":      "Tires are hot. Manage them.",
        "sector_loss":   ev.summary,
        "great_sector":  "Good sector. Hold that line.",
        "coast":         "You're coasting. Pick brake or throttle.",
    }
    return table.get(ev.kind, ev.summary)


def sync_to_dashboard(session, log):
    if not config.CHIEF_SYNC_URL or not session.laps: return
    try:
        headers = {"Content-Type": "application/json"}
        if config.CHIEF_SYNC_TOKEN:
            headers["Authorization"] = f"Bearer {config.CHIEF_SYNC_TOKEN}"
        r = requests.post(config.CHIEF_SYNC_URL, json=session.to_sync_payload(), headers=headers, timeout=15)
        log.info(f"Sync -> {r.status_code}: {r.text[:200]}")
    except Exception as e:
        log.warning(f"Sync failed: {e}")


def make_test_handler(voice, log):
    def _handler(phrase, use_claude=False):
        log.info(f"TEST EVENT: {phrase!r}")
        spoken = coach_call(phrase, {"car":"Test","track":"Test","session_type":"Test"}) if use_claude else None
        if not spoken: spoken = phrase
        STATE.push_coaching("test", "info", phrase, spoken=spoken, source="test")
        voice.say(spoken)
        return {"phrase": phrase, "spoken": spoken, "use_claude": use_claude}
    return _handler


def make_question_handler(voice, log, sector_tracker, setup_archive):
    class _S:
        @property
        def info(s_): return STATE.session_info or {}
        def best_lap_time(s_): return 0.0
    sess = _S()
    def _handler(question_text):
        log.info(f"PTT QUESTION: {question_text!r}")
        ctx = build_context(sess, STATE.latest_snap, sector_tracker, setup_archive)
        reply = coach_question(question_text, ctx) or "Standby."
        STATE.push_coaching("ptt_question", "info", f"Q: {question_text}", spoken=reply, source="ptt")
        voice.say(reply)
        return reply
    return _handler


def make_setup_change_handler(setup_archive, log):
    class _S:
        @property
        def info(s_): return STATE.session_info or {}
        def best_lap_time(s_): return 0.0
    sess = _S()
    def _on_change(path):
        try: mtime = os.path.getmtime(path)
        except OSError: return
        if setup_archive.already_has(path, mtime): return
        info = sess.info
        row = setup_archive.archive(
            source_path=path,
            car_name=info.get("car_name", ""), car_path=info.get("car_path", ""),
            track_name=info.get("track_name", ""),
            best_lap=sess.best_lap_time(), session_type="Practice",
        )
        if row:
            STATE.push_coaching("setup_saved", "info",
                f"Saved iRacing setup for {row.get('car_slug')} @ {row.get('track_slug')}",
                spoken=None, source="auto")
    return _on_change


def make_hw_change_handler(hw_archive, log):
    class _S:
        @property
        def info(s_): return STATE.session_info or {}
        def best_lap_time(s_): return 0.0
    sess = _S()
    def _on_change(path, brand):
        try: mtime = os.path.getmtime(path)
        except OSError: return
        if hw_archive.already_has(path, mtime): return
        info = sess.info
        row = hw_archive.archive(
            source_path=path, brand=brand,
            car_name=info.get("car_name", ""), car_path=info.get("car_path", ""),
            track_name=info.get("track_name", ""),
            best_lap=sess.best_lap_time(),
        )
        if row:
            STATE.push_coaching("hardware_saved", "info",
                f"Saved {brand} profile: {os.path.basename(path)}",
                spoken=None, source="auto")
    return _on_change


def run_simulate(voice, use_claude, log, sector_tracker, setup_archive):
    log.info("SIMULATE MODE")
    STATE.set_status("Simulation mode")
    session = Session()
    info = {"car_name": "NASCAR Cup Next Gen", "car_path": "stockcars/cup", "track_name": "Charlotte"}
    session.attach_info(info); STATE.set_session_info(info); STATE.set_session_id(session.session_id)
    fakes = [
        CoachEvent("first_lap", "info", "First flying lap: 28.450s. Baseline set."),
        CoachEvent("sector_loss", "high", "sector 3: lost 0.182s vs your best (9.241s)."),
        CoachEvent("slow_lap", "high", "Lap 4: 29.120s -- +0.670s vs best."),
        CoachEvent("great_sector", "info", "sector 5: new best (8.892s)."),
        CoachEvent("off_track", "high", "Off track at 65% of lap distance."),
        CoachEvent("fuel_low", "high", "Fuel getting low: 18% remaining."),
    ]
    for ev in fakes:
        handle_event(ev, session, voice, None, use_claude, log,
                     sector_tracker=sector_tracker, setup_archive=setup_archive)
        time.sleep(5)
    log.info("SIMULATE done -- HTTP server still up. Ctrl+C to exit.")
    while True: time.sleep(5)


def run_live(voice, use_claude, log, sector_tracker, setup_archive):
    tele = Telemetry()
    detector = EventDetector()
    session = Session()
    STATE.set_session_id(session.session_id)
    log.info("Waiting for iRacing -- launch and enter a session.")
    STATE.set_status("Waiting for iRacing")
    while not tele.connect():
        time.sleep(2)
    STATE.set_iracing_connected(True)
    log.info("iRacing connected.")
    info = tele.session_info()
    session.attach_info(info); STATE.set_session_info(info)
    log.info(f"Session: {info.get('car_name','?')} @ {info.get('track_name','?')}")
    voice.say("Chief is on the radio. Have a good run.")

    if setup_archive:
        rows = setup_archive.for_car_track(car_path=info.get("car_path", ""), track_name=info.get("track_name", ""))
        if rows:
            best = min((r.get("best_lap", 0) or 0) for r in rows if r.get("best_lap", 0) > 0)
            voice.say(f"I have {len(rows)} saved setups for this combo." +
                     (f" Best lap on file: {best:.3f} seconds." if best > 0 else ""))

    tick = 1.0 / config.TELEMETRY_HZ
    last_info = 0.0; last_log = 0.0
    try:
        while True:
            snap = tele.snapshot()
            if snap is None:
                log.info("iRacing disconnected.")
                STATE.set_iracing_connected(False); break
            STATE.update_telemetry(snap); session.update(snap)

            sec_event = sector_tracker.update(snap) if sector_tracker else None
            if sec_event:
                idx, t, d = sec_event
                for ev in detector.on_sector_completed(idx, t, d, sector_tracker.num_sectors):
                    handle_event(ev, session, voice, snap, use_claude, log,
                                 sector_tracker=sector_tracker, setup_archive=setup_archive)

            if time.time() - last_info > 10:
                fresh = tele.session_info()
                merged = {**session.info, **fresh}
                session.attach_info(merged); STATE.set_session_info(merged)
                last_info = time.time()

            if time.time() - last_log > 10:
                log.info("tick: lap=%s speed=%.0fmph thr=%.0f%% brk=%.0f%%" % (
                    snap.get("lap"), snap.get("speed_ms", 0)*2.237,
                    snap.get("throttle", 0)*100, snap.get("brake", 0)*100))
                last_log = time.time()

            for ev in detector.update(snap):
                handle_event(ev, session, voice, snap, use_claude, log,
                             sector_tracker=sector_tracker, setup_archive=setup_archive)
            time.sleep(tick)
    finally:
        session.ended_at = time.time()
        voice.say("Session over. Logging your laps.")
        sync_to_dashboard(session, log)
        tele.shutdown()


def main():
    p = argparse.ArgumentParser(description="CHIEF v3 -- live AI crew chief for iRacing")
    p.add_argument("--simulate", action="store_true")
    p.add_argument("--no-voice", action="store_true")
    p.add_argument("--no-claude", action="store_true")
    p.add_argument("--no-ptt", action="store_true")
    p.add_argument("--no-setup-watch", action="store_true")
    p.add_argument("--no-hw-watch", action="store_true")
    p.add_argument("--port", type=int, default=LOGGER_PORT)
    args = p.parse_args()

    setup_logging()
    log = logging.getLogger("chief.main")
    log.info("=" * 60)
    log.info("CHIEF v3 starting...")
    log.info("=" * 60)

    if not args.no_claude and not config.ANTHROPIC_API_KEY:
        log.warning("ANTHROPIC_API_KEY missing -- falling back to canned phrases.")
        args.no_claude = True

    voice = Voice()
    if args.no_voice: voice.disable()
    voice.start()
    use_claude = not args.no_claude

    sector_tracker = SectorTracker(num_sectors=config.SECTOR_COUNT)
    log.info(f"Sector tracker: {config.SECTOR_COUNT} sectors per lap.")

    # Setup archive + watcher
    setup_archive = None; setup_watcher = None
    if config.SETUP_ARCHIVE_ENABLED and not args.no_setup_watch:
        archive_dir = config.SETUP_ARCHIVE_DIR or os.path.join(os.path.dirname(os.path.abspath(__file__)), "setups_archive")
        setup_archive = SetupArchive(archive_dir)
        log.info(f"Setup archive: {archive_dir}  ({len(setup_archive._index)} indexed)")
        iracing_root = config.IRACING_SETUPS_PATH or default_setups_path()
        if os.path.isdir(iracing_root):
            setup_watcher = SetupWatcher(root=iracing_root, on_change=make_setup_change_handler(setup_archive, log), poll_interval=3.0)
            setup_watcher.start()
        else:
            log.warning(f"iRacing setups folder not found at {iracing_root} -- setup watcher disabled.")

    # Hardware archive + watcher (NEW in v3)
    hardware_archive = None; hardware_watcher = None; hw_dirs = []
    if config.HARDWARE_ARCHIVE_ENABLED and not args.no_hw_watch:
        hw_dir = config.HARDWARE_ARCHIVE_DIR or os.path.join(os.path.dirname(os.path.abspath(__file__)), "hardware_archive")
        hardware_archive = HardwareArchive(hw_dir)
        log.info(f"Hardware archive: {hw_dir}  ({len(hardware_archive._index)} indexed)")
        hw_dirs = detect_hw_dirs()
        if hw_dirs:
            for d in hw_dirs:
                log.info(f"  detected {d['brand']} @ {d['path']}")
            hardware_watcher = HardwareWatcher(on_change=make_hw_change_handler(hardware_archive, log), dirs=hw_dirs, poll_interval=5.0)
            hardware_watcher.start()
        else:
            log.warning("No SimuCube/SimMagic config dirs detected. Set HARDWARE_WATCH_PATHS in .env to override.")

    test_handler = make_test_handler(voice, log)
    question_handler = make_question_handler(voice, log, sector_tracker, setup_archive)

    api = ApiServer(host="127.0.0.1", port=args.port,
        test_event_handler=test_handler, ptt_question_handler=question_handler,
        setup_archive=setup_archive, iracing_setups_root=(config.IRACING_SETUPS_PATH or default_setups_path()),
        sector_tracker=sector_tracker,
        hardware_archive=hardware_archive, hardware_dirs=hw_dirs)
    api.start()
    log.info(f"Web app endpoint: http://localhost:{args.port}/")

    ptt = None
    if config.PTT_ENABLED and not args.no_ptt:
        class _SS:
            @property
            def info(s_): return STATE.session_info or {}
            def best_lap_time(s_): return 0.0
        def _ctx(): return build_context(_SS(), STATE.latest_snap, sector_tracker, setup_archive)
        ptt = PushToTalk(ctx_provider=_ctx, voice=voice,
                         on_question=lambda t, c: coach_question(t, c) or "Standby.",
                         hotkey=config.PTT_HOTKEY)
        ptt.start()

    def shutdown(*_):
        log.info("Shutting down...")
        if hardware_watcher: hardware_watcher.stop()
        if setup_watcher: setup_watcher.stop()
        if ptt: ptt.stop()
        api.stop(); voice.stop(); sys.exit(0)
    # Signal handlers can only be registered from the main thread.  When chief
    # runs inside the Companion (.exe), main() is called from a worker thread,
    # so just skip them â€” the tray Quit handler covers shutdown there.
    try:
        signal.signal(signal.SIGINT, shutdown)
        signal.signal(signal.SIGTERM, shutdown)
    except (ValueError, AttributeError):
        log.info("Signal handlers skipped (non-main thread, OK under Companion).")

    try:
        if args.simulate:
            run_simulate(voice, use_claude, log, sector_tracker, setup_archive)
        else:
            run_live(voice, use_claude, log, sector_tracker, setup_archive)
    except KeyboardInterrupt: pass
    finally:
        if hardware_watcher: hardware_watcher.stop()
        if setup_watcher: setup_watcher.stop()
        if ptt: ptt.stop()
        api.stop(); voice.stop()


if __name__ == "__main__":
    main()
