@echo off
REM CHIEF launcher — double-click to start live coaching
cd /d "%~dp0"

REM First-run setup: create venv if missing
if not exist "venv\Scripts\python.exe" (
    echo First run — setting up Python virtual environment...
    python -m venv venv
    if errorlevel 1 (
        echo.
        echo Python not found. Install Python 3.10+ from https://python.org and re-run.
        pause
        exit /b 1
    )
    call venv\Scripts\activate.bat
    pip install --upgrade pip
    pip install -r requirements.txt
) else (
    call venv\Scripts\activate.bat
)

REM Check .env exists
if not exist ".env" (
    echo.
    echo No .env file found. Copy .env.example to .env and paste your ANTHROPIC_API_KEY.
    pause
    exit /b 1
)

python chief.py %*
pause
