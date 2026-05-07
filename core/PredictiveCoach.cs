// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Predictive Coach
// Core predictive coaching engine with lookahead voice call generation.
// Analyzes telemetry and generates context-aware coaching at optimal moments.
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// PERFORMANCE PROVIDER INTERFACE
// ═══════════════════════════════════════

/// <summary>
/// Interface for accessing corner performance data.
/// Implemented by CornerPerformanceTracker (created by another agent).
/// </summary>
public interface ICornerPerformanceProvider
{
    /// <summary>Get performance data for a corner from the last lap</summary>
    CornerPerformance? GetLastLapCorner(string cornerId);

    /// <summary>Get performance data for a corner from the best lap</summary>
    CornerPerformance? GetBestLapCorner(string cornerId);

    /// <summary>Get list of mistake patterns for a corner over last N laps</summary>
    List<string> GetMistakePatterns(string cornerId, int lastNLaps);
}

/// <summary>
/// Corner performance data from a single lap.
/// </summary>
public record CornerPerformance(
    string CornerName,
    float EntrySpeed,
    float MinSpeed,
    float ExitSpeed,
    float BrakePressure,
    float BrakeStartDist,
    float ApexLat,
    float ApexLong,
    bool OnlyLine,
    string Timestamp
);

// ═══════════════════════════════════════
// COACHING CONFIG
// ═══════════════════════════════════════

/// <summary>Configuration for predictive coaching behavior</summary>
public record PredictiveCoachConfig(
    bool Enabled,
    string Intensity,                   // calm, aggressive
    float SecondsToBrakeZoneEarly,      // 3-6s window
    float SecondsToBrakeZoneShort,      // 1-2s reminder window
    bool AllowDuringBraking,
    bool PreferStraightsTalking,
    int MaxCallsPerCornerPerLap
);

// ═══════════════════════════════════════
// COACHING CALL
// ═══════════════════════════════════════

/// <summary>A single predictive coaching call event</summary>
public record PredictiveCall(
    string CornerName,
    string CallText,
    int LapNumber,
    float LapDistPct,
    string Category,                    // main, reminder, dynamic_adjust
    long TimestampMs
);

// ═══════════════════════════════════════
// PREDICTIVE COACH ENGINE
// ═══════════════════════════════════════

/// <summary>
/// Core predictive coaching engine.
/// Analyzes real-time telemetry and generates contextual voice coaching
/// with intelligent lookahead and performance-based call generation.
/// </summary>
public class PredictiveCoach
{
    private readonly IRacingSdk _sdk;
    private readonly TrackMapService _mapService;
    private readonly ICornerPerformanceProvider _perfTracker;
    private readonly TalkTimingSystem _timing;
    private PredictiveCoachConfig _config;

    // Session state
    private TrackMap? _currentTrackMap;
    private string _sessionMode = "practice"; // practice, qualifying, race
    private int _currentLapNumber = 0;

    // Call tracking
    private TrackCorner? _nextCorner;
    private string _lastCalloutCorner = "";
    private int _lastCalloutLap = -1;
    private readonly Dictionary<string, CalloutInfo> _cornerCalloutHistory = new();

    // Performance state
    private float _lastSpeed = 0f;
    private float _lastBrake = 0f;
    private float _lastThrottle = 0f;
    private int _lapsSinceLastCall = 0;

    public event Action<PredictiveCall>? OnPredictiveCall;

    public PredictiveCoach(
        IRacingSdk sdk,
        TrackMapService mapService,
        ICornerPerformanceProvider perfTracker,
        TalkTimingSystem timing,
        PredictiveCoachConfig config)
    {
        _sdk = sdk;
        _mapService = mapService;
        _perfTracker = perfTracker;
        _timing = timing;
        _config = config;
    }

    // ═══════════════════════════════════════
    // CONFIG & MODE
    // ═══════════════════════════════════════

    /// <summary>Update coaching configuration at runtime</summary>
    public void UpdateConfig(PredictiveCoachConfig config)
    {
        _config = config;
    }

    /// <summary>Set session mode (affects coaching strategy)</summary>
    public void SetSessionMode(string mode)
    {
        _sessionMode = mode;
    }

    // ═══════════════════════════════════════
    // SESSION LIFECYCLE
    // ═══════════════════════════════════════

    /// <summary>
    /// Called when a new session starts.
    /// Loads the track map for this track/car combination.
    /// </summary>
    public void OnSessionStarted(string trackName, string carClass)
    {
        _currentTrackMap = _mapService.LoadTrackMap(trackName, carClass);
        _currentLapNumber = 0;
        _lastCalloutCorner = "";
        _lastCalloutLap = -1;
        _cornerCalloutHistory.Clear();
    }

    /// <summary>Called when a lap completes. Resets per-lap state.</summary>
    public void OnLapCompleted(int lapNumber)
    {
        _currentLapNumber = lapNumber;
        _lastCalloutCorner = "";
        _lapsSinceLastCall = 0;
    }

    // ═══════════════════════════════════════
    // MAIN UPDATE LOOP
    // ═══════════════════════════════════════

    /// <summary>
    /// Called every telemetry tick (20Hz).
    /// Analyzes current position and generates predictive voice calls.
    /// </summary>
    public void Update(TelemetrySample sample)
    {
        if (!_config.Enabled || _currentTrackMap == null || _currentTrackMap.Corners.Count == 0)
        {
            return;
        }

        // Update state
        _lastSpeed = sample.Speed;
        _lastBrake = sample.Brake;
        _lastThrottle = sample.Throttle;

        // Find next corner
        _nextCorner = _currentTrackMap.GetNextCorner(sample.LapDistPct);
        if (_nextCorner == null) return;

        // Calculate seconds to next brake zone
        float secsToNextBrake = _currentTrackMap.SecondsToNextBrakeZone(
            sample.LapDistPct,
            sample.Speed / 2.237f, // mph to m/s
            _currentTrackMap.TrackLengthKm * 1000f
        );

        // Main predictive lookahead: 3-6 seconds before brake zone
        if (secsToNextBrake >= _config.SecondsToBrakeZoneEarly &&
            secsToNextBrake <= _config.SecondsToBrakeZoneEarly + 3f)
        {
            if (ShouldMakeMainCall(_nextCorner))
            {
                GenerateMainCall(_nextCorner, sample);
            }
        }

        // Short reminder: 1-2 seconds before brake zone
        else if (secsToNextBrake >= _config.SecondsToBrakeZoneShort &&
                 secsToNextBrake <= _config.SecondsToBrakeZoneEarly)
        {
            if (IsOffPaceOrUnprepared(_nextCorner, sample))
            {
                GenerateShortReminder(_nextCorner, sample);
            }
        }

        // Never speak during heavy braking
        if (_lastBrake > 0.7f)
        {
            return;
        }

        // Prefer speaking on straights (low steering angle)
        if (_config.PreferStraightsTalking && Math.Abs(sample.Steering) > 0.3f)
        {
            return;
        }
    }

    // ═══════════════════════════════════════
    // DECISION LOGIC
    // ═══════════════════════════════════════

    private bool ShouldMakeMainCall(TrackCorner corner)
    {
        if (corner == null) return false;

        // Don't repeat the same corner instruction more than once per lap
        if (_lastCalloutCorner == corner.Id && _lastCalloutLap == _currentLapNumber)
        {
            return false;
        }

        // Check max calls per corner per lap
        string histKey = $"{corner.Id}_{_currentLapNumber}";
        if (_cornerCalloutHistory.ContainsKey(histKey))
        {
            var info = _cornerCalloutHistory[histKey];
            if (info.CallCount >= _config.MaxCallsPerCornerPerLap)
            {
                return false;
            }
        }

        // Check if timing system allows speaking
        if (!_timing.IsGoodTimeToTalk())
        {
            return false;
        }

        return true;
    }

    private bool IsOffPaceOrUnprepared(TrackCorner corner, TelemetrySample sample)
    {
        if (corner == null) return false;

        var lastLap = _perfTracker.GetLastLapCorner(corner.Id);
        var bestLap = _perfTracker.GetBestLapCorner(corner.Id);

        if (lastLap == null) return false;

        // Off pace if significantly slower than best
        if (bestLap != null)
        {
            float speedDelta = bestLap.MinSpeed - lastLap.MinSpeed;
            if (speedDelta > 5f) return true; // More than 5 mph slower
        }

        // Unprepared if brake pressure doesn't match target
        if (Math.Abs(lastLap.BrakePressure - corner.TargetBrakePressure) > 20f)
        {
            return true;
        }

        return false;
    }

    private void GenerateMainCall(TrackCorner corner, TelemetrySample sample)
    {
        string callText = BuildDynamicVoiceCall(corner, sample);

        RecordCallout(corner);

        var call = new PredictiveCall(
            corner.CornerName,
            callText,
            _currentLapNumber,
            sample.LapDistPct,
            "main",
            sample.TimestampMs
        );

        OnPredictiveCall?.Invoke(call);
    }

    private void GenerateShortReminder(TrackCorner corner, TelemetrySample sample)
    {
        string callText = $"{corner.CornerName}. Brake at {corner.BrakeMarker}.";

        var call = new PredictiveCall(
            corner.CornerName,
            callText,
            _currentLapNumber,
            sample.LapDistPct,
            "reminder",
            sample.TimestampMs
        );

        OnPredictiveCall?.Invoke(call);
    }

    // ═══════════════════════════════════════
    // VOICE CALL GENERATION
    // ═══════════════════════════════════════

    private string BuildDynamicVoiceCall(TrackCorner corner, TelemetrySample sample)
    {
        var lastLap = _perfTracker.GetLastLapCorner(corner.Id);
        var bestLap = _perfTracker.GetBestLapCorner(corner.Id);
        var mistakes = _perfTracker.GetMistakePatterns(corner.Id, 3);

        // If no performance data, use default call
        if (lastLap == null)
        {
            return corner.DefaultVoiceCall;
        }

        // Build dynamic call based on performance
        var callBuilder = new List<string>();
        callBuilder.Add(corner.CornerName);

        // Braking feedback
        if (bestLap != null)
        {
            float brakeDelta = lastLap.BrakePressure - bestLap.BrakePressure;
            if (brakeDelta > 15f)
            {
                callBuilder.Add("Brake half a car earlier this lap.");
            }
            else if (brakeDelta < -15f)
            {
                callBuilder.Add("Later brake. Commit to the 3.");
            }
        }

        // Speed feedback
        float entryDelta = lastLap.EntrySpeed - corner.TargetEntrySpeed;
        if (Math.Abs(entryDelta) > 5f)
        {
            if (entryDelta < 0)
            {
                callBuilder.Add("Entry too slow. Carry more speed.");
            }
            else
            {
                callBuilder.Add("Check entry speed. Too hot.");
            }
        }

        // Exit speed feedback
        float exitDelta = lastLap.ExitSpeed - corner.TargetExitSpeed;
        if (exitDelta < -5f)
        {
            if (mistakes.Contains("loose_exit"))
            {
                callBuilder.Add("Wait on throttle. Let wheels grip first.");
            }
            else
            {
                callBuilder.Add("Give up entry. Focus on drive off.");
            }
        }

        // Apex feedback
        if (!lastLap.OnlyLine && bestLap?.OnlyLine == true)
        {
            callBuilder.Add("Tighter line. Aim for the apex.");
        }

        // Mode-specific adjustments
        if (_sessionMode == "qualifying")
        {
            callBuilder.Add("Commit.");
        }
        else if (_sessionMode == "race")
        {
            callBuilder.Add("Smooth.");
        }

        return string.Join(" ", callBuilder);
    }

    private void RecordCallout(TrackCorner corner)
    {
        _lastCalloutCorner = corner.Id;
        _lastCalloutLap = _currentLapNumber;

        string histKey = $"{corner.Id}_{_currentLapNumber}";
        if (_cornerCalloutHistory.ContainsKey(histKey))
        {
            _cornerCalloutHistory[histKey].CallCount++;
        }
        else
        {
            _cornerCalloutHistory[histKey] = new CalloutInfo { CallCount = 1 };
        }

        // Cleanup old history entries
        if (_cornerCalloutHistory.Count > 100)
        {
            var oldestKey = _cornerCalloutHistory
                .OrderBy(x => x.Value.FirstCallTime)
                .First()
                .Key;
            _cornerCalloutHistory.Remove(oldestKey);
        }
    }

    // ═══════════════════════════════════════
    // HELPER CLASSES
    // ═══════════════════════════════════════

    private class CalloutInfo
    {
        public int CallCount { get; set; }
        public DateTime FirstCallTime { get; set; } = DateTime.UtcNow;
    }
}
