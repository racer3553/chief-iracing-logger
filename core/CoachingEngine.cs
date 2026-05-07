// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Coaching Engine
// Subscribes to event stream and generates contextual coaching messages
// with confidence scoring and pattern tracking.
// ═══════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// COACHING CONFIG
// ═══════════════════════════════════════

public record CoachingConfig(
    bool Enabled,
    string Mode,                    // off, spotter, coach, both
    string Intensity,               // calm, aggressive
    bool CoachBrakeLockup,
    bool CoachThrottleSpike,
    bool CoachSteeringCorrection,
    bool CoachDelta,
    bool CoachFuel,
    bool CoachTires,
    bool SpotterCarNear,
    bool SpotterWreck,
    bool SpotterSlowCar
);

// ═══════════════════════════════════════
// COACHING MESSAGE
// ═══════════════════════════════════════

public record CoachingMessage(
    string Type,                    // brake_lockup, throttle_spike, steering, delta, fuel, tires, car_near, wreck, slow_car
    string Text,
    string Priority,                // spotter, safety, coaching, info
    float Confidence,               // 0.0-1.0
    string Category                 // driving_technique, tire_management, fuel_management, track_positioning
);

// ═══════════════════════════════════════
// COACHING ENGINE
// ═══════════════════════════════════════

public class CoachingEngine
{
    private readonly EventHooks _eventHooks;
    private readonly ChiefDatabase _db;
    private CoachingConfig _config;

    // Confidence tracking: messageType -> confidence (0.0-1.0)
    private readonly Dictionary<string, float> _confidenceScores = new()
    {
        { "brake_lockup", 0.5f },
        { "throttle_spike", 0.5f },
        { "steering_correction", 0.5f },
        { "delta_loss", 0.5f },
        { "fuel_warning", 0.7f },
        { "tire_wear", 0.6f },
    };

    // Cooldown tracking: messageType -> DateTime of last message
    private readonly Dictionary<string, DateTime> _lastMessageTime = new();

    // Pattern tracking: eventType -> list of LapDistPct values
    private readonly Dictionary<string, List<float>> _eventLocations = new();

    // Recent lap data for sector analysis
    private readonly ConcurrentDictionary<string, LapAnalysisData> _lapData = new();

    public event Action<CoachingMessage>? OnCoachingMessage;

    // Configurable cooldown times (seconds)
    private const float CoachingCooldown = 30f;
    private const float SpotterCooldown = 5f;
    private const float FuelWarningCooldown = 45f;

    // Pattern clustering threshold (lap distance percentage)
    private const float PatternClusterThreshold = 0.02f;

    // Confidence threshold for message delivery
    private const float MinimumConfidenceToDeliver = 0.3f;

    public CoachingEngine(EventHooks eventHooks, ChiefDatabase db, CoachingConfig config)
    {
        _eventHooks = eventHooks;
        _db = db;
        _config = config;

        _eventHooks.Subscribe(OnRacingEvent);
    }

    // ═══ CONFIG UPDATE ═══
    public void UpdateConfig(CoachingConfig config)
    {
        _config = config;
    }

    // ═══ EVENT LISTENER ═══
    private void OnRacingEvent(RacingEvent evt)
    {
        if (!_config.Enabled) return;

        // Track event locations for pattern analysis
        TrackEventLocation(evt.Type, evt.LapDistPct);

        // Process specific event types
        switch (evt.Type)
        {
            case EventTypes.BrakeLockup:
                ProcessBrakeLockup(evt);
                break;

            case EventTypes.ThrottleSpike:
                ProcessThrottleSpike(evt);
                break;

            case EventTypes.SteeringCorrection:
                ProcessSteeringCorrection(evt);
                break;

            case EventTypes.DeltaFalling:
                ProcessDeltaFalling(evt);
                break;

            case EventTypes.DeltaImproving:
                ProcessDeltaImproving(evt);
                break;

            case EventTypes.FuelWarning:
                ProcessFuelWarning(evt);
                break;

            case EventTypes.TireWearHigh:
                ProcessTireWear(evt);
                break;

            case EventTypes.CarNear:
                ProcessCarNear(evt);
                break;

            case EventTypes.Incident:
                ProcessIncident(evt);
                break;

            case EventTypes.LapCompleted:
                ProcessLapCompleted(evt);
                break;
        }
    }

    // ═══ BRAKE LOCKUP ═══
    private void ProcessBrakeLockup(RacingEvent evt)
    {
        if (!_config.CoachBrakeLockup) return;
        if (_config.Mode == "off") return;

        // Detect if this is part of a pattern
        var recentLockups = GetRecentPatternCount(EventTypes.BrakeLockup, evt.LapDistPct, PatternClusterThreshold);

        string message;
        string priority = "coaching";

        if (recentLockups >= 3)
        {
            message = $"Lockup pattern at Turn area. Try trail braking instead.";
            priority = "coaching";
            UpdateConfidence("brake_lockup", recentLockups >= 3 ? -0.15f : 0.05f); // Negative if repeating
        }
        else
        {
            message = "Ease the brake pressure here.";
            UpdateConfidence("brake_lockup", 0.05f);
        }

        DeliverMessage(new CoachingMessage(
            Type: "brake_lockup",
            Text: message,
            Priority: priority,
            Confidence: _confidenceScores["brake_lockup"],
            Category: "driving_technique"
        ), CoachingCooldown);
    }

    // ═══ THROTTLE SPIKE ═══
    private void ProcessThrottleSpike(RacingEvent evt)
    {
        if (!_config.CoachThrottleSpike) return;
        if (_config.Mode == "off") return;

        var recentSpikes = GetRecentPatternCount(EventTypes.ThrottleSpike, evt.LapDistPct, PatternClusterThreshold);

        string message;
        if (recentSpikes >= 2)
        {
            message = "Patient on throttle — wait for wheel to open more.";
            UpdateConfidence("throttle_spike", -0.15f);
        }
        else
        {
            message = "Smooth throttle application here.";
            UpdateConfidence("throttle_spike", 0.05f);
        }

        DeliverMessage(new CoachingMessage(
            Type: "throttle_spike",
            Text: message,
            Priority: "coaching",
            Confidence: _confidenceScores["throttle_spike"],
            Category: "driving_technique"
        ), CoachingCooldown);
    }

    // ═══ STEERING CORRECTION ═══
    private void ProcessSteeringCorrection(RacingEvent evt)
    {
        if (!_config.CoachSteeringCorrection) return;
        if (_config.Mode == "off") return;

        // Large corrections = overdriving
        float steeringDelta = 0.5f;
        if (evt.Data?.ContainsKey("steering_delta_rad") == true)
        {
            steeringDelta = (float)evt.Data["steering_delta_rad"];
        }

        string message;
        if (steeringDelta > 1.0f) // > ~57 degrees
        {
            message = "Overdriving entry — slow down your inputs and commit earlier.";
            UpdateConfidence("steering_correction", -0.15f);
        }
        else
        {
            message = "Smooth your hands through the apex.";
            UpdateConfidence("steering_correction", 0.05f);
        }

        DeliverMessage(new CoachingMessage(
            Type: "steering_correction",
            Text: message,
            Priority: "coaching",
            Confidence: _confidenceScores["steering_correction"],
            Category: "driving_technique"
        ), CoachingCooldown);
    }

    // ═══ DELTA ANALYSIS ═══
    private void ProcessDeltaFalling(RacingEvent evt)
    {
        if (!_config.CoachDelta) return;
        if (_config.Mode == "off") return;

        // Determine which sector based on LapDistPct
        string sector = GetSectorName(evt.LapDistPct);

        string message = $"Losing time in {sector}. Focus on this area.";
        UpdateConfidence("delta_loss", -0.1f);

        DeliverMessage(new CoachingMessage(
            Type: "delta_loss",
            Text: message,
            Priority: "coaching",
            Confidence: _confidenceScores["delta_loss"],
            Category: "track_positioning"
        ), CoachingCooldown);
    }

    private void ProcessDeltaImproving(RacingEvent evt)
    {
        if (!_config.CoachDelta) return;

        string message = "Delta improving — nice work!";
        UpdateConfidence("delta_loss", 0.1f);

        DeliverMessage(new CoachingMessage(
            Type: "delta_improving",
            Text: message,
            Priority: "info",
            Confidence: 0.8f,
            Category: "track_positioning"
        ), CoachingCooldown);
    }

    // ═══ FUEL ═══
    private void ProcessFuelWarning(RacingEvent evt)
    {
        if (!_config.CoachFuel) return;

        string message = "Fuel warning — manage consumption.";
        if (evt.Data?.ContainsKey("laps_remaining") == true)
        {
            int lapsRemaining = (int)evt.Data["laps_remaining"];
            message = lapsRemaining switch
            {
                <= 1 => "CRITICAL: Fuel for 1 lap only.",
                <= 3 => "Only 3 laps of fuel remaining.",
                <= 5 => "5 laps of fuel left — start managing.",
                _ => "Monitor fuel consumption."
            };
        }

        UpdateConfidence("fuel_warning", 0.0f); // Fuel warnings are deterministic

        DeliverMessage(new CoachingMessage(
            Type: "fuel_warning",
            Text: message,
            Priority: "safety",
            Confidence: 0.95f,
            Category: "fuel_management"
        ), FuelWarningCooldown);
    }

    // ═══ TIRE WEAR ═══
    private void ProcessTireWear(RacingEvent evt)
    {
        if (!_config.CoachTires) return;

        string message = "Tire wear detected. Adjust driving or prepare for stop.";
        UpdateConfidence("tire_wear", 0.05f);

        DeliverMessage(new CoachingMessage(
            Type: "tire_wear",
            Text: message,
            Priority: "coaching",
            Confidence: _confidenceScores["tire_wear"],
            Category: "tire_management"
        ), CoachingCooldown);
    }

    // ═══ SPOTTER: CAR NEAR ═══
    private void ProcessCarNear(RacingEvent evt)
    {
        if (!_config.SpotterCarNear) return;
        if (_config.Mode == "off" || _config.Mode == "coach") return;

        string message = "Car on your bumper.";
        if (evt.Data?.ContainsKey("relative_position") == true)
        {
            string position = (string)evt.Data["relative_position"];
            message = position switch
            {
                "left" => "Car to your left.",
                "right" => "Car to your right.",
                "behind" => "Car right behind.",
                _ => "Car nearby."
            };
        }

        DeliverMessage(new CoachingMessage(
            Type: "car_near",
            Text: message,
            Priority: "spotter",
            Confidence: 0.9f,
            Category: "track_positioning"
        ), SpotterCooldown);
    }

    // ═══ SPOTTER: INCIDENT/WRECK ═══
    private void ProcessIncident(RacingEvent evt)
    {
        if (!_config.SpotterWreck) return;
        if (_config.Mode == "off" || _config.Mode == "coach") return;

        string message = "Incident ahead!";
        DeliverMessage(new CoachingMessage(
            Type: "wreck",
            Text: message,
            Priority: "spotter",
            Confidence: 0.95f,
            Category: "track_positioning"
        ), SpotterCooldown);
    }

    // ═══ SPOTTER: SLOW CAR ═══
    private void ProcessCarNear(RacingEvent evt, bool isSlow)
    {
        if (!_config.SpotterSlowCar) return;
        if (_config.Mode == "off" || _config.Mode == "coach") return;

        string message = "Slower car ahead.";
        DeliverMessage(new CoachingMessage(
            Type: "slow_car",
            Text: message,
            Priority: "spotter",
            Confidence: 0.85f,
            Category: "track_positioning"
        ), SpotterCooldown);
    }

    // ═══ LAP COMPLETION ═══
    private void ProcessLapCompleted(RacingEvent evt)
    {
        if (evt.Data?.ContainsKey("session_id") == true)
        {
            string sessionId = (string)evt.Data["session_id"];
            // Store lap data for analysis
            var lapData = new LapAnalysisData
            {
                LapNumber = evt.LapNumber,
                LapDistPct = evt.LapDistPct,
                EventTime = DateTime.UtcNow
            };
            _lapData.TryAdd($"{sessionId}_{evt.LapNumber}", lapData);
        }
    }

    // ═══ PATTERN DETECTION ═══
    private void TrackEventLocation(string eventType, float lapDistPct)
    {
        if (!_eventLocations.ContainsKey(eventType))
        {
            _eventLocations[eventType] = new List<float>();
        }
        _eventLocations[eventType].Add(lapDistPct);

        // Keep only recent 20 occurrences
        if (_eventLocations[eventType].Count > 20)
        {
            _eventLocations[eventType].RemoveAt(0);
        }
    }

    private int GetRecentPatternCount(string eventType, float currentDistPct, float threshold)
    {
        if (!_eventLocations.ContainsKey(eventType)) return 0;

        return _eventLocations[eventType].Count(loc =>
            Math.Abs(loc - currentDistPct) < threshold);
    }

    // ═══ CONFIDENCE MANAGEMENT ═══
    private void UpdateConfidence(string messageType, float delta)
    {
        if (_confidenceScores.ContainsKey(messageType))
        {
            _confidenceScores[messageType] = Math.Max(0.0f, Math.Min(1.0f,
                _confidenceScores[messageType] + delta));
        }
    }

    // ═══ MESSAGE DELIVERY ═══
    private void DeliverMessage(CoachingMessage msg, float cooldownSeconds)
    {
        // Check confidence threshold
        if (msg.Confidence < MinimumConfidenceToDeliver)
            return;

        // Check cooldown
        string key = msg.Type;
        if (_lastMessageTime.ContainsKey(key))
        {
            var elapsed = DateTime.UtcNow - _lastMessageTime[key];
            if (elapsed.TotalSeconds < cooldownSeconds)
                return;
        }

        // Update last message time
        _lastMessageTime[key] = DateTime.UtcNow;

        // Fire event
        OnCoachingMessage?.Invoke(msg);
    }

    // ═══ SECTOR IDENTIFICATION ═══
    private string GetSectorName(float lapDistPct)
    {
        return lapDistPct switch
        {
            < 0.25f => "Sector 1 (entry)",
            < 0.50f => "Sector 2 (mid-corner)",
            < 0.75f => "Sector 3 (exit)",
            _ => "Sector 4 (straights)"
        };
    }

    // ═══ CLEANUP ═══
    public void Dispose()
    {
        _eventHooks.Unsubscribe(OnRacingEvent);
        _eventLocations.Clear();
        _lapData.Clear();
    }
}

// ═══════════════════════════════════════
// LAP ANALYSIS DATA
// ═══════════════════════════════════════

internal class LapAnalysisData
{
    public int LapNumber { get; set; }
    public float LapDistPct { get; set; }
    public DateTime EventTime { get; set; }
}
