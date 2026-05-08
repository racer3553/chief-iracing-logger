"""Runs Chief logger inside a Windows system tray icon — with live status.

Right-click menu:
    • Status:  iRacing/Telemetry/Cloud at-a-glance (greyed-out items)
    • Open Live Status / Open Dashboard / Open Diagnostics
    • Pause / Resume coaching
    • Auto-start with Windows (toggle)
    • Check for updates
    • Re-link token / Quit

Uses pystray (which uses pywin32 on Windows). Falls back to plain console mode
if pystray is unavailable (developer environment).
"""
import logging
import threading
import time
import webbrowser
from typing import Callable, Optional

try:
    import pystray
    from pystray import MenuItem as Item, Menu
    from PIL import Image, ImageDraw
    HAS_TRAY = True
except ImportError:
    HAS_TRAY = False

from companion import autostart, token_store

log = logging.getLogger("chief.tray")

LIVE_URL = "https://chiefracing.com/dashboard/sim-racing/live-status"
DIAG_URL = "https://chiefracing.com/dashboard/diagnostics"
DOWNLOAD_URL = "https://chiefracing.com/dashboard/download"


def _icon_for(state: str) -> "Image.Image":
    """Generate an icon coloured by current state.

    state ∈ {'live','idle','offline'}.
    Live = green flag (driving), Idle = grey flag (no session), Offline = red flag.
    """
    palette = {
        "live":    (163, 255, 0, 255),
        "idle":    (136, 146, 164, 255),
        "offline": (239, 68, 68, 255),
    }.get(state, (136, 146, 164, 255))

    img = Image.new("RGBA", (64, 64), (12, 13, 18, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((14, 8, 18, 56), fill=(232, 236, 244, 255))
    d.polygon([(18, 10), (56, 10), (56, 36), (18, 36)], fill=palette)
    if state == "live":
        d.rectangle((18, 22, 56, 26), fill=(34, 211, 238, 255))
    return img


def run_tray(
    state_provider: Optional[Callable[[], dict]] = None,
    on_pause: Optional[Callable[[], None]] = None,
    on_resume: Optional[Callable[[], None]] = None,
    on_quit: Optional[Callable[[], None]] = None,
    version_provider: Optional[Callable[[], Optional[dict]]] = None,
):
    """Blocks until the user picks Quit. Run this on the main thread."""
    if not HAS_TRAY:
        print("[tray] pystray not installed — running headless")
        return

    paused = {"v": False}
