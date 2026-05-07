// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Corner Performance Tracker
// Tracks per-corner performance metrics across laps.
// ═══════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

/// <summary>
/// Performance metrics for a single corner on a single lap.
/// </summary>
public class CornerPerformance
{
    public string CornerId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int LapNumber { get; set; }

    // Braking metrics
    public float BrakeStartDistPct { get; set; }    // Where driver actually braked
    public float PeakBrakePressure { get; set; }    // Max brake 0-1
    public float BrakeReleaseRate { get; set; }     // How fast brake was released

    // Speed metrics
    public float EntrySpeed { get; set; }           // Speed at turn-in (mph)
    public float ApexSpeed { get; set; }            // Speed at apex (mph)
    public float MinSpeed { get; set; }             // Minimum speed in corner (mph)
    public float ThrottlePickupDistPct { get; set; } // Where throttle > 0.3
    public float ExitSpeed { get; set; }            // Speed at exit (mph)

    // Gear and control
    public int GearAtEntry { get; set; }
    public int GearAtApex { get; set; }
    public int SteeringCorrectionCount { get; set; } // Rapid steering changes in corner

    // Performance
    public float DeltaGainedLost { get; set; }       // Time gained/lost vs best in this corner zone
    public string MistakeCategory { get; set; } = ""; // none, braked_late, braked_early, over_slowed, snap_oversteer, missed_apex, poor_exit, lockup
}

/// <summary>
/// Tracks per-corner performance across laps. Analyzes driving patterns
/// and detects mistakes in real time.
/// </summary>
public class CornerPerformanceTracker
{
    private readonly ChiefDatabase _db;
    private readonly TrackMapService _mapService;

    // In-memory tracking for current corner being driven
    private class ActiveCornerState
    {
        public string CornerId { get; set; } = "";
        public int LapNumber { get; set; }
        public float BrakeStartDistPct { get; set; } = -1;
        public float PeakBrakePressure { get; set; }
        public float BrakeReleaseRate { get; set; }
        public float EntrySpeed { get; set; } = float.MaxValue;
        public float ApexSpeed { get; set; }
        public float MinSpeed { get; set; } = float.MaxValue;
        public float ThrottlePickupDistPct { get; set; } = -1;
        public float ExitSpeed { get; set; }
        public int GearAtEntry { get; set; }
        public int GearAtApex { get; set; }
        public int SteeringCorrectionCount { get; set; }
        public float LastSteeringInput { get; set; }
        public float LastYawRate { get; set; }
        public bool HasBrakeLockup { get; set; }
        public float TimeInCorner { get; set; }
    }

    private ActiveCornerState? _activeCorner;
    private readonly Dictionary<int, List<CornerPerformance>> _lapCornerCache = new(); // lap number -> list of corners
    private const int CACHE_LAPS = 10;
    private const float STEERING_CHANGE_THRESHOLD = 0.1f; // Rapid steering change detection

    public CornerPerformanceTracker(ChiefDatabase db, TrackMapService mapService)
    {
        _db = db;
        _mapService = mapService;
    }

    /// <summary>
    /// Process a telemetry sample while the car is in a corner zone.
    /// </summary>
    public void ProcessTelemetrySample(TelemetryRecord sample, TrackCorner? currentCorner)
    {
        if (currentCorner == null) return;

        // Start tracking a new corner
        if (_activeCorner == null || _activeCorner.CornerId != currentCorner.Id)
        {
            _activeCorner = new ActiveCornerState
            {
                CornerId = currentCorner.Id,
                LapNumber = sample.LapNumber,
                GearAtEntry = sample.Gear,
            };
        }

        // Track braking phase
        if (sample.Brake > 0.01f && _activeCorner.BrakeStartDistPct < 0)
        {
            _activeCorner.BrakeStartDistPct = sample.LapDistPct;
        }

        if (sample.Brake > _activeCorner.PeakBrakePressure)
        {
            _activeCorner.PeakBrakePressure = sample.Brake;
        }

        // Detect brake lockup (brake pressure high, but wheel speeds indicate lockup)
        if (sample.Brake > 0.9f && _activeCorner.Speed > 20 && _activeCorner.LastYawRate > 2.0f)
        {
            _activeCorner.HasBrakeLockup = true;
        }

        // Track entry speed (first measurement in corner at turn-in)
        if (sample.LapDistPct >= currentCorner.TurnInDistPct &&
            sample.LapDistPct < currentCorner.ApexDistPct &&
            _activeCorner.EntrySpeed == float.MaxValue)
        {
            _activeCorner.EntrySpeed = sample.Speed;
            _activeCorner.GearAtEntry = sample.Gear;
        }

        // Track apex speed and min speed
        if (sample.LapDistPct >= currentCorner.ApexDistPct - 0.02f &&
            sample.LapDistPct <= currentCorner.ApexDistPct + 0.02f)
        {
            _activeCorner.ApexSpeed = sample.Speed;
            _activeCorner.GearAtApex = sample.Gear;
        }

        if (sample.Speed < _activeCorner.MinSpeed)
        {
            _activeCorner.MinSpeed = sample.Speed;
        }

        // Track throttle pickup
        if (sample.Throttle > 0.3f && _activeCorner.ThrottlePickupDistPct < 0)
        {
            _activeCorner.ThrottlePickupDistPct = sample.LapDistPct;
        }

        // Detect steering corrections (rapid steering changes)
        float steeringDelta = System.Math.Abs(sample.Steering - _activeCorner.LastSteeringInput);
        if (steeringDelta > STEERING_CHANGE_THRESHOLD && sample.LapDistPct > currentCorner.ApexDistPct)
        {
            _activeCorner.SteeringCorrectionCount++;
        }
        _activeCorner.LastSteeringInput = sample.Steering;
        _activeCorner.LastYawRate = sample.YawRate;

        // Track exit speed (as car exits corner zone)
        if (sample.LapDistPct >= currentCorner.ExitDistPct - 0.01f &&
            sample.LapDistPct <= currentCorner.ExitDistPct + 0.02f)
        {
            _activeCorner.ExitSpeed = sample.Speed;
        }
    }

    /// <summary>
    /// Called when car exits the current corner zone. Finalizes the corner performance
    /// and stores it.
    /// </summary>
    public void OnCornerExited(string sessionId)
    {
        if (_activeCorner == null) return;

        // Build corner performance record
        var perf = new CornerPerformance
        {
            CornerId = _activeCorner.CornerId,
            SessionId = sessionId,
            LapNumber = _activeCorner.LapNumber,
            BrakeStartDistPct = _activeCorner.BrakeStartDistPct >= 0 ? _activeCorner.BrakeStartDistPct : 0,
            PeakBrakePressure = _activeCorner.PeakBrakePressure,
            BrakeReleaseRate = _activeCorner.BrakeReleaseRate,
            EntrySpeed = _activeCorner.EntrySpeed < float.MaxValue ? _activeCorner.EntrySpeed : 0,
            ApexSpeed = _activeCorner.ApexSpeed,
            MinSpeed = _activeCorner.MinSpeed < float.MaxValue ? _activeCorner.MinSpeed : 0,
            ThrottlePickupDistPct = _activeCorner.ThrottlePickupDistPct >= 0 ? _activeCorner.ThrottlePickupDistPct : 0,
            ExitSpeed = _activeCorner.ExitSpeed,
            GearAtEntry = _activeCorner.GearAtEntry,
            GearAtApex = _activeCorner.GearAtApex,
            SteeringCorrectionCount = _activeCorner.SteeringCorrectionCount,
            DeltaGainedLost = 0, // Will be calculated when comparing to best lap
            MistakeCategory = DetectMistakeCategory(_activeCorner, perf)
        };

        // Store in database
        _db.InsertCornerPerformance(perf);

        // Cache in memory
        if (!_lapCornerCache.ContainsKey(perf.LapNumber))
        {
            _lapCornerCache[perf.LapNumber] = new List<CornerPerformance>();
        }
        _lapCornerCache[perf.LapNumber].Add(perf);

        // Clear active corner
        _activeCorner = null;
    }

    /// <summary>
    /// Called when a lap is completed. Finalizes all corner data for that lap.
    /// </summary>
    public void OnLapCompleted(int lap, string sessionId)
    {
        // Prune old cache entries
        while (_lapCornerCache.Count > CACHE_LAPS)
        {
            var oldestLap = _lapCornerCache.Keys.Min();
            _lapCornerCache.Remove(oldestLap);
        }
    }

    /// <summary>
    /// Detect what type of mistake was made in this corner.
    /// </summary>
    private string DetectMistakeCategory(ActiveCornerState state, CornerPerformance perf)
    {
        if (state.HasBrakeLockup)
            return "lockup";

        // Get the corner definition to compare against targets
        var corners = _mapService.GetCornersForSession(perf.SessionId);
        var cornerDef = corners?.FirstOrDefault(c => c.Id == state.CornerId);

        if (cornerDef == null)
            return "none";

        // Check each mistake category
        if (perf.BrakeStartDistPct > cornerDef.BrakeZoneDistPct + 0.01f)
            return "braked_late";

        if (perf.BrakeStartDistPct < cornerDef.BrakeZoneDistPct - 0.015f && perf.BrakeStartDistPct > 0)
            return "braked_early";

        if (perf.MinSpeed < cornerDef.TargetMinSpeed * 0.85f)
            return "over_slowed";

        if (state.LastYawRate > 3.5f)
            return "snap_oversteer";

        // Missed apex: minimum speed point is far from apex
        float minSpeedDist = System.Math.Abs(perf.ApexSpeed - perf.MinSpeed) /
            System.Math.Max(perf.ApexSpeed, 1);
        if (minSpeedDist > 0.01f)
            return "missed_apex";

        if (perf.ExitSpeed < cornerDef.TargetExitSpeed * 0.92f)
            return "poor_exit";

        return "none";
    }

    /// <summary>
    /// Get the corner performance for the last lap in a specific corner.
    /// </summary>
    public CornerPerformance? GetLastLapCorner(string cornerId)
    {
        foreach (var lap in _lapCornerCache.Keys.OrderByDescending(x => x))
        {
            var perf = _lapCornerCache[lap].FirstOrDefault(p => p.CornerId == cornerId);
            if (perf != null) return perf;
        }
        return null;
    }

    /// <summary>
    /// Get the best (fastest minimum speed) lap's corner performance.
    /// </summary>
    public CornerPerformance? GetBestLapCorner(string cornerId)
    {
        CornerPerformance? best = null;
        foreach (var perfList in _lapCornerCache.Values)
        {
            var perf = perfList.FirstOrDefault(p => p.CornerId == cornerId);
            if (perf != null && (best == null || perf.ExitSpeed > best.ExitSpeed))
                best = perf;
        }
        return best;
    }

    /// <summary>
    /// Get mistake patterns for a corner across the last N laps.
    /// Returns list of mistake categories found (e.g., ["braked_late", "snap_oversteer"]).
    /// </summary>
    public List<string> GetMistakePatterns(string cornerId, int lastNLaps)
    {
        var mistakes = new List<string>();
        var recentLaps = _lapCornerCache.Keys.OrderByDescending(x => x).Take(lastNLaps);

        foreach (var lap in recentLaps)
        {
            var perf = _lapCornerCache[lap].FirstOrDefault(p => p.CornerId == cornerId);
            if (perf != null && !string.IsNullOrEmpty(perf.MistakeCategory) && perf.MistakeCategory != "none")
            {
                mistakes.Add(perf.MistakeCategory);
            }
        }

        return mistakes;
    }
}

/// <summary>
/// Extension methods for accessing session corner data.
/// </summary>
public static class TrackMapServiceExtensions
{
    /// <summary>
    /// Get all corners for a session (loads track map based on session info).
    /// </summary>
    public static List<TrackCorner>? GetCornersForSession(this TrackMapService service, string sessionId)
    {
        // This would be called from Database to get session info, then load the track map
        // For now, we return null - the caller should pass the correct context
        return null;
    }
}
