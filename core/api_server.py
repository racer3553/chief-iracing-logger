"""Local HTTP server for the Chief web app to poll.

Endpoints:
    GET  /                       — friendly HTML status page
    GET  /health                 — overall status
    GET  /diagnostics            — full diagnostics snapshot (iRacing/cloud/mic/audio/sub)
    GET  /telemetry/latest       — latest snapshot
    GET  /coaching/latest        — recent coaching events + voice state
    GET  /sectors                — current sector tracker snapshot
    GET  /setups                 — full setup library
    GET  /setups/recommend       — setups for current car/track
    POST /setups/{id}/restore    — copy archived setup back into iRacing
    GET  /hardware               — full hardware profile library (SimuCube + SimMagic)
    GET  /hardware/dirs          — detected hardware-config dirs
    POST /test/coaching-event    — fire a test coaching call
    POST /test/ptt-question      — simulate PTT question (text input)
"""
import json
import logging
import threading
import urllib.parse
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from typing import Optional, Callable

from core.state import STATE

log = logging.getLogger("chief.api")


class _Handler(BaseHTTPRequestHandler):
    test_event_handler: Optional[Callable] = None
    ptt_question_handler: Optional[Callable] = None
    setup_archive = None
    iracing_setups_root: Optional[str] = None
    sector_tracker = None
    hardware_archive = None
    hardware_dirs = None  # list of {brand, path}

    def log_message(self, fmt, *args):
        pass

    def _send(self, code: int, body: dict):
        payload = json.dumps(body, default=str).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def _send_html(self, code: int, html: str):
        body = html.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self._send(204, {})

    def do_GET(self):
        try:
            path = self.path.split("?")[0]
            if path in ("/", "/index.html"):
                return self._send_html(200, _STATUS_HTML)
            if path == "/health":
                return self._send(200, STATE.health_snapshot())
            if path == "/diagnostics":
                try:
                    from core import health as _h
                    return self._send(200, _h.collect(STATE))
                except Exception as e:
                    log.exception("diagnostics error")
                    return self._send(500, {"error": str(e)})
            if path == "/telemetry/latest":
                return self._send(200, STATE.telemetry_snapshot())
            if path == "/coaching/latest":
                return self._send(200, STATE.coaching_snapshot(limit=15))
            if path == "/sectors":
                if _Handler.sector_tracker is None:
                    return self._send(503, {"error": "sector_tracker_not_ready"})
                return self._send(200, _Handler.sector_tracker.snapshot())
            if path == "/setups":
                if _Handler.setup_archive is None:
                    return self._send(503, {"error": "setup_archive_not_ready"})
                return self._send(200, {
                    "count": len(_Handler.setup_archive._index),
                    "setups": _Handler.setup_archive.list_all(),
                })
            if path == "/setups/recommend":
                if _Handler.setup_archive is None:
                    return self._send(503, {"error": "setup_archive_not_ready"})
                info = STATE.session_info or {}
                rows = _Handler.setup_archive.for_car_track(
                    car_path=info.get("car_path", ""),
                    track_name=info.get("track_name", ""),
                )
                return self._send(200, {
                    "car_path": info.get("car_path", ""),
                    "car_name": info.get("car_name", ""),
                    "track_name": info.get("track_name", ""),
                    "count": len(rows),
                    "setups": rows,
                })
            if path == "/hardware":
                if _Handler.hardware_archive is None:
                    return self._send(503, {"error": "hardware_archive_not_ready"})
                return self._send(200, {
                    "count": len(_Handler.hardware_archive._index),
                    "profiles": _Handler.hardware_archive.list_all(),
                })
            if path == "/hardware/dirs":
                return self._send(200, {
                    "dirs": _Handler.hardware_dirs or [],
                })
            return self._send(404, {"error": "not_found", "path": path})
        except Exception as e:
            log.exception("GET error")
            return self._send(500, {"error": str(e)})

    def do_POST(self):
        try:
            length = int(self.headers.get("Content-Length", "0") or 0)
            raw = self.rfile.read(length) if length else b""
            body = {}
            if raw:
                try:
                    body = json.loads(raw.decode("utf-8") or "{}")
                except json.JSONDecodeError:
                    return self._send(400, {"error": "invalid_json"})

            path = self.path.split("?")[0]

            if path == "/test/coaching-event":
                if not _Handler.test_event_handler:
                    return self._send(503, {"error": "test_handler_not_ready"})
                phrase = body.get("phrase") or "Chief live coach test. Voice and coaching system active."
                use_claude = bool(body.get("use_claude", False))
                result = _Handler.test_event_handler(phrase=phrase, use_claude=use_claude)
                return self._send(200, {"ok": True, **result})

            if path == "/test/ptt-question":
                if not _Handler.ptt_question_handler:
                    return self._send(503, {"error": "ptt_handler_not_ready"})
                question = body.get("question") or "How am I doing?"
                result = _Handler.ptt_question_handler(question)
                return self._send(200, {"ok": True, "question": question, "reply": result})

            if path.startswith("/setups/") and path.endswith("/restore"):
                if _Handler.setup_archive is None or not _Handler.iracing_setups_root:
                    return self._send(503, {"error": "setup_archive_not_ready"})
                setup_id = urllib.parse.unquote(path[len("/setups/"):-len("/restore")])
                target = _Handler.setup_archive.restore_to_iracing(setup_id, _Handler.iracing_setups_root)
                if not target:
                    return self._send(404, {"error": "setup_not_found_or_restore_failed"})
                return self._send(200, {"ok": True, "restored_to": target})

            return self._send(404, {"error": "not_found", "path": path})
        except Exception as e:
            log.exception("POST error")
            return self._send(500, {"error": str(e)})


_STATUS_HTML = """<!doctype html>
<html><head><meta charset="utf-8"><title>Chief Logger</title>
<style>
body{font-family:-apple-system,system-ui,Segoe UI,Roboto,sans-serif;background:#0c0d12;color:#e8ecf4;margin:0;padding:32px;}
h1{color:#a3ff00;margin:0 0 8px 0;}p{color:#8892a4;}
.card{background:#1c1c28;border:1px solid rgba(255,255,255,.08);border-radius:12px;padding:14px;margin-top:10px;}
code{background:rgba(255,255,255,.05);padding:2px 6px;border-radius:4px;color:#22d3ee;}
.dot{display:inline-block;width:10px;height:10px;border-radius:50%;background:#a3ff00;margin-right:8px;animation:p 2s infinite;}
@keyframes p{50%{opacity:.3}}
</style></head><body>
<h1><span class="dot"></span>Chief Logger Online</h1>
<p>Local HTTP server endpoints:</p>
<div class="card"><strong>GET</strong> <code>/health</code></div>
<div class="card"><strong>GET</strong> <code>/telemetry/latest</code></div>
<div class="card"><strong>GET</strong> <code>/coaching/latest</code></div>
<div class="card"><strong>GET</strong> <code>/sectors</code></div>
<div class="card"><strong>GET</strong> <code>/setups</code> · <code>/setups/recommend</code></div>
<div class="card"><strong>POST</strong> <code>/setups/{id}/restore</code></div>
<div class="card"><strong>GET</strong> <code>/hardware</code> · <code>/hardware/dirs</code></div>
<div class="card"><strong>POST</strong> <code>/test/coaching-event</code> · <code>/test/ptt-question</code></div>
</body></html>"""


class ApiServer:
    def __init__(self, host="127.0.0.1", port=5188,
                 test_event_handler=None, ptt_question_handler=None,
                 setup_archive=None, iracing_setups_root=None,
                 sector_tracker=None,
                 hardware_archive=None, hardware_dirs=None):
        self.host = host; self.port = port
        _Handler.test_event_handler = test_event_handler
        _Handler.ptt_question_handler = ptt_question_handler
        _Handler.setup_archive = setup_archive
        _Handler.iracing_setups_root = iracing_setups_root
        _Handler.sector_tracker = sector_tracker
        _Handler.hardware_archive = hardware_archive
        _Handler.hardware_dirs = hardware_dirs
        self._server: Optional[ThreadingHTTPServer] = None
        self._thread: Optional[threading.Thread] = None

    def start(self):
        self._server = ThreadingHTTPServer((self.host, self.port), _Handler)
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True, name="ChiefAPIServer")
        self._thread.start()
        log.info(f"HTTP server listening on http://{self.host}:{self.port}")

    def stop(self):
        if self._server:
            try:
                self._server.shutdown()
                self._server.server_close()
                log.info("HTTP server stopped.")
            except Exception:
                pass
