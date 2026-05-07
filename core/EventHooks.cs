// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Event Hooks for Future Voice Coaching
// Fires events for key racing moments. Phase 1 = framework only.
// Voice/audio will be added in Phase 2.
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Text.Json;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// EVENT TYPES
// ═══════════════════════════════════════

public static class EventTypes
{
    // Session events
    public const string SessionStarted = "session_started";
    public const string SessionEnded = "session_ended";

    // Lap events
    public const string LapStarted = "lap_started";
    public const string LapCompleted = "lap_completed";

    // Driving events
    public const string OffTrack = "off_track";
    public const string BrakeLockup = "brake_lockup";
    public const string WheelSpin = "wheel_spin";
    public const string OverSlowed = "over_slowed_corner";
    public const string ThrottleSpike = "throttle_spike";
    public const string SteeringCorrection = "steering_correction";
    public const string Incident = "incident";

    // Alerts
    public const string FuelWarning = "fuel_warning";
    public const string PitWindow = "pit_window";
    public const string DeltaImproving = "delta_improving";
    public const string DeltaFalling = "delta_falling";
    public const string PersonalBest = "personal_best";
    public const string TireWearHigh = "tire_wear_high";

    // Position
    public const string PositionGained = "position_gained";
    public const string PositionLost = "position_lost";
    public const string CarNear = "car_near";    // Future: proximity detection
}

// ═══════════════════════════════════════
// EVENT DATA
// ═══════════════════════════════════════

public class RacingEvent
{
    public string Type { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int LapNumber { get; set; }
    public float LapDistPct { get; set; }
    public long TimestampMs { get; set; }
    public string Severity { get; set; } = "info";    // info, warning, critical
    public string Message { get; set; } = "";
    public Dictionary<string, object>? Data { get; set; }
}

// ═══════════════════════════════════════
// EVENT HOOKS ENGINE
// ═══════════════════════════════════════

public class EventHooks
{
    private readonly ChiefDatabase _db;
    private readonly ConcurrentQueue<RacingEvent> _eventQueue = new();
    private readonly List<Action<RacingEvent>> _listeners = new();
    private readonly object _listenerLock = new();

    // Telemetry state for event detection
    private float _prevBrake = 0f;
    private float _prevThrottle = 0f;
    private float _prevSteering = 0f;
    private float _prevDelta = 0f;
    private int _prevPosition = 0;
    private int _prevLap = 0;
    private bool _wasOffTrack = false;

    // Event log (recent, for UI display)
    public ConcurrentQueue<RacingEvent> RecentEvents { get; } = new();
    private const int MaxRecentEvents = 100;

    public EventHooks(ChiefDatabase db)
    {
        _db = db;
    }

    // ═══ SUBSCRIBE ═══

    public void Subscribe(Action<RacingEvent> listener)
    {
        lock (_listenerLock) _listeners.Add(listener);
    }

    public void Unsubscribe(Action<RacingEvent> listener)
    {
        lock (_listenerLock) _listeners.Remove(listener);
    }

    // ═══ FIRE EVENT ═══

    public void FireEvent(string type, string sessionId, int lap, float distPct,
        string message, string severity = "info", Dictionary<string, object>? data = null)
    {
        var evt = new RacingEvent
        {
            Type = type,
            SessionId = sessionId,
            LapNumber = lap,
            LapDistPct = distPct,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Severity = severity,
            Message = message,
            Data = data,
        };

        // Queue for processing
        _eventQueue.Enqueue(evt);

        // Add to recent events
        RecentEvents.Enqueue(evt);
        while (RecentEvents.Count > MaxRecentEvents) RecentEvents.TryDequeue(out _);

        // Notify listeners
        List<Action<RacingEvent>> listeners;
        lock (_listenerLock) listeners = new List<Action<RacingEvent>>(_listeners);
        foreach (var listener in listeners)
        {
            try { listener(evt); }
            catch { /* Don't let listener errors break the event system */ }
        }

        // Persist to database
        try
        {
            _db.InsertCoachingEvent(new CoachingEvent
            {
                SessionId = sessionId,
                LapNumber = lap,
                LapDistPct = distPct,
                TimestampMs = evt.TimestampMs,
                EventType = type,
                Severity = severity,
                Message = message,
                Data = data != null ? JsonSerializer.Serialize(data) : "",
            });
        }
        catch { /* DB write failure shouldn't break logging */ }
    }

    // ═══ ANALYZE TELEMETRY FOR EVENTS ═══
    // Call this from TelemetryRecorder's OnSample handler

    public void AnalyzeSample(TelemetrySample s, string sessionId)
    {
        // Brake lockup detection: high brake + low speed change (simplified)
        if (s.Brake > 0.95f && Math.Abs(s.LongAccel) < 0.5f && _prevBrake > 0.9f)
        {
            FireEvent(EventTypes.BrakeLockup, sessionId, s.Lap, s.LapDistPct,
                "Possible brake lockup detected", "warning",
                new Dictionary<string, object> { ["brake"] = s.Brake, ["speed"] = s.Speed });
        }

        // Throttle spike: sudden large throttle application
        if (s.Throttle - _prevThrottle > 0.6f && s.Speed > 20f)
        {
            FireEvent(EventTypes.ThrottleSpike, sessionId, s.Lap, s.LapDistPct,
                "Aggressive throttle application", "info",
                new Dictionary<string, object> { ["throttle_delta"] = s.Throttle - _prevThrottle });
        }

        // Steering correction: rapid steering reversal
        var steeringDelta = Math.Abs(s.SteeringAngle - _prevSteering);
        if (steeringDelta > 0.5f && s.Speed > 30f) // ~28 degrees
        {
            FireEvent(EventTypes.SteeringCorrection, sessionId, s.Lap, s.LapDistPct,
                "Large steering correction", "info",
                new Dictionary<string, object> { ["steering_delta_rad"] = steeringDelta });
        }

        // Delta improving / falling
        if (s.LapDeltaToSessionBestLap < _prevDelta - 0.3f)
        {
            FireEvent(EventTypes.DeltaImproving, sessionId, s.Lap, s.LapDistPct,
                $"Delta improving: {s.LapDeltaToSessionBestLap:+0.000;-0.000}s");
        }
        else if (s.LapDeltaToSessionBestLap > _prevDelta + 0.5f)
        {
            FireEvent(EventTypes.DeltaFalling, sessionId, s.Lap, s.LapDistPct,
                $"Delta falling: {s.LapDeltaToSessionBestLap:+0.000;-0.000}s", "warning");
        }

        // Position changes
        if (s.TrackPosition > 0 && _prevPosition > 0)
        {
            if (s.TrackPosition < _prevPosition)
                FireEvent(EventTypes.PositionGained, sessionId, s.Lap, s.LapDistPct,
                    $"P{_prevPosition} → P{s.TrackPosition}");
            else if (s.TrackPosition > _prevPosition)
                FireEvent(EventTypes.PositionLost, sessionId, s.Lap, s.LapDistPct,
                    $"P{_prevPosition} → P{s.TrackPosition}", "warning");
        }

        // Update state
        _prevBrake = s.Brake;
        _prevThrottle = s.Throttle;
        _prevSteering = s.SteeringAngle;
        _prevDelta = s.LapDeltaToSessionBestLap;
        _prevPosition = s.TrackPosition;
        _prevLap = s.Lap;
    }
}
