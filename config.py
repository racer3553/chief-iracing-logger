"""Chief — runtime configuration loaded from .env"""
import os
from dotenv import load_dotenv

load_dotenv()

# Anthropic
ANTHROPIC_API_KEY = os.getenv("ANTHROPIC_API_KEY", "").strip()
ANTHROPIC_MODEL = "claude-sonnet-4-6"
ANTHROPIC_URL = "https://api.anthropic.com/v1/messages"

# Optional dashboard sync (post-session)
CHIEF_SYNC_URL = os.getenv("CHIEF_SYNC_URL", "").strip()
CHIEF_SYNC_TOKEN = os.getenv("CHIEF_SYNC_TOKEN", "").strip()

# Voice
VOICE_RATE = int(os.getenv("VOICE_RATE", "180"))
VOICE_VOLUME = float(os.getenv("VOICE_VOLUME", "1.0"))
VOICE_INDEX = int(os.getenv("VOICE_INDEX", "0"))

# Coaching tuning
COACH_VERBOSITY = os.getenv("COACH_VERBOSITY", "chatty").lower()  # quiet | normal | chatty
COACH_MIN_INTERVAL_SEC = float(os.getenv("COACH_MIN_INTERVAL_SEC", "6"))
COACH_MIN_DELTA_SEC = float(os.getenv("COACH_MIN_DELTA_SEC", "0.20"))
COACH_MAX_CALLS_PER_LAP = int(os.getenv("COACH_MAX_CALLS_PER_LAP", "6"))
COACH_SECTOR_LOSS_THRESHOLD = float(os.getenv("COACH_SECTOR_LOSS_THRESHOLD", "0.10"))
SECTOR_COUNT = int(os.getenv("SECTOR_COUNT", "10"))

# Push-to-talk
PTT_ENABLED = os.getenv("PTT_ENABLED", "true").lower() in ("1", "true", "yes")
PTT_HOTKEY = os.getenv("PTT_HOTKEY", "f8").lower()

# Setup archiving
SETUP_ARCHIVE_ENABLED = os.getenv("SETUP_ARCHIVE_ENABLED", "true").lower() in ("1", "true", "yes")
IRACING_SETUPS_PATH = os.getenv("IRACING_SETUPS_PATH", "").strip()  # auto-detected if blank
SETUP_ARCHIVE_DIR = os.getenv("SETUP_ARCHIVE_DIR", "").strip()  # default: ./setups_archive

# Logging
LOG_LEVEL = os.getenv("LOG_LEVEL", "INFO")

# Telemetry tick
TELEMETRY_HZ = 60

# Hardware (SimuCube + SimMagic) auto-archive
HARDWARE_ARCHIVE_ENABLED = os.getenv("HARDWARE_ARCHIVE_ENABLED", "true").lower() in ("1", "true", "yes")
HARDWARE_ARCHIVE_DIR = os.getenv("HARDWARE_ARCHIVE_DIR", "").strip()  # default: ./hardware_archive
HARDWARE_WATCH_PATHS = os.getenv("HARDWARE_WATCH_PATHS", "").strip()  # ; separated overrides
