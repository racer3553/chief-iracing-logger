@echo off
REM Pre-flight test — fires fake events to verify voice + Claude work BEFORE iRacing
cd /d "%~dp0"
if not exist "venv\Scripts\python.exe" (
    python -m venv venv
    call venv\Scripts\activate.bat
    pip install --upgrade pip
    pip install -r requirements.txt
) else (
    call venv\Scripts\activate.bat
)
python chief.py --simulate
pause
