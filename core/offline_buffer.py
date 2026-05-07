"""Offline event buffer.

When chiefracing.com is unreachable, coaching events get appended to a JSONL
file at %APPDATA%\\Chief\\offline_queue.jsonl.  A background thread retries
flushing the queue every ~30s.

This means:
- driver still hears local fallback phrases live (no degradation in-car)
- transcripts of every event still reach the cloud once internet returns
- post-session telemetry sync still works
"""
import json
import logging
import os
import threading
import time
from pathlib import Path
from typing import Optional

import requests

from companion import token_store

log = logging.getLogger("chief.offline")

CLOUD_BASE = os.getenv("CHIEF_CLOUD_URL", "https://chiefracing.com").rstrip("/")


def _queue_path() -> Path:
    base = os.getenv("APPDATA") or str(Path.home() / "AppData" / "Roaming")
    p = Path(base) / "Chief"
    p.mkdir(parents=True, exist_ok=True)
    return p / "offline_queue.jsonl"


def enqueue(event: dict) -> None:
    """Append an event to the queue.  Cheap, never raises."""
    try:
        with _queue_path().open("a", encoding="utf-8") as f:
            f.write(json.dumps({"ts": time.time(), **event}) + "\n")
    except Exception as e:
        log.debug(f"enqueue failed: {e}")


def _flush_one(event: dict) -> bool:
    token = token_store.get_token()
    if not token:
        return False
    try:
        r = requests.post(
            f"{CLOUD_BASE}/api/companion/coach",
            json={
                "event_summary": event.get("summary") or event.get("event_summary") or "",
                "race_context": event.get("ctx") or event.get("race_context") or {},
            },
            headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
            timeout=8,
        )
        return r.status_code == 200
    except requests.RequestException:
        return False


def flush_all() -> int:
    """Try to send every queued event. Returns count flushed."""
    p = _queue_path()
    if not p.exists():
        return 0
    flushed = 0
    remaining = []
    try:
        with p.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    ev = json.loads(line)
                except Exception:
                    continue
                if _flush_one(ev):
                    flushed += 1
                else:
                    remaining.append(line)
    except Exception as e:
        log.warning(f"flush read failed: {e}")
        return 0
    try:
        if remaining:
            with p.open("w", encoding="utf-8") as f:
                f.write("\n".join(remaining) + "\n")
        else:
            p.unlink(missing_ok=True)
    except Exception as e:
        log.warning(f"flush write-back failed: {e}")
    if flushed:
        log.info(f"offline buffer: flushed {flushed} events")
    return flushed


class FlushThread(threading.Thread):
    def __init__(self, interval: int = 30):
        super().__init__(daemon=True, name="ChiefOfflineFlush")
        self._stop = threading.Event()
        self.interval = interval

    def run(self):
        time.sleep(5)
        while not self._stop.is_set():
            try:
                flush_all()
            except Exception as e:
                log.debug(f"flush loop: {e}")
            self._stop.wait(self.interval)

    def stop(self):
        self._stop.set()
