@echo off
REM ============================================================
REM  Chief Companion — one-click .exe builder
REM  Run this on your HOME PC (where Python + iRacing are).
REM  Output: dist\ChiefCompanion.exe  (single signed-able binary)
REM ============================================================

setlocal
cd /d "%~dp0"

if not exist .venv (
    echo [build] creating venv...
    py -3 -m venv .venv
)
call .venv\Scripts\activate.bat

echo [build] upgrading pip...
python -m pip install --upgrade pip >nul

echo [build] installing requirements...
pip install -r requirements.txt
pip install pyinstaller pystray pillow pywin32

echo [build] cleaning prior output...
if exist build rmdir /s /q build
if exist dist rmdir /s /q dist

echo [build] running PyInstaller...
pyinstaller --clean chief_companion.spec
if errorlevel 1 (
    echo.
    echo [build] FAILED — see output above.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  Build complete:  dist\ChiefCompanion.exe
echo  Test it:  dist\ChiefCompanion.exe
echo  Distribute it:  upload dist\ChiefCompanion.exe to chiefracing.com
echo ============================================================
echo.
pause
