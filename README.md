# CHIEF — Live AI Crew Chief for iRacing

Real-time in-ear coaching while you drive. Reads iRacing telemetry at 60Hz, detects coachable events (slow laps, off-tracks, fuel, tire temps, incidents), sends context to Claude, and speaks short radio calls back through your speakers/headset.

## TONIGHT — Home PC Setup (15 min)

You're building this on your work PC. iRacing is on your home PC. Here's how to ship it home tonight.

### 1. Get the folder onto your home PC (pick one)

**Option A — USB stick (fastest, no internet needed):**
1. Copy the entire `chief-iracing-logger` folder to a USB stick
2. Plug into home PC, copy to `C:\chief-iracing-logger\` (or wherever)

**Option B — Git (if this folder is a repo):**
```
# On work PC:
cd C:\Users\benw\Desktop\chief-iracing-logger
git add .
git commit -m "live coach v1"
git push

# On home PC:
git clone <your-repo-url> C:\chief-iracing-logger
```

**Option C — OneDrive / Dropbox / Google Drive:**
Drop the folder into your synced cloud folder, wait for it to sync, pull on home PC.

### 2. On your home PC — install once

1. Install **Python 3.10+** from https://python.org (check "Add to PATH" during install)
2. Open the `chief-iracing-logger` folder
3. Copy `.env.example` → `.env`
4. Open `.env` in Notepad and paste your `ANTHROPIC_API_KEY` (grab it from `chief-final/env-folder-backup/.env.local`)

### 3. Pre-flight test (no iRacing needed)

Double-click **`test.bat`**.

First run installs dependencies (~60 sec). Then it fires 5 fake events. You should:
- See log lines for each event
- **Hear Chief speak 5 short radio calls** through your default audio device

If you hear voice → you're 100% ready. If not, see Troubleshooting below.

### 4. Live run

1. Double-click **`run.bat`** (Chief waits for iRacing to launch)
2. Launch iRacing, load any session
3. Drive. Chief talks to you.
4. Close iRacing or press `Ctrl+C` in the chief window when done — it auto-syncs laps to your dashboard if `CHIEF_SYNC_URL` is set.

That's it.

## Command Flags

```
python chief.py                  # normal live mode
python chief.py --simulate       # fake events (use this to demo / test)
python chief.py --no-voice       # text only, prints instead of speaks
python chief.py --no-claude      # zero API cost — uses canned phrases only
```

## What Triggers a Call

Rule-based event detector (cheap, instant). Claude is only called when an event fires.

| Event | When | Severity |
|---|---|---|
| `first_lap` | Lap 1 completed | info |
| `great_lap` | Lap within 0.05s of best | info |
| `slow_lap` | Lap > 0.6s off best | high |
| `off_track` | Player car leaves the track surface | high |
| `incident` | iRacing incident counter ticks up | high / critical |
| `fuel_low` | Fuel < 20% (high) or < 10% (critical) | high / critical |
| `tire_hot` | Any tire > 110°C | high |

Guardrails (`config.py` / `.env`):
- `COACH_MIN_INTERVAL_SEC=8` — minimum seconds between calls
- `COACH_MAX_CALLS_PER_LAP=4` — never more than 4 calls per lap
- Critical events bypass the throttle

## File Structure

```
chief-iracing-logger/
├── chief.py               # main entry — start this
├── config.py              # loads .env settings
├── requirements.txt       # pip deps
├── run.bat                # double-click launcher (live)
├── test.bat               # double-click pre-flight test
├── .env.example           # copy → .env, fill in API key
├── .gitignore
└── core/
    ├── telemetry.py       # iRacing SDK reader (60Hz)
    ├── events.py          # rule-based coachable event detector
    ├── coach.py           # Claude API caller
    ├── voice.py           # pyttsx3 TTS (offline, instant)
    └── session.py         # lap state + post-session sync payload
```

## Voice Selection

Default uses your Windows default voice. To pick a different one:

```
cd C:\chief-iracing-logger
venv\Scripts\activate
python -m core.voice
```

That prints all installed voices. Set `VOICE_INDEX=N` in `.env` to use voice `N`.

Want a better voice? Install Microsoft's premium "neural" voices via Windows Settings → Time & Language → Speech → Manage voices. They appear in the list automatically.

For top-tier voice (ElevenLabs / OpenAI TTS) — that's a 1-hour upgrade for v2.

## Dashboard Sync (Optional)

After each session, Chief can POST your laps to chief-final. In `.env`:

```
CHIEF_SYNC_URL=https://chiefracing.com/api/sim/sync-session
CHIEF_SYNC_TOKEN=<your-bearer-token-if-required>
```

Note: that endpoint currently uses Supabase auth cookies, not a bearer token. If you want server-to-server sync, we add a service-key endpoint next — it's a 10-minute change.

## Troubleshooting

**"Python not found"** — install Python 3.10+ from python.org, check "Add to PATH"

**"Could not find a version that satisfies pyirsdk"** — make sure you're on Windows with Python 3.10+ (not 3.13+ until pyirsdk updates)

**No voice plays** — open Windows Sound settings, confirm default playback device is your speakers/headset. Re-run `test.bat`.

**Voice plays but Chief never talks during driving** — iRacing must be running with you in a session. Check the chief window log for "Connected to iRacing." If it never connects, run iRacing **as Administrator** the first time.

**Claude returns nothing / 401** — your API key is wrong. Double-check `.env`. The key from `chief-final/env-folder-backup/.env.local` is the right one.

**Chief talks too much / too little** — tune `COACH_MIN_INTERVAL_SEC` (default 8) and `COACH_MAX_CALLS_PER_LAP` (default 4) in `.env`.

## What's NOT in v1 (next-build candidates)

- Push-to-talk: hold a button, ask Chief a question ("how's my fuel?")
- Live setup advice mid-stint based on tire temps + lap deltas
- Sector-by-sector delta detection (needs track maps)
- Premium voice (ElevenLabs)
- Live dashboard streaming (websocket → Elite Coach UI updates while driving)
- Fuel/strategy math (laps to empty, save % needed)

These are all 1–3 hour additions once tonight's loop is proven.
