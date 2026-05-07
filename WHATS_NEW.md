# Chief v2 — What's New

## TL;DR — pull from Google Drive, run `run.bat` again. New things:

1. **Sector-by-sector live deltas** (10 sectors per lap) — Chief tells you EXACTLY where you're losing time
2. **Push-to-talk** — hold **F8** in iRacing, ask any question, get spoken answer
3. **Auto setup capture** — every time you save a setup in iRacing, Chief archives it tagged by car/track/best lap
4. **Setup recall** — when you go to a track again, Chief speaks: "I have 3 saved setups for this combo, best lap 14.2"
5. **Restore from web app** — click "Restore" on any saved setup → it appears in iRacing's setup picker
6. **Smart event types** — sector_loss, great_sector, coast detection, plus chatty mode positive feedback

## How to upgrade (5 minutes)

1. Open **Google Drive** → re-download `chief-iracing-logger` folder (overwrite local copy)
2. **DO NOT delete** your existing `.env` file or `setups_archive/` folder if they exist
3. Open Command Prompt at the folder
4. Run: `venv\Scripts\activate`
5. Run: `pip install -r requirements.txt --upgrade`
   - This pulls in: `pynput`, `sounddevice`, `numpy`, `faster-whisper` (~400MB total, one-time)
   - First whisper model download happens on first PTT use (~75MB)
6. Double-click **run.bat**
7. Console should now show extra startup lines:
   - `Sector tracker: 10 sectors per lap.`
   - `Setup archive: <path> (0 setups indexed)`
   - `Setup watcher: monitoring C:\...\Documents\iRacing\setups`
   - `PTT hotkey: HOLD F8 to talk to Chief.`

## How to use the new features

### Sector deltas
Just drive. After 1-2 laps, sector best times start populating. When you lose >0.10s in any sector vs your own best, Chief calls it out by sector number with specific actionable advice (Claude generates the advice based on race state).

Watch the **SECTOR DELTAS** card on the live-status page — colored grid of all 10 sectors with current delta vs your best.

### Push-to-talk
1. Make sure iRacing is in focus (or any window)
2. Hold **F8** key
3. Speak: "How's my fuel?" / "Where am I losing time?" / "What setup change for tight center?"
4. Release F8
5. Chief transcribes locally (faster-whisper, no API cost) → sends transcript + race context to Claude → speaks the reply through your speakers
6. Whole loop: ~2-3 seconds

You can also test it from the web app — there's an **ASK CHIEF** card with a text input.

To change the hotkey: edit `.env`, set `PTT_HOTKEY=f7` (or any single letter, or any function key). Restart logger.

To map to a wheel button: use SimHub or the iRacing Controllers app to map the wheel button to send F8 keystroke. Chief will pick it up.

### Setup auto-capture
The watcher monitors `Documents\iRacing\setups\` recursively. Whenever you hit "Save Setup" inside iRacing and a new .sto file is written, Chief copies it into `setups_archive/<car_slug>/<track_slug>/` and tags it with:
- Timestamp
- Current car name + iRacing car_path
- Current track name
- Your best lap time so far in this session
- Session type (Practice/Qualifying/Race)

Index lives at `setups_archive/index.json`.

### Setup recall
When you load any iRacing session, Chief checks the archive for matching car+track combo. If matches found:
- Chief SPEAKS over your headset: "I have 3 saved setups for this combo. Best lap on file: 28.450 seconds."
- The web app **SAVED SETUPS** card lists them sorted by best lap
- Click **Restore** on any → Chief copies that .sto back into your iRacing setups folder
- Open iRacing's garage → Load Setup → it's there, named with timestamp + lap time

### Verbosity tuning
In `.env`:
- `COACH_VERBOSITY=quiet` — only critical events (incidents, fuel, off-track)
- `COACH_VERBOSITY=normal` — all standard events
- `COACH_VERBOSITY=chatty` — everything + sector confirmations + coast warnings + positive reinforcement (DEFAULT — fixes "wasn't talkative enough" feedback)

## New HTTP endpoints (for power users)

```
GET  /sectors                     # live sector tracker snapshot
GET  /setups                      # full library
GET  /setups/recommend            # setups for current car/track combo
POST /setups/{id}/restore         # copy archived setup back into iRacing
POST /test/ptt-question           # simulate a PTT question (text input)
```

Existing endpoints unchanged: `/health`, `/telemetry/latest`, `/coaching/latest`, `/test/coaching-event`.

## Web app

The Live Status page (`/dashboard/sim-racing/live-status`) now shows:
- **SECTOR DELTAS** — colored grid of 10 sectors with current delta
- **ASK CHIEF** — text input → spoken Claude reply
- **SAVED SETUPS** — list of archived setups for current car/track with Restore buttons
- Plus everything from v1

You'll need to redeploy chief-final to Vercel to see these on chiefracing.com:
```
cd chief-final
vercel --prod
```

If you're running locally with `npm run dev`, just refresh the page.

## Troubleshooting v2 specifically

**PTT hotkey doesn't trigger:**
- Make sure F8 isn't already mapped in iRacing controls (check Options → Controls → search for F8)
- If iRacing is fullscreen exclusive, pynput hotkey can be blocked. Run iRacing in **Fullscreen Windowed** instead.
- Test from web app first (ASK CHIEF card) — that bypasses the hotkey

**faster-whisper install fails:**
- Need Microsoft Visual C++ Redistributable installed (https://aka.ms/vs/17/release/vc_redist.x64.exe)
- After install, retry: `pip install faster-whisper --upgrade`

**Setup watcher doesn't see saved files:**
- Check console — should say "Setup watcher: monitoring C:\...\Documents\iRacing\setups"
- If wrong path: set `IRACING_SETUPS_PATH=C:\actual\path` in `.env`
- Files only get archived on CHANGE — files that already existed when Chief started are recorded but not re-archived

**"sector deltas all show '—'":**
- Need to complete at least one full lap first (so each sector has a 'best' to compare to)
- Cross-checks: confirm `is_on_track=true` and `on_pit_road=false` in /telemetry/latest

**Web app doesn't show new cards:**
- Did you redeploy chief-final to Vercel? `cd chief-final && vercel --prod`
- Or refresh chiefracing.com (hard reload: Ctrl+Shift+R)

## What's still NOT in v2 (next builds)

- ElevenLabs premium voice (replaces Windows TTS — sounds like a real person)
- One-click .exe installer (PyInstaller bundle, no Python install for friends)
- System tray icon (no console window)
- Track maps with corner names (so Chief says "Turn 3" instead of "sector 4")
- Friend tokens / multi-user (so chiefracing.com can show YOUR friend's live coaching)
- Setup-tuning suggestions based on tire temp + handling description

## Recap of feature scope

**v1 (last night):**
- Logger HTTP server, /health, /telemetry/latest, /coaching/latest, /test/coaching-event
- Live Status web page on chiefracing.com
- Voice via Windows TTS
- Auto event detection: first_lap, slow_lap, off_track, incident, fuel_low, tire_hot, great_lap

**v2 (this build):**
- 10-sector tracking + per-sector delta events
- Push-to-talk via F8 + faster-whisper local STT
- Auto setup capture/archive/recall
- Setup restore (copy back to iRacing)
- Chatty verbosity mode (default)
- New HTTP endpoints: /sectors, /setups, /setups/recommend, /setups/{id}/restore, /test/ptt-question
- Updated web app cards: SECTOR DELTAS, ASK CHIEF, SAVED SETUPS

**v3 (next):**
- Premium voice
- Single .exe installer
- Track maps + corner naming
