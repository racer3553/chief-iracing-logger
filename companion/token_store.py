"""Persists the user's install token + settings under %APPDATA%\\Chief\\config.json.

This is the single source of truth the .exe reads on boot.  The user only ever pastes
a token once (during first-run setup); after that everything is automatic.
"""
import json
import os
from pathlib import Path
from typing import Optional


def _config_dir() -> Path:
    base = os.getenv("APPDATA") or str(Path.home() / "AppData" / "Roaming")
    p = Path(base) / "Chief"
    p.mkdir(parents=True, exist_ok=True)
    return p


def config_path() -> Path:
    return _config_dir() / "config.json"


def load() -> dict:
    p = config_path()
    if not p.exists():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except Exception:
        return {}


def save(data: dict) -> None:
    p = config_path()
    p.write_text(json.dumps(data, indent=2), encoding="utf-8")


def get_token() -> Optional[str]:
    return load().get("token")


def set_token(token: str) -> None:
    d = load()
    d["token"] = token.strip()
    save(d)


def clear_token() -> None:
    d = load()
    d.pop("token", None)
    save(d)


def get_user() -> Optional[dict]:
    return load().get("user")


def set_user(user: dict) -> None:
    d = load()
    d["user"] = user
    save(d)
