# Chief Companion — Build, Sign, Release

End-to-end recipe for shipping a polished, signed Windows installer that won't
trigger SmartScreen warnings after enough downloads, with auto-update wired
through chiefracing.com.

---

## 1. Build the .exe (PyInstaller)

On the Windows build machine (your home PC), in `chief-iracing-logger/`:

```cmd
build_companion.bat
```

Produces:

```
dist\ChiefCompanion.exe
```

This is the single-binary entry that includes Python, all deps, and the tray.

## 2. Wrap it in a real installer (Inno Setup)

Install Inno Setup from https://jrsoftware.org/isdl.php (free).

Open `installer.iss` in Inno Setup Compiler and click **Build → Compile**.

Produces:

```
dist\ChiefCompanion-Setup.exe
```

This is the installer to ship. It:
- Installs into `Program Files\Chief Racing\Chief Companion`
- Adds Start Menu shortcuts
- Optionally adds the autostart registry entry
- Includes a real Windows Add/Remove Programs entry (uninstaller)
- Cleans `%APPDATA%\Chief` on uninstall

## 3. Code-sign it (eliminates SmartScreen warnings)

You need an EV (Extended Validation) Authenticode certificate from one of:
- DigiCert (~$500/yr)
- Sectigo (~$300/yr)
- SSL.com (~$300/yr)

EV certs eliminate "Microsoft Defender SmartScreen prevented..." warnings
**immediately**. Standard (OV) certs eventually warm up after enough downloads.

### Configure signing in Inno Setup

1. Tools → Configure Sign Tools…
2. Add:
   ```
   Name:    signtool
   Command: "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe" sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 /a $f
   ```
3. Uncomment in `installer.iss`:
   ```ini
   SignTool=signtool
   SignedUninstaller=yes
   ```
4. Recompile — output is now signed end-to-end.

Verify:
```cmd
signtool verify /pa /v dist\ChiefCompanion-Setup.exe
```

## 4. Host the binary

Drop `ChiefCompanion-Setup.exe` somewhere stable. Two options:

**A — Vercel public folder (simplest, free):**
```
chief-final/public/downloads/ChiefCompanion-Setup.exe
```
Then `vercel --prod`. Available at:
```
https://chiefracing.com/downloads/ChiefCompanion-Setup.exe
```

**B — GitHub release + Cloudflare R2 (faster CDN):**
Upload to a public R2 bucket, point a CNAME at it.

Set the env var so the dashboard /download page links to it:
```
NEXT_PUBLIC_COMPANION_DOWNLOAD_URL=https://chiefracing.com/downloads/ChiefCompanion-Setup.exe
```

## 5. Bump the auto-update manifest

Open `chief-final/app/api/companion/version/route.ts` and bump:

```ts
const LATEST = '1.0.1'   // ← match the version you just built
```

Redeploy. Existing installs will see "Check for updates" → land on /download
→ download new installer → run → done.

## 6. Sanity-check the user experience

Run in a clean Windows VM (Windows Sandbox is free in Pro/Enterprise):

1. Visit chiefracing.com → sign in → /dashboard/download
2. Copy install token
3. Click "Download ChiefCompanion-Setup.exe"
4. SmartScreen should NOT appear if signed
5. Run installer → paste token → Activate
6. Confirm:
   - Green flag icon appears in tray
   - Right-click → Status shows iRacing/Cloud/Telemetry
   - chiefracing.com/dashboard/diagnostics shows everything green
7. Launch iRacing → enter test session → Chief speaks within seconds

## Versioning convention

Follow semver:
- `1.0.0` first public release
- `1.0.x` patches (no behavior change)
- `1.x.0` features
- `2.0.0` breaking changes (e.g. token format change)

When `min_supported` in `version/route.ts` exceeds an installed version,
the tray will refuse to run and force the user through `/download`.
