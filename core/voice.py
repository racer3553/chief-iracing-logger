"""Voice/TTS layer -- async queue so coach calls never block the telemetry loop.

Uses Windows SAPI via pyttsx3 (free, offline, instant). Swap to ElevenLabs later.
Also pushes voice activity into shared STATE so the web app can see it.
"""
import threading
import queue
import logging

import pyttsx3

import config
from core.state import STATE

log = logging.getLogger("chief.voice")


class Voice:
    def __init__(self):
        self._q = queue.Queue()
        self._thread = threading.Thread(target=self._run, daemon=True, name="ChiefVoice")
        self._engine = None
        self._started = False
        self._enabled = True

    def start(self):
        if self._started:
            return
        self._started = True
        self._thread.start()
        log.info("Voice engine started.")

    def disable(self):
        """Used in --no-voice mode. Logs/state still updated; no audio played."""
        self._enabled = False

    def _init_engine(self):
        eng = pyttsx3.init()
        eng.setProperty("rate", config.VOICE_RATE)
        eng.setProperty("volume", config.VOICE_VOLUME)
        voices = eng.getProperty("voices")
        if voices and 0 <= config.VOICE_INDEX < len(voices):
            eng.setProperty("voice", voices[config.VOICE_INDEX].id)
        return eng

    def _run(self):
        if not self._enabled:
            return
        try:
            self._engine = self._init_engine()
        except Exception as e:
            log.error(f"Voice init failed: {e}")
            STATE.set_error(f"Voice engine failed to init: {e}")
            return
        while True:
            text = self._q.get()
            if text is None:
                break
            try:
                while self._q.qsize() > 2:
                    drained = self._q.get_nowait()
                    if drained is None:
                        return
                    text = drained
                log.info(f"VOICE SPEAK: {text}")
                self._engine.say(text)
                self._engine.runAndWait()
                STATE.voice_spoken(text)
            except Exception as e:
                log.warning(f"TTS speak failed: {e}")
                STATE.set_error(f"TTS error: {e}")

    def say(self, text):
        if not text:
            return
        log.info(f"VOICE QUEUED: {text}")
        STATE.voice_queued(text)
        if self._enabled:
            self._q.put(text)
        else:
            STATE.voice_spoken(text)

    def stop(self):
        self._q.put(None)


def list_voices_console():
    """Run `python -m core.voice` to see voice indexes installed on this PC."""
    eng = pyttsx3.init()
    for i, v in enumerate(eng.getProperty("voices")):
        print(f"[{i}] {v.name}  ({v.id})")


if __name__ == "__main__":
    list_voices_console()
