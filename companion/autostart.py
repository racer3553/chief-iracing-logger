"""Adds/removes a Windows autostart entry so Chief Companion launches with Windows.

Uses HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run — no admin needed.
"""
import os
import sys
from typing import Optional

RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
APP_NAME = "ChiefCompanion"


def _exe_path() -> str:
    # If frozen by PyInstaller, sys.executable is the .exe itself
    if getattr(sys, "frozen", False):
        return sys.executable
    # Dev mode — use pythonw + entry script (so no console window)
    script = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "chief_companion.py"))
    return f'"{sys.executable}" "{script}"'


def is_enabled() -> bool:
    try:
        import winreg
    except ImportError:
        return False
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_READ) as k:
            val, _ = winreg.QueryValueEx(k, APP_NAME)
            return bool(val)
    except FileNotFoundError:
        return False
    except OSError:
        return False


def enable() -> bool:
    try:
        import winreg
    except ImportError:
        return False
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as k:
            winreg.SetValueEx(k, APP_NAME, 0, winreg.REG_SZ, _exe_path())
        return True
    except OSError:
        return False


def disable() -> bool:
    try:
        import winreg
    except ImportError:
        return False
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as k:
            winreg.DeleteValue(k, APP_NAME)
        return True
    except FileNotFoundError:
        return True
    except OSError:
        return False
