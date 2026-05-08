"""Resilient TTS - fresh pyttsx3 engine per call, hard timeout, fail-soft."""
import logging, os, threading

log = logging.getLogger("chief.voice")
_fail = 0
_off = False
_lock = threading.Lock()
_idx = int(os.getenv("VOICE_INDEX", "0"))
_rate = int(os.getenv("VOICE_RATE", "175"))
_vol = float(os.getenv("VOICE_VOLUME", "1.0"))
_max_fail = int(os.getenv("VOICE_MAX_FAILURES", "5"))
_timeout = float(os.getenv("VOICE_TIMEOUT_SEC", "12"))


def _do_speak(text):
    import pyttsx3
    eng = pyttsx3.init()
    try:
        try:
            vs = eng.getProperty("voices") or []
            if vs and 0 <= _idx < len(vs):
                eng.setProperty("voice", vs[_idx].id)
        except Exception:
            pass
        eng.setProperty("rate", _rate)
        eng.setProperty("volume", _vol)
        eng.say(text)
        eng.runAndWait()
    finally:
        try: eng.stop()
        except Exception: pass


def speak(text):
    global _fail, _off
    if not text: return True
    if _off:
        log.info("(voice off) %s", text)
        return False
    with _lock:
        r = {"ok": False}
        def go():
            try:
                _do_speak(text)
                r["ok"] = True
            except Exception as e:
                log.warning("voice err: %s", e)
        t = threading.Thread(target=go, daemon=True)
        t.start()
        t.join(timeout=_timeout)
        if t.is_alive():
            log.warning("voice TIMEOUT after %ss: %s", _timeout, text[:60])
            _fail += 1
        elif r["ok"]:
            _fail = 0
            return True
        else:
            _fail += 1
        if _fail >= _max_fail:
            _off = True
            log.error("VOICE DISABLED after %s failures - text-only mode now", _fail)
        return False


def stop(): pass


def list_voices():
    try:
        import pyttsx3
        e = pyttsx3.init()
        for i, v in enumerate(e.getProperty("voices") or []):
            print(f"{i}: {v.name} ({v.id})")
        e.stop()
    except Exception as e:
        print(f"err: {e}")


if __name__ == "__main__":
    list_voices()