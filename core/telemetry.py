"""iRacing telemetry reader — wraps pyirsdk into a clean snapshot dict."""
import time
import logging
from typing import Optional, Dict, Any

try:
    import irsdk
except ImportError:
    irsdk = None

log = logging.getLogger("chief.telemetry")


class Telemetry:
    """Polls iRacing SDK at TELEMETRY_HZ. Returns None if iRacing not running."""

    def __init__(self):
        if irsdk is None:
            raise RuntimeError("pyirsdk not installed. Run: pip install -r requirements.txt")
        self.ir = irsdk.IRSDK()
        self._connected = False
        self._last_session_info_update = -1

    def connect(self) -> bool:
        """Try to connect. Returns True if iRacing is alive."""
        if self.ir.is_initialized and self.ir.is_connected:
            return True
        ok = self.ir.startup()
        self._connected = bool(ok and self.ir.is_connected)
        if self._connected:
            log.info("Connected to iRacing.")
        return self._connected

    def shutdown(self):
        try:
            self.ir.shutdown()
        except Exception:
            pass
        self._connected = False

    def snapshot(self) -> Optional[Dict[str, Any]]:
        """One frame of telemetry. None if not connected or iRacing closed."""
        if not self.ir.is_connected:
            self._connected = False
            return None

        # Refresh session string occasionally (it's expensive)
        update = self.ir["SessionInfoUpdate"] or 0
        session_info_changed = update != self._last_session_info_update
        if session_info_changed:
            self._last_session_info_update = update

        try:
            snap = {
                "ts": time.time(),
                # Lap / position
                "lap": self.ir["Lap"] or 0,
                "lap_completed": self.ir["LapCompleted"] or 0,
                "lap_dist_pct": self.ir["LapDistPct"] or 0.0,
                "lap_current_time": self.ir["LapCurrentLapTime"] or 0.0,
                "lap_last_time": self.ir["LapLastLapTime"] or 0.0,
                "lap_best_time": self.ir["LapBestLapTime"] or 0.0,
                "lap_delta_to_best": self.ir["LapDeltaToBestLap"] or 0.0,
                "lap_delta_to_optimal": self.ir["LapDeltaToOptimalLap"] or 0.0,
                # Driver inputs
                "throttle": self.ir["Throttle"] or 0.0,
                "brake": self.ir["Brake"] or 0.0,
                "clutch": self.ir["Clutch"] or 0.0,
                "steering": self.ir["SteeringWheelAngle"] or 0.0,
                "gear": self.ir["Gear"] or 0,
                "rpm": self.ir["RPM"] or 0.0,
                "speed_ms": self.ir["Speed"] or 0.0,  # m/s
                # Forces
                "lat_g": self.ir["LatAccel"] or 0.0,
                "long_g": self.ir["LongAccel"] or 0.0,
                "vert_g": self.ir["VertAccel"] or 0.0,
                "yaw_rate": self.ir["YawRate"] or 0.0,
                # Track / state
                "on_pit_road": bool(self.ir["OnPitRoad"]),
                "track_surface": self.ir["PlayerTrackSurface"] or 0,  # 0=off, 1=in pit, 2=apron, 3=on track
                "is_on_track": bool(self.ir["IsOnTrack"]),
                "session_state": self.ir["SessionState"] or 0,
                "session_flags": self.ir["SessionFlags"] or 0,
                "session_time": self.ir["SessionTime"] or 0.0,
                "session_time_remain": self.ir["SessionTimeRemain"] or 0.0,
                # Fuel
                "fuel_pct": self.ir["FuelLevelPct"] or 0.0,
                "fuel_level": self.ir["FuelLevel"] or 0.0,
                "fuel_use_per_hour": self.ir["FuelUsePerHour"] or 0.0,
                # Tires (RF/LF/RR/LR temps middle)
                "tire_lf_temp": self.ir["LFtempCM"] or 0.0,
                "tire_rf_temp": self.ir["RFtempCM"] or 0.0,
                "tire_lr_temp": self.ir["LRtempCM"] or 0.0,
                "tire_rr_temp": self.ir["RRtempCM"] or 0.0,
                # Incidents
                "incidents": self.ir["PlayerCarMyIncidentCount"] or 0,
                "team_incidents": self.ir["PlayerCarTeamIncidentCount"] or 0,
                # Position
                "position": self.ir["PlayerCarPosition"] or 0,
                "class_position": self.ir["PlayerCarClassPosition"] or 0,
                # Conditions
                "track_temp": self.ir["TrackTempCrew"] or 0.0,
                "air_temp": self.ir["AirTemp"] or 0.0,
            }
            return snap
        except Exception as e:
            log.warning(f"Telemetry read failed: {e}")
            return None

    def session_info(self) -> Dict[str, Any]:
        """Read the (slower-changing) session metadata. Best-effort."""
        info = {}
        try:
            wi = self.ir["WeekendInfo"] or {}
            di = self.ir["DriverInfo"] or {}
            drivers = di.get("Drivers", []) if isinstance(di, dict) else []
            my_idx = di.get("DriverCarIdx", 0) if isinstance(di, dict) else 0
            me = drivers[my_idx] if drivers and my_idx < len(drivers) else {}

            info = {
                "track_name": wi.get("TrackDisplayName", ""),
                "track_config": wi.get("TrackConfigName", ""),
                "track_id": wi.get("TrackID", 0),
                "session_id": wi.get("SessionID", 0),
                "sub_session_id": wi.get("SubSessionID", 0),
                "car_name": me.get("CarScreenName", ""),
                "car_path": me.get("CarPath", ""),
                "driver_name": me.get("UserName", ""),
                "driver_id": me.get("UserID", 0),
                "skies": wi.get("WeekendOptions", {}).get("Skies", "") if isinstance(wi.get("WeekendOptions"), dict) else "",
            }
        except Exception as e:
            log.debug(f"session_info read partial: {e}")
        return info
