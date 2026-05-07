"""Setup archive -- the local library of every iRacing setup Chief has ever seen.

Folder layout under chief-iracing-logger/setups_archive/:
    <car_path>/
        <track_slug>/
            <YYYYMMDD_HHMM>__<best_lap_or_NA>__<original_filename>.sto
            ...

Plus a JSON index for fast lookup:
    setups_archive/index.json
"""
import json
import logging
import os
import re
import shutil
import time
from typing import Optional, Dict, Any, List
from datetime import datetime

log = logging.getLogger("chief.setup_archive")


def _slug(s: str) -> str:
    if not s:
        return "unknown"
    s = re.sub(r"[^a-zA-Z0-9._-]+", "_", s).strip("_")
    return s.lower() or "unknown"


class SetupArchive:
    def __init__(self, root_dir: str):
        self.root = root_dir
        os.makedirs(self.root, exist_ok=True)
        self.index_path = os.path.join(self.root, "index.json")
        self._index: List[Dict[str, Any]] = self._load_index()

    def _load_index(self) -> List[Dict[str, Any]]:
        if not os.path.exists(self.index_path):
            return []
        try:
            with open(self.index_path, "r", encoding="utf-8") as f:
                return json.load(f) or []
        except Exception as e:
            log.warning(f"Index load failed: {e}")
            return []

    def _save_index(self):
        try:
            tmp = self.index_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(self._index, f, indent=2)
            os.replace(tmp, self.index_path)
        except Exception as e:
            log.warning(f"Index save failed: {e}")

    def already_has(self, source_path: str, mtime: float) -> bool:
        """Avoid duplicate archives if file hasn't changed."""
        for row in self._index:
            if row.get("source_path") == source_path and abs(row.get("source_mtime", 0) - mtime) < 1.0:
                return True
        return False

    def archive(self,
                source_path: str,
                car_name: str,
                car_path: str,
                track_name: str,
                best_lap: Optional[float] = None,
                session_type: Optional[str] = None,
                notes: Optional[str] = None) -> Optional[Dict[str, Any]]:
        """Copy a .sto file into the archive and add an index entry. Returns the row."""
        if not os.path.exists(source_path):
            log.warning(f"Source missing: {source_path}")
            return None

        car_slug = _slug(car_path or car_name or "unknown_car")
        track_slug = _slug(track_name or "unknown_track")
        dest_dir = os.path.join(self.root, car_slug, track_slug)
        os.makedirs(dest_dir, exist_ok=True)

        ts = datetime.now().strftime("%Y%m%d_%H%M")
        lap_part = f"{best_lap:.3f}" if best_lap and best_lap > 0 else "NA"
        orig = os.path.basename(source_path)
        dest_name = f"{ts}__{lap_part}__{orig}"
        dest = os.path.join(dest_dir, dest_name)

        try:
            shutil.copy2(source_path, dest)
        except Exception as e:
            log.error(f"Archive copy failed: {e}")
            return None

        try:
            mtime = os.path.getmtime(source_path)
            size = os.path.getsize(source_path)
        except Exception:
            mtime = time.time()
            size = 0

        row = {
            "id": f"{car_slug}/{track_slug}/{dest_name}",
            "filename": dest_name,
            "original_filename": orig,
            "car_name": car_name or "",
            "car_path": car_path or "",
            "car_slug": car_slug,
            "track_name": track_name or "",
            "track_slug": track_slug,
            "session_type": session_type or "",
            "best_lap": best_lap or 0.0,
            "notes": notes or "",
            "archived_at": time.time(),
            "size": size,
            "source_path": source_path,
            "source_mtime": mtime,
            "archive_path": dest,
        }
        self._index.append(row)
        self._save_index()
        log.info(f"Archived setup -> {dest} (car={car_slug}, track={track_slug}, best_lap={lap_part})")
        return row

    def list_all(self) -> List[Dict[str, Any]]:
        # Return newest first
        return sorted(self._index, key=lambda r: r.get("archived_at", 0), reverse=True)

    def for_car_track(self, car_path: str, track_name: str) -> List[Dict[str, Any]]:
        car_slug = _slug(car_path or "")
        track_slug = _slug(track_name or "")
        rows = [r for r in self._index if r.get("car_slug") == car_slug and r.get("track_slug") == track_slug]
        # Sort by best_lap ascending (best first); 0/NA goes to end
        rows.sort(key=lambda r: (r.get("best_lap") or 9e9, -r.get("archived_at", 0)))
        return rows

    def get(self, setup_id: str) -> Optional[Dict[str, Any]]:
        for r in self._index:
            if r.get("id") == setup_id:
                return r
        return None

    def restore_to_iracing(self, setup_id: str, iracing_setups_root: str) -> Optional[str]:
        """Copy an archived .sto back into the user's iRacing setups folder under the
        same car_path subfolder, so it appears in iRacing's in-game setup picker."""
        row = self.get(setup_id)
        if not row:
            return None
        car_subfolder = row.get("car_path") or row.get("car_slug")
        if not car_subfolder:
            return None
        target_dir = os.path.join(iracing_setups_root, car_subfolder)
        os.makedirs(target_dir, exist_ok=True)
        target = os.path.join(target_dir, row["original_filename"])
        try:
            shutil.copy2(row["archive_path"], target)
            log.info(f"Restored setup -> {target}")
            return target
        except Exception as e:
            log.error(f"Restore failed: {e}")
            return None
