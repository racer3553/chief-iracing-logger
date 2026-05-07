"""First-run setup wizard.

Shows a tiny Tk dialog asking for the install token, validates with chiefracing.com,
saves to %APPDATA%\\Chief\\config.json, enables Windows autostart, then exits.

User flow:
    1. Sign up at chiefracing.com
    2. Visit /dashboard/download — copy the install token
    3. Double-click ChiefCompanion.exe
    4. Paste token, click Activate
    5. Done forever — every Windows boot from then on launches Chief silently in tray
"""
import sys
import tkinter as tk
from tkinter import messagebox, ttk
from typing import Optional

import requests

from companion import token_store, autostart

SERVER_URL = "https://chiefracing.com"


def validate_token(token: str) -> Optional[dict]:
    """POST token to /api/companion/register; return user dict on success."""
    try:
        r = requests.post(
            f"{SERVER_URL}/api/companion/register",
            json={"token": token.strip()},
            timeout=10,
        )
        if r.status_code == 200:
            data = r.json()
            if data.get("ok"):
                return data.get("user") or {}
        return None
    except requests.RequestException:
        return None


def run() -> bool:
    """Returns True if setup was completed successfully."""
    root = tk.Tk()
    root.title("Chief Companion — First-Run Setup")
    root.geometry("520x340")
    root.configure(bg="#0c0d12")
    try:
        root.iconbitmap(default="")
    except Exception:
        pass

    style = ttk.Style()
    try:
        style.theme_use("clam")
    except tk.TclError:
        pass

    result = {"ok": False}

    header = tk.Label(
        root,
        text="Chief Companion",
        font=("Segoe UI", 22, "bold"),
        fg="#a3ff00",
        bg="#0c0d12",
    )
    header.pack(pady=(28, 4))

    sub = tk.Label(
        root,
        text="Paste your install token to activate AI race coaching.",
        font=("Segoe UI", 10),
        fg="#8892a4",
        bg="#0c0d12",
    )
    sub.pack(pady=(0, 18))

    hint = tk.Label(
        root,
        text="Find your token at chiefracing.com/dashboard/download",
        font=("Segoe UI", 9),
        fg="#22d3ee",
        bg="#0c0d12",
    )
    hint.pack()

    entry_var = tk.StringVar()
    entry = tk.Entry(
        root,
        textvariable=entry_var,
        font=("Consolas", 12),
        bg="#1c1c28",
        fg="#e8ecf4",
        insertbackground="#e8ecf4",
        relief="flat",
        justify="center",
        width=30,
    )
    entry.pack(pady=14, ipady=8)
    entry.focus_set()

    status = tk.Label(
        root, text="", font=("Segoe UI", 9), fg="#8892a4", bg="#0c0d12"
    )
    status.pack()

    def attempt():
        tok = entry_var.get().strip()
        if not tok or len(tok) < 8:
            status.config(text="Token looks too short.", fg="#ff6b6b")
            return
        status.config(text="Activating…", fg="#22d3ee")
        root.update_idletasks()
        user = validate_token(tok)
        if user is None:
            status.config(text="Invalid or expired token.", fg="#ff6b6b")
            return
        token_store.set_token(tok)
        token_store.set_user(user)
        autostart.enable()
        result["ok"] = True
        messagebox.showinfo(
            "Chief Companion",
            "Activated. Chief will now run quietly in the system tray\n"
            "(green flag icon, bottom-right). It launches automatically with Windows.\n\n"
            "Open chiefracing.com → Sim Racing → Live Status to see live data when you race.",
        )
        root.destroy()

    btn = tk.Button(
        root,
        text="Activate",
        command=attempt,
        font=("Segoe UI", 11, "bold"),
        bg="#a3ff00",
        fg="#0c0d12",
        activebackground="#7fcc00",
        activeforeground="#0c0d12",
        relief="flat",
        padx=24,
        pady=8,
        cursor="hand2",
    )
    btn.pack(pady=18)

    foot = tk.Label(
        root,
        text="No token? Sign up free at chiefracing.com",
        font=("Segoe UI", 9),
        fg="#8892a4",
        bg="#0c0d12",
    )
    foot.pack(pady=(8, 0))

    root.bind("<Return>", lambda _e: attempt())
    root.mainloop()
    return result["ok"]


if __name__ == "__main__":
    sys.exit(0 if run() else 1)
