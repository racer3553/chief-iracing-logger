"""Trace analyzer -- captures throttle/brake/steering/speed traces per lap into
100 bins indexed by lap distance (each bin = 1% of the lap). Compares each lap
to the personal best. Identifies WHERE and WHY the driver lost time.

This is the intelligence behind:
  - "you released brake 0.3s too early at sector 4"
  - "you got back to throttle a quarter second late at apex"
  - "you used 12 degrees more steering than the reference lap"
"""
import logging
from typing import Dict, Any, List, Optional, Tuple

log = logging.getLogger("chief.trace_analyzer")

NUM_BINS = 100  # 100 samples per channel per lap


class LapTrace:
    """Per-lap trace data (~100 samples per channel)."""
    __slots__ = ("lap_number", "lap_time", "is_pb",
                 "throttle", "brake", "steering", "speed", "gear")

    def __init__(self, lap_number: int, lap_time: float, is_pb: bool = False):
        self.lap_number = lap_number
        self.lap_time = lap_time
        self.is_pb = is_pb
        # Each channel = list of (count, sum) per bin -> avg
        self.throttle: List[float] = [0.0] * NUM_BINS
        self.brake: List[float]    = [0.0] * NUM_BINS
        self.steering: List[float] = [0.0] * NUM_BINS
        self.speed: List[float]    = [0.0] * NUM_BINS
        self.gear: List[int]       = [0]   * NUM_BINS

    def to_dict(self) -> Dict[str, Any]:
        return {
            "lap_number": self.lap_number,
            "lap_time": self.lap_time,
            "is_pb": self.is_pb,
            "throttle": self.throttle,
            "brake": self.brake,
            "steering": self.steering,
            "speed": self.speed,
            "gear": self.gear,
        }


class TraceAnalyzer:
    def __init__(self):
        self._building: Optional[LapTrace] = None
        self._building_counts: List[int] = [0] * NUM_BINS
        self._building_sums: Dict[str, List[float]] = {}
        self._current_lap = -1

        # Completed laps
        self.laps: List[LapTrace] = []
        self.best_trace: Optional[LapTrace] = None

    def update(self, snap: Dict[str, Any]):
        if snap is None or snap.get("on_pit_road"):
            return
        lap = snap.get("lap", 0) or 0
        if lap != self._current_lap:
            # New lap: finalize old, start new
            self._finalize_current(snap)
            self._current_lap = lap
            self._building = LapTrace(lap_number=lap, lap_time=0.0)
            self._building_counts = [0] * NUM_BINS
            self._building_sums = {k: [0.0] * NUM_BINS for k in
                                   ("throttle", "brake", "steering", "speed", "gear")}

        if self._building is None:
            return
        pct = max(0.0, min(0.999, snap.get("lap_dist_pct", 0.0) or 0.0))
        idx = int(pct * NUM_BINS)
        self._building_counts[idx] += 1
        self._building_sums["throttle"][idx] += snap.get("throttle", 0.0) or 0.0
        self._building_sums["brake"][idx]    += snap.get("brake", 0.0) or 0.0
        self._building_sums["steering"][idx] += snap.get("steering", 0.0) or 0.0
        self._building_sums["speed"][idx]    += snap.get("speed_ms", 0.0) or 0.0
        self._building_sums["gear"][idx]     += snap.get("gear", 0) or 0

    def _finalize_current(self, latest_snap: Optional[Dict[str, Any]]):
        if self._building is None:
            return
        lap_time = (latest_snap or {}).get("lap_last_time", 0.0) or 0.0
        if lap_time <= 0 or self._current_lap <= 0:
            self._building = None
            return

        # Compute averages per bin
        for ch in ("throttle", "brake", "steering", "speed"):
            arr = getattr(self._building, ch)
            sums = self._building_sums[ch]
            counts = self._building_counts
            for i in range(NUM_BINS):
                arr[i] = (sums[i] / counts[i]) if counts[i] > 0 else (arr[i - 1] if i > 0 else 0.0)
        # Gear is int; pick max-occurrence-ish (avg rounded)
        gsums = self._building_sums["gear"]
        for i in range(NUM_BINS):
            self._building.gear[i] = int(round(gsums[i] / self._building_counts[i])) if self._building_counts[i] > 0 else (self._building.gear[i - 1] if i > 0 else 0)

        self._building.lap_time = lap_time
        # Mark as PB?
        if self.best_trace is None or lap_time < self.best_trace.lap_time:
            self._building.is_pb = True
            if self.best_trace is not None:
                self.best_trace.is_pb = False
            self.best_trace = self._building
            log.info(f"New PB trace: lap {self._current_lap} = {lap_time:.3f}s")
        else:
            self._building.is_pb = False
        self.laps.append(self._building)
        # Cap memory at last 50 laps
        if len(self.laps) > 50:
            self.laps = self.laps[-50:]
        self._building = None

    # ── Comparison helpers ────────────────────────────────────
    def compare_to_best(self, lap_idx: int = -1) -> Optional[Dict[str, Any]]:
        """Compare a lap (default: last) to the personal best. Returns analysis dict."""
        if not self.laps or self.best_trace is None or self.best_trace is self.laps[lap_idx]:
            return None
        target = self.laps[lap_idx]
        ref = self.best_trace
        delta_lap = target.lap_time - ref.lap_time

        # Per-bin differences
        thr_diff = [target.throttle[i] - ref.throttle[i] for i in range(NUM_BINS)]
        brk_diff = [target.brake[i]    - ref.brake[i]    for i in range(NUM_BINS)]
        str_diff = [target.steering[i] - ref.steering[i] for i in range(NUM_BINS)]
        spd_diff = [target.speed[i]    - ref.speed[i]    for i in range(NUM_BINS)]

        # Find biggest speed loss bin (where they're SLOWEST relative to PB)
        worst_pct, worst_loss = 0, 0.0
        for i in range(NUM_BINS):
            if spd_diff[i] < worst_loss:
                worst_loss = spd_diff[i]; worst_pct = i

        # Identify cause at worst bin
        cause = self._diagnose_at_bin(worst_pct, target, ref)

        # Sector-level loss buckets (10 buckets of 10 bins each)
        sector_loss = []
        for s in range(10):
            lo, hi = s * 10, (s + 1) * 10
            avg_spd_diff = sum(spd_diff[lo:hi]) / 10.0
            sector_loss.append(round(avg_spd_diff, 2))

        return {
            "lap_number": target.lap_number,
            "lap_time": target.lap_time,
            "best_lap_time": ref.lap_time,
            "delta": round(delta_lap, 3),
            "worst_pct": worst_pct,
            "worst_speed_loss_ms": round(worst_loss, 2),
            "worst_sector": worst_pct // 10,
            "worst_cause": cause,
            "sector_speed_diff": sector_loss,
            "input_diff_summary": self._summarize_inputs(thr_diff, brk_diff, str_diff),
        }

    def _diagnose_at_bin(self, pct: int, target: LapTrace, ref: LapTrace) -> str:
        """Categorize what went wrong at the slowest point."""
        thr_d = target.throttle[pct] - ref.throttle[pct]
        brk_d = target.brake[pct]    - ref.brake[pct]
        str_d = target.steering[pct] - ref.steering[pct]

        if brk_d > 0.10:        return "brake_too_hard"
        if brk_d < -0.10:       return "brake_too_light"
        if thr_d > 0.10:        return "early_throttle"   # more throttle here = bad if losing speed
        if thr_d < -0.10:       return "late_throttle"
        if abs(str_d) > 0.20:   return "over_steering"
        return "carrying_speed_short"

    def _summarize_inputs(self, thr_diff, brk_diff, str_diff) -> str:
        # Aggregate signed deltas into a short readable string
        thr_avg = sum(thr_diff) / len(thr_diff) * 100  # percent
        brk_avg = sum(brk_diff) / len(brk_diff) * 100
        str_avg_deg = (sum(abs(s) for s in str_diff) / len(str_diff)) * (180.0 / 3.14159)
        bits = []
        if abs(thr_avg) > 2: bits.append(f"throttle {thr_avg:+.0f}%")
        if abs(brk_avg) > 2: bits.append(f"brake {brk_avg:+.0f}%")
        if str_avg_deg > 3:  bits.append(f"steering {str_avg_deg:.0f}° more swing")
        return ", ".join(bits) if bits else "inputs near reference"

    def snapshot(self) -> Dict[str, Any]:
        last = self.compare_to_best(-1) if len(self.laps) > 1 else None
        return {
            "total_laps": len(self.laps),
            "best_lap_time": self.best_trace.lap_time if self.best_trace else 0.0,
            "best_lap_number": self.best_trace.lap_number if self.best_trace else 0,
            "last_lap_analysis": last,
            "last_5_lap_times": [l.lap_time for l in self.laps[-5:]],
        }
