"""Push-to-talk -- hold a hotkey, ask Chief a question, get a spoken reply.

Pipeline:
1. Listen for global hotkey (default F8) via pynput
2. Press: start mic recording (sounddevice)
3. Release: stop recording, transcribe via faster-whisper (tiny.en, local)
4. Send transcript + race context to Claude (coach_question)
5. Speak reply via voice queue

Lazy-loads heavy deps so import never crashes the main loop.
"""
import logging
import threading
import time
from typing import Callable, Optional

log = logging.getLogger("chief.ptt")

_HOTKEY_DEFAULT = "f8"
_SAMPLE_RATE = 16000  # Whisper expects 16kHz
_CHANNELS = 1
_MAX_SECONDS = 15  # cap each PTT recording


class PushToTalk:
    """Hold-to-talk hotkey driver. All heavy imports done lazily."""

    def __init__(self,
                 ctx_provider: Callable[[], dict],
                 voice,
                 on_question: Callable[[str, dict], Optional[str]],
                 hotkey: str = _HOTKEY_DEFAULT):
        self.ctx_provider = ctx_provider
        self.voice = voice
        self.on_question = on_question
        self.hotkey = (hotkey or _HOTKEY_DEFAULT).lower().strip()
        self._listener = None
        self._recording = False
        self._record_thread = None
        self._frames = []
        self._sd = None
        self._whisper_model = None
        self._np = None
        self._enabled = True
        self._key_obj = None

    def start(self):
        try:
            from pynput import keyboard
        except Exception as e:
            log.error(f"pynput unavailable -- PTT disabled. ({e}). Install with: pip install pynput")
            self._enabled = False
            return

        # Resolve hotkey to a Key or KeyCode
        try:
            self._key_obj = self._resolve_key(self.hotkey, keyboard)
        except Exception as e:
            log.error(f"Bad PTT hotkey {self.hotkey!r}: {e}. Disabling PTT.")
            self._enabled = False
            return

        log.info(f"PTT hotkey: HOLD {self.hotkey.upper()} to talk to Chief.")
        self._listener = keyboard.Listener(on_press=self._on_press, on_release=self._on_release)
        self._listener.daemon = True
        self._listener.start()

    @staticmethod
    def _resolve_key(name: str, kb_mod):
        from pynput import keyboard as kb
        nm = name.lower()
        if hasattr(kb.Key, nm):
            return getattr(kb.Key, nm)
        if len(nm) == 1:
            return kb.KeyCode.from_char(nm)
        # f1..f24
        if nm.startswith("f") and nm[1:].isdigit():
            return getattr(kb.Key, nm)
        raise ValueError(f"unknown key: {name}")

    def _key_matches(self, key) -> bool:
        try:
            return key == self._key_obj
        except Exception:
            return False

    def _on_press(self, key):
        if not self._enabled or self._recording or not self._key_matches(key):
            return
        self._recording = True
        self._frames = []
        log.info("PTT: recording...")
        self._record_thread = threading.Thread(target=self._record_loop, daemon=True)
        self._record_thread.start()

    def _on_release(self, key):
        if not self._enabled or not self._recording or not self._key_matches(key):
            return
        self._recording = False
        log.info("PTT: stopped, processing...")
        # Process in a worker thread so the keyboard listener stays responsive
        threading.Thread(target=self._process, daemon=True).start()

    def _record_loop(self):
        try:
            import sounddevice as sd
            import numpy as np
            self._sd = sd
            self._np = np
        except Exception as e:
            log.error(f"sounddevice unavailable -- PTT cannot record ({e}). Install: pip install sounddevice")
            self._recording = False
            return

        try:
            with sd.InputStream(samplerate=_SAMPLE_RATE, channels=_CHANNELS, dtype="float32") as stream:
                start = time.time()
                while self._recording and (time.time() - start) < _MAX_SECONDS:
                    data, _ = stream.read(int(_SAMPLE_RATE * 0.1))  # 100ms chunks
                    self._frames.append(data.copy())
        except Exception as e:
            log.error(f"PTT recording failed: {e}")
            self._recording = False

    def _process(self):
        # Wait for record thread to finish
        if self._record_thread:
            self._record_thread.join(timeout=2)

        if not self._frames:
            log.warning("PTT: no audio captured.")
            return

        try:
            import numpy as np
            audio = np.concatenate(self._frames, axis=0).flatten().astype(np.float32)
        except Exception as e:
            log.error(f"PTT: audio assemble failed: {e}")
            return

        if len(audio) < _SAMPLE_RATE * 0.3:
            log.info("PTT: too short, ignoring.")
            return

        # Transcribe
        transcript = self._transcribe(audio)
        if not transcript:
            log.warning("PTT: transcription returned empty.")
            self.voice.say("Didn't catch that.")
            return

        log.info(f"PTT transcript: {transcript!r}")
        # Build context and ask Claude
        ctx = self.ctx_provider() if self.ctx_provider else {}
        reply = self.on_question(transcript, ctx)
        if not reply:
            self.voice.say("Sorry, can't reach the brain right now.")
            return

        self.voice.say(reply)

    def _transcribe(self, audio_np) -> str:
        """faster-whisper local transcription, lazy-loaded."""
        if self._whisper_model is None:
            try:
                from faster_whisper import WhisperModel
                model_size = "tiny.en"
                log.info(f"Loading Whisper model {model_size} (one-time, ~75MB download if first run)...")
                self._whisper_model = WhisperModel(model_size, device="cpu", compute_type="int8")
                log.info("Whisper model ready.")
            except Exception as e:
                log.error(f"faster-whisper unavailable ({e}). PTT disabled. Install: pip install faster-whisper")
                return ""

        try:
            segments, _info = self._whisper_model.transcribe(
                audio_np, language="en", beam_size=1, vad_filter=False,
            )
            text = " ".join(seg.text for seg in segments).strip()
            return text
        except Exception as e:
            log.error(f"Whisper transcription failed: {e}")
            return ""

    def stop(self):
        if self._listener:
            try:
                self._listener.stop()
            except Exception:
                pass
