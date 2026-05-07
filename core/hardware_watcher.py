"""Hardware-config watcher -- monitors SimuCube True Drive + SimMagic SimPro
profile folders. Whenever the user saves a new profile or tweaks one, Chief
auto-archives a copy with the current car/track context.

Detects defaults:
  SimuCube True Drive:
    %LOCALAPPDATA%\\Granite Devices\\Simucube True Drive\\profiles\\
    %APPDATA%\\Granite Devices\\Simucube True Drive\\profiles\\
  SimMagic SimPro Manager:
    %LOCALAPPDATA%\\SimPro Manager\\
    %APPDATA%\\SimPro Manager\\

If standard paths don't exist, you can override with HARDWARE_WATCH_PATHS
env var (semicolon-separated absolute paths).

Each archived file is tagged with brand + car/track context.
"""
import logging
import os
import threading
from typing import Callable, List, Optional

log = logging.getLogger("chief.hardware_watcher")

SIMUCUBE_REL = [
    os.path.join("Granite Devices", "Simucube True Drive", "profiles"),
    os.path.join("Granite Devices", "Simucube 2", "profiles"),
]
SIMMAGIC_REL = ["SimPro Manager", "SimMagic", "Simagic"]


def _candidates(rel_paths, env_keys=("LOCALAPPDATA", "APPDATA")):
    out = []
    for env in env_keys:
        base = os.environ.get(env)
        if not base:
            continue
        for rel in rel_paths:
            p = os.path.join(base, rel)
            if os.path.isdir(p):
                out.append(p)
    return out


def detect_simucube_dirs() -> List[str]:
    return _candidates(SIMUCUBE_REL)


def detect_simmagic_dirs() -> List[str]:
    return _candidates(SIMMAGIC_REL)


def detect_all() -> List[dict]:
    """Return list of {brand, path} for all detected hardware config dirs."""
    out = []
    for p in detect_simucube_dirs():
        out.append({"brand": "Simucube", "path": p})
    for p in detect_simmagic_dirs():
        out.append({"brand": "SimMagic", "path": p})

    # User overrides via HARDWARE_WATCH_PATHS env (semicolon-separated)
    extra = os.environ.get("HARDWARE_WATCH_PATHS", "").strip()
    if extra:
        for p in extra.split(";"):
            p = p.strip()
            if p and os.path.isdir(p):
                # try to guess brand from path
                low = p.lower()
                brand = "SimMagic" if "sim" in low and ("magic" in low or "pro" in low) \
                    else "Simucube" if "simucube" in low or "granite" in low \
                    else "Other"
                out.append({"brand": brand, "path": p})
    return out


# File extensions worth archiving
HW_EXTS = (".tdp", ".profile", ".json", ".xml", ".cfg", ".ini", ".smprofile", ".scprofile")


class HardwareWatcher:
    """Polls hardware-config dirs for changes. Notifies on new/changed files."""

    def __init__(self,
                 on_change: Callable[[str, str], None],   # (path, brand)
                 dirs: Optional[List[dict]] = None,
                 poll_interval: float = 5.0):
        self.on_change = on_change
        self.dirs = dirs if dirs is not None else detect_all()
        self.poll_interval = poll_interval
        self._thread: Optional[threading.Thread] = None
        self._stop = threading.Event()
        self._known: dict = {}  # path -> mtime

    def start(self):
        if not self.dirs:
            log.warning("No hardware config dirs detected. SimuCube/SimMagic not installed, "
                        "or use HARDWARE_WATCH_PATHS env to specify custom paths.")
            return
        for d in self.dirs:
            log.info(f"Hardware watcher: monitoring {d['brand']} @ {d['path']}")
        self._known = self._scan()
        self._thread = threading.Thread(target=self._loop, daemon=True, name="ChiefHWWatcher")
        self._thread.start()

    def _scan(self) -> dict:
        out = {}
        for d in self.dirs:
            for dirpath, _, filenames in os.walk(d["path"]):
                for fn in filenames:
                    if fn.lower().endswith(HW_EXTS):
                        full = os.path.join(dirpath, fn)
                        try:
                            out[full] = (os.path.getmtime(full), d["brand"])
                        except OSError:
                            pass
        return out

    def _loop(self):
        while not self._stop.is_set():
            try:
                current = self._scan()
                for path, (mtime, brand) in current.items():
                    prev = self._known.get(path)
                    if prev is None or mtime > prev[0] + 0.5:
                        self._notify(path, brand)
                self._known = current
            except Exception as e:
                log.warning(f"Hardware scan failed: {e}")
            self._stop.wait(self.poll_interval)

    def _notify(self, path: str, brand: str):
        try:
            self.on_change(path, brand)
        except Exception as e:
            log.warning(f"Hardware change handler failed: {e}")

    def stop(self):
        self._stop.set()
