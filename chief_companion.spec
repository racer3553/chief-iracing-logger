# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller spec for the single-binary ChiefCompanion.exe.

Build with:
    build_companion.bat
"""
from PyInstaller.utils.hooks import collect_submodules, collect_data_files

block_cipher = None

hidden = []
hidden += collect_submodules("pyirsdk")
hidden += collect_submodules("pyttsx3")
hidden += collect_submodules("pynput")
hidden += collect_submodules("sounddevice")
hidden += collect_submodules("faster_whisper")
hidden += collect_submodules("pystray")
hidden += collect_submodules("PIL")
hidden += [
    "win32com",
    "win32com.client",
    "pythoncom",
    "comtypes",
    "comtypes.client",
    "pywin32",
]

datas = []
datas += collect_data_files("faster_whisper")

a = Analysis(
    ["chief_companion.py"],
    pathex=["."],
    binaries=[],
    datas=datas,
    hiddenimports=hidden,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name="ChiefCompanion",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,             # no terminal window — runs silent in tray
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=None,                 # add icon.ico later for taskbar branding
)
