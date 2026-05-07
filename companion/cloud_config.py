"""Pulls the user's runtime config from chiefracing.com given the install token.

Returns a config dict with the user's Anthropic API key, voice prefs, coach prefs.
If the network call fails, returns whatever was last cached at %APPDATA%\\Chief\\config.json.
"""
import logging
from typing import Optional

import requests

from companion import token_store

log = logging.getLogger("chief.cloud_config")

SERVER_URL = "https://chiefracing.com"


def fetch_config() -> Optional[dict]:
    token = token_store.get_token()
    if not token:
        return None
    try:
        r = requests.get(
            f"{SERVER_URL}/api/companion/config",
            headers={"Authorization": f"Bearer {token}"},
            timeout=10,
        )
        if r.status_code == 200:
            data = r.json()
            if data.get("ok"):
                cfg = data.get("config") or {}
                # cache locally
                local = token_store.load()
                local["last_config"] = cfg
                token_store.save(local)
                return cfg
    except requests.RequestException as e:
        log.warning(f"cloud config fetch failed: {e}")

    cached = token_store.load().get("last_config")
    return cached or None
