"""Sector tracker — splits each lap into N micro-sectors via LapDistPct.

Tracks best time per sector across the session. Computes live delta vs best.
Drives the smart "you're losing X tenths in sector Y" coaching.
"""
import time
import logging
from collections import defaultdict
from typing import Dict, List, Optional, Tuple

log = logging.getLogger("chief.sectors")


class SectorTracker:
    def __init__(self, num_sectors: int = 10):
        self.num_sectors = num_sectors

        # Best time per sector index (0..N-1) ever seen
        self._best_sector_times: Dict[int, float] = {}

        # Per-lap accumulator: sector_idx -> elapsed time entered + cumulative
        self._lap_sector_times: Dict[int, float] = {}
        self._current_sector: Optional[int] = None
        self._sector_enter_session_time: float = 0.0
        self._current_lap: int = -1

        # Last completed sector delta (for fast events)
        self._last_completed_sector: Optional[int] = None
        self._last_sector_delta: float = 0.0  # +ve = slower than best
        self._last_sector_time: float = 0.0

        # Live (in-progress) sector delta
        self._live_delta: float = 0.0

    def _idx(self, lap_dist_pct: float) -> int:
        if lap_dist_pct < 0:
            lap_dist_pct = 0.0
        if lap_dist_pct >= 1.0:
            lap_dist_pct = 0.999
        return int(lap_dist_pct * self.num_sectors)

    def update(self, snap: dict) -> Optional[Tuple[int, float, float]]:
        """Process a telemetry tick.

        Returns (completed_sector_idx, sector_time, delta_to_best) when a sector
        boundary is crossed. Otherwise None.
        """
        if snap is None or snap.get("on_pit_road") or not snap.get("is_on_track"):
            return None

        lap = snap.get("lap", 0) or 0
        pct = snap.get("lap_dist_pct", 0.0) or 0.0
        sess_t = snap.get("session_time", 0.0) or 0.0

        # New lap detection — reset accumulator
        if lap != self._current_lap:
            self._current_lap = lap
            self._lap_sector_times = {}
            self._current_sector = None

        idx = self._idx(pct)
        completed_event = None

        if self._current_sector is None:
            # First sample of this lap
            self._current_sector = idx
            self._sector_enter_session_time = sess_t
        elif idx != self._current_sector:
            # Crossed into a new sector — finalize the one we just left
            sector_time = sess_t - self._sector_enter_session_time
            if sector_time > 0.05:  # ignore obvious glitches
                left_idx = self._current_sector
                self._lap_sector_times[left_idx] = sector_time
                best = self._best_sector_times.get(left_idx, 0.0)
                if best == 0 or sector_time < best:
                    self._best_sector_times[left_idx] = sector_time
                    delta = 0.0
                else:
                    delta = sector_time - best

                self._last_completed_sector = left_idx
                self._last_sector_time = sector_time
                self._last_sector_delta = delta
                completed_event = (left_idx, sector_time, delta)

            self._current_sector = idx
            self._sector_enter_session_time = sess_t

        # Live delta: time-so-far in current sector vs best
        if self._current_sector is not None:
            elapsed = sess_t - self._sector_enter_session_time
            best = self._best_sector_times.get(self._current_sector, 0.0)
            self._live_delta = elapsed - best if best > 0 else 0.0

        return completed_event

    def reset(self):
        self._best_sector_times = {}
        self._lap_sector_times = {}
        self._current_sector = None
        self._current_lap = -1
        self._live_delta = 0.0

    def best_sector_times(self) -> Dict[int, float]:
        return dict(self._best_sector_times)

    def theoretical_best_lap(self) -> float:
        """Sum of all best sector times (the 'optimal' lap if all sectors stitched)."""
        if len(self._best_sector_times) < self.num_sectors:
            return 0.0
        return sum(self._best_sector_times.values())

    def biggest_loss_sector(self, current_sectors: Dict[int, float]) -> Optional[Tuple[int, float]]:
        """Return (sector_idx, delta) for biggest loss vs best, given a complete lap."""
        worst_idx, worst_delta = None, 0.0
        for idx, t in current_sectors.items():
            best = self._best_sector_times.get(idx, t)
            d = t - best
            if d > worst_delta:
                worst_delta, worst_idx = d, idx
        return (worst_idx, worst_delta) if worst_idx is not None else None

    def snapshot(self) -> dict:
        return {
            "num_sectors": self.num_sectors,
            "current_sector": self._current_sector,
            "current_lap": self._current_lap,
            "best_sector_times": self._best_sector_times,
            "lap_sector_times_so_far": self._lap_sector_times,
            "last_completed_sector": self._last_completed_sector,
            "last_sector_time": self._last_sector_time,
            "last_sector_delta": self._last_sector_delta,
            "live_delta": self._live_delta,
            "theoretical_best": self.theoretical_best_lap(),
        }
