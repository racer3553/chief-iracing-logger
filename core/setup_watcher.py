"""Setup watcher -- monitors iRacing's setups folder and auto-archives new .sto files.

Default iRacing setups path on Windows:
    C:\\Users\\<you>\\Documents\\iRacing\\setups\\<car_path>\\<name>.sto

Whenever a .sto file is created/modified, we archive a copy into Chief's library,
tagged with the current iRacing car/track/best_lap context.

Uses the watchdog library if available, falls back to a simple polling watcher.
"""
import logging
import os
import threading
import time
from typing import Callable, Optional

log = logging.getLogger("chief.setup_watcher")


def default_setups_path() -> str:
    return os.path.join(os.path.expanduser("~"), "Documents", "iRacing", "setups")


class SetupWatcher:
    """Polls iRacing setups folder and notifies on new/changed .sto files.

    Polling-based (no watchdog dependency) for max reliability. Cheap: ~50ms scan.
    """

    def __init__(self,
                 root: str,
                 on_change: Callable[[str], None],
                 poll_interval: float = 3.0):
        self.root = root
        self.on_change = on_change
        self.poll_interval = poll_interval
        self._thread = None
        self._stop = threading.Event()
        self._known: dict = {}  # path -> mtime

    def start(self):
        if not os.path.isdir(self.root):
            log.warning(f"Setup watcher root not found: {self.root} -- watcher disabled.")
            return
        log.info(f"Setup watcher: monitoring {self.root}")
        # Initial scan -- mark all existing files as 'known' so we don't archive them all on start.
        # But still record their mtimes so we detect future updates.
        self._known = self._scan()
        self._thread = threading.Thread(target=self._loop, daemon=True, name="ChiefSetupWatcher")
        self._thread.start()

    def _scan(self) -> dict:
        out = {}
        for dirpath, dirnames, filenames in os.walk(self.root):
            for fn in filenames:
                if fn.lower().endswith(".sto"):
                    full = os.path.join(dirpath, fn)
                    try:
                        out[full] = os.path.getmtime(full)
                    except OSError:
                        pass
        return out

    def _loop(self):
        while not self._stop.is_set():
            try:
                current = self._scan()
                # New files
                for path, mtime in current.items():
                    if path not in self._known:
                        self._notify(path)
                    elif mtime > self._known[path] + 0.5:
                        self._notify(path)
                self._known = current
            except Exception as e:
                log.warning(f"Setup watcher scan failed: {e}")
            self._stop.wait(self.poll_interval)

    def _notify(self, path: str):
        try:
            self.on_change(path)
        except Exception as e:
            log.warning(f"Setup change handler failed: {e}")

    def stop(self):
        self._stop.set()
