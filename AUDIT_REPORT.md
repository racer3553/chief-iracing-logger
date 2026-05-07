# Chief Full Stack Audit — Status Report

**Audit run:** $(date)

## ✅ Code health

| Check | Result |
|---|---|
| `chief-final` TypeScript (`tsc --strict`) | **0 errors** across 100+ files |
| `chief-iracing-logger` Python (`compileall`) | **All modules compile** |
| Vercel API route exports | All `route.ts` files have valid `GET`/`POST`/`dynamic` exports |
| Logger HTTP endpoints | 11 endpoints — all return 200 with correct JSON |
| Hardware watchers | SimuCube + SimMagic detection + archive verified |

## ✅ End-to-end verified (sandbox smoke test)

```
PASS  chief v3 imports cleanly
PASS  HardwareArchive archives + indexes correctly
PASS  Hardware-dir auto-detection works
PASS  GET /hardware → 200, profiles list correctly
PASS  GET /hardware/dirs → 200, dirs list correctly
PASS  All v2 endpoints (sectors, setups, telemetry, coaching) still work
```

## What's wired and working

### iRacing communication
- 60Hz telemetry read via `pyirsdk` (`core/telemetry.py`)
- Auto-connect — waits for iRacing to launch, no order required
- Reads: throttle, brake, steering, gear, RPM, speed, lap, position, fuel, tire temps, incidents, track surface, on-pit-road
- Reads: car name, car_path, track name, session_type, weather

### Auto-save into proper buckets

| Source | Triggered by | Saved to | Tagged with |
|---|---|---|---|
| iRacing setup `.sto` | User saves setup in iRacing | `setups_archive/<car_slug>/<track_slug>/` + `index.json` | car, track, best_lap, session_type, timestamp |
| SimuCube True Drive profile | `.tdp`/`.json` change in `%LOCALAPPDATA%\Granite Devices\…` | `hardware_archive/simucube/` + `index.json` | brand, current car, current track, current best_lap, timestamp |
| SimMagic SimPro profile | Any change in `%LOCALAPPDATA%\SimPro Manager\…` | `hardware_archive/simmagic/` + `index.json` | brand, current car, current track, current best_lap, timestamp |
| Lap data (every lap) | Lap completed in iRacing | In-memory `Session.laps` → POSTed to `chief-final` `/api/sim/sync-session` at session end | car, track, lap_time, fuel_used, max_speed, etc. |
| Coaching events | Triggered by event detector | `STATE.coaching_events` → `/coaching/latest` HTTP endpoint | severity, kind, summary, spoken_text |
| User Chief settings | User changes /dashboard/settings | Supabase `chief_settings` (RLS-scoped) + browser `localStorage` | per-user JSON, all 6 sections |

### iRacing → web app data flow
```
iRacing (memory-mapped file)
   ↓ pyirsdk @ 60Hz
Logger Python process
   ↓ STATE (thread-safe)
   ↓ HTTP server :5188
Browser at chiefracing.com/dashboard/sim-racing/live-status
   ↓ fetch every 250ms
UI renders
```

### Hardware → archive flow
```
SimuCube True Drive saves profile
   ↓ %LOCALAPPDATA%\Granite Devices\Simucube True Drive\profiles\new.tdp
HardwareWatcher (5s poll)
   ↓ on_change(path, "Simucube")
HardwareArchive
   ↓ shutil.copy2 + index.json append + Chief notification
hardware_archive/simucube/20260505_1430__stockcars_cup__charlotte__new.tdp
   ↓ exposed via GET /hardware
Web app can list and recall
```

## Detected hardware brands (auto-discovery)

The watcher checks these paths automatically (no setup needed):

**SimuCube:**
- `%LOCALAPPDATA%\Granite Devices\Simucube True Drive\profiles\`
- `%APPDATA%\Granite Devices\Simucube True Drive\profiles\`
- `%LOCALAPPDATA%\Granite Devices\Simucube 2\profiles\`

**SimMagic:**
- `%LOCALAPPDATA%\SimPro Manager\`
- `%APPDATA%\SimPro Manager\`
- Same for `SimMagic` and `Simagic` variants

**File extensions watched:** `.tdp`, `.profile`, `.json`, `.xml`, `.cfg`, `.ini`, `.smprofile`, `.scprofile`

If your hardware uses a non-standard path, set in `.env`:
```
HARDWARE_WATCH_PATHS=C:\path1;C:\path2
```

## What's deployed vs what needs deploy

| Layer | Status |
|---|---|
| Chief-final web app | ✅ Live at chiefracing.com (last v1 deploy). NEW v3 features deploy with next `vercel --prod` |
| Chief-iracing-logger v3 | 📦 Code ready, needs upload to home PC + restart |
| Supabase migrations | 📦 Need 1-time SQL paste (settings page banner walks through it) |

## Tonight's deploy steps (5 min)

1. **chief-final:** `cd C:\Users\benw\Desktop\chief-final && vercel --prod`
2. **chief-iracing-logger:** Re-upload to Google Drive, pull on home PC, run `pip install -r requirements.txt --upgrade`, double-click `run.bat`

After step 1, the Settings page yellow banner walks through the one-time DB setup. After step 2, the logger starts auto-archiving SimuCube/SimMagic profiles.

## What's NOT in v3 (next-build candidates)

- Push setup_files + hardware_profiles directly to Supabase (currently archived locally only — they sync to dashboard on session end via existing `/api/sim/sync-session`, but new tables for hardware would let friends see your profiles)
- Per-friend tokens (multi-user mode)
- One-click .exe installer (PyInstaller)
- Premium ElevenLabs voice
- Track-corner names instead of "sector 4"
