"""Hardware archive — local library of every SimuCube/SimMagic profile Chief has seen.

Folder layout under chief-iracing-logger/hardware_archive/:
    <brand>/
        <YYYYMMDD_HHMM>__<car_slug>__<track_slug>__<original_filename>
        ...
Plus an index JSON for fast lookup.
"""
import json
import logging
import os
import re
import shutil
import time
from datetime import datetime
from typing import Optional, Dict, Any, List

log = logging.getLogger("chief.hardware_archive")


def _slug(s: str) -> str:
    if not s: return "unknown"
    s = re.sub(r"[^a-zA-Z0-9._-]+", "_", s).strip("_")
    return s.lower() or "unknown"


class HardwareArchive:
    def __init__(self, root_dir: str):
        self.root = root_dir
        os.makedirs(self.root, exist_ok=True)
        self.index_path = os.path.join(self.root, "index.json")
        self._index: List[Dict[str, Any]] = self._load()

    def _load(self):
        if not os.path.exists(self.index_path): return []
        try:
            with open(self.index_path, "r", encoding="utf-8") as f:
                return json.load(f) or []
        except Exception as e:
            log.warning(f"Index load failed: {e}")
            return []

    def _save(self):
        try:
            tmp = self.index_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(self._index, f, indent=2)
            os.replace(tmp, self.index_path)
        except Exception as e:
            log.warning(f"Index save failed: {e}")

    def already_has(self, source_path: str, mtime: float) -> bool:
        for row in self._index:
            if row.get("source_path") == source_path and abs(row.get("source_mtime", 0) - mtime) < 1.0:
                return True
        return False

    def archive(self, source_path: str, brand: str,
                car_name: str = "", car_path: str = "", track_name: str = "",
                best_lap: float = 0.0, notes: str = "") -> Optional[Dict[str, Any]]:
        if not os.path.exists(source_path):
            return None

        brand_slug = _slug(brand or "Other")
        car_slug = _slug(car_path or car_name or "")
        track_slug = _slug(track_name or "")
        dest_dir = os.path.join(self.root, brand_slug)
        os.makedirs(dest_dir, exist_ok=True)

        ts = datetime.now().strftime("%Y%m%d_%H%M")
        orig = os.path.basename(source_path)
        ctx_part = f"{car_slug}__{track_slug}" if (car_slug != "unknown" or track_slug != "unknown") else "no_session"
        dest_name = f"{ts}__{ctx_part}__{orig}"
        dest = os.path.join(dest_dir, dest_name)

        try:
            shutil.copy2(source_path, dest)
        except Exception as e:
            log.error(f"Hardware archive copy failed: {e}")
            return None

        try:
            mtime = os.path.getmtime(source_path)
            size = os.path.getsize(source_path)
        except Exception:
            mtime = time.time(); size = 0

        # Try to read text contents (JSON/XML/INI are usually text)
        preview = ""
        try:
            with open(source_path, "rb") as f:
                raw = f.read(2000)
            try: preview = raw.decode("utf-8", errors="ignore")[:500]
            except Exception: preview = ""
        except Exception: preview = ""

        row = {
            "id": f"{brand_slug}/{dest_name}",
            "filename": dest_name,
            "original_filename": orig,
            "brand": brand,
            "brand_slug": brand_slug,
            "car_name": car_name,
            "car_path": car_path,
            "car_slug": car_slug,
            "track_name": track_name,
            "track_slug": track_slug,
            "best_lap": best_lap,
            "notes": notes,
            "preview": preview,
            "archived_at": time.time(),
            "size": size,
            "source_path": source_path,
            "source_mtime": mtime,
            "archive_path": dest,
        }
        self._index.append(row)
        self._save()
        log.info(f"HW archived [{brand}] -> {dest_name}")
        return row

    def list_all(self) -> List[Dict[str, Any]]:
        return sorted(self._index, key=lambda r: r.get("archived_at", 0), reverse=True)

    def by_brand(self, brand_slug: str) -> List[Dict[str, Any]]:
        slug = _slug(brand_slug)
        return [r for r in self._index if r.get("brand_slug") == slug]

    def for_car_track(self, car_path: str, track_name: str) -> List[Dict[str, Any]]:
        cs = _slug(car_path); ts = _slug(track_name)
        return [r for r in self._index if r.get("car_slug") == cs and r.get("track_slug") == ts]
