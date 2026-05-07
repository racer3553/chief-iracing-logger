// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Talk Timing System
// Controls when the voice engine is allowed to speak based on
// driving state, race conditions, and context suppression rules.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// TALK TIMING SYSTEM
// ═══════════════════════════════════════

public class TalkTimingSystem
{
    private readonly IRacingSdk _iracing;

    // State tracking
    private DateTime _lastSpeakTime = DateTime.UtcNow;
    private DateTime _cornerExitTime = DateTime.UtcNow;
    private bool _isInBrakeZone = false;
    private bool _isInCorner = false;
    private bool _isSideBySide = false;
    private bool _isRaceStart = false;
    private int _lastLap = 1;
    private float _lastBrake = 0f;
    private float _lastSteering = 0f;

    // Configuration thresholds
    private const float BrakeThreshold = 0.1f;              // 10% brake = in brake zone
    private const float SteeringThreshold = 0.3f;           // radians (~17 degrees)
    private const float HeavyBrakingThreshold = 0.5f;       // 50% brake = heavy
    private const float CornerExitWaitTime = 0.5f;          // seconds after brake release
    private const float RaceStartSuppression = 10f;         // seconds (first 10s of lap 1)
    private const float MinimumMessageGap = 2f;             // seconds between any messages
    private const float MinimumCoachingGap = 4f;            // seconds between coaching messages

    public TalkTimingSystem(IRacingSdk iracing)
    {
        _iracing = iracing;
    }

    // ═══ MAIN ENTRY POINT ═══
    public bool CanSpeak()
    {
        // Check message gaps
        float timeSinceLastMessage = (float)(DateTime.UtcNow - _lastSpeakTime).TotalSeconds;
        if (timeSinceLastMessage < MinimumMessageGap)
            return false;

        // Cannot speak during heavy braking
        if (_lastBrake > HeavyBrakingThreshold)
            return false;

        // Cannot speak on straights during corners (wait for corner exit)
        if (_isInCorner)
            return false;

        // Cannot speak during race start
        if (_isRaceStart)
            return false;

        // Cannot speak during side-by-side racing (for coaching)
        if (_isSideBySide)
            return false;

        return true;
    }

    // ═══ PRIORITY-BASED SPEAK CHECK ═══
    public bool CanSpeakPriority(string priority)
    {
        switch (priority.ToLower())
        {
            case "spotter":
                // Spotter always allowed (passing/wreck info is critical)
                return true;

            case "safety":
                // Safety almost always allowed, except during heavy braking
                if (_lastBrake > HeavyBrakingThreshold)
                    return false;

                float timeSinceLastMessage = (float)(DateTime.UtcNow - _lastSpeakTime).TotalSeconds;
                return timeSinceLastMessage >= MinimumMessageGap;

            case "coaching":
                // Coaching only on straights, not in corners, not in brake zones
                return CanCoachingSpeak();

            case "info":
                // Info only on long straights
                return CanInfoSpeak();

            default:
                return CanSpeak();
        }
    }

    // ═══ UPDATE STATE FROM TELEMETRY ═══
    public void UpdateState(TelemetrySample sample)
    {
        // Detect lap change
        if (sample.Lap != _lastLap)
        {
            _lastLap = sample.Lap;

            // Reset race start suppression on lap 1
            if (sample.Lap == 1)
            {
                _isRaceStart = true;
                _cornerExitTime = DateTime.UtcNow.AddSeconds(RaceStartSuppression);
            }
            else
            {
                _isRaceStart = false;
            }
        }

        // Check if race start window has passed
        if (_isRaceStart && DateTime.UtcNow > _cornerExitTime)
        {
            _isRaceStart = false;
        }

        // Update brake zone state
        _lastBrake = sample.Brake;
        bool wasInBrakeZone = _isInBrakeZone;

        if (sample.Brake > BrakeThreshold)
        {
            _isInBrakeZone = true;
            _isInCorner = true;
        }
        else if (sample.Brake < BrakeThreshold * 0.5f) // Hysteresis
        {
            _isInBrakeZone = false;

            // If exiting brake zone, set corner exit timer
            if (wasInBrakeZone)
            {
                _cornerExitTime = DateTime.UtcNow.AddSeconds(CornerExitWaitTime);
            }
        }

        // Update corner state based on timer
        if (DateTime.UtcNow > _cornerExitTime && _isInCorner)
        {
            _isInCorner = false;
        }

        // Update steering state (large steering = in corner)
        _lastSteering = sample.SteeringAngle;
        if (Math.Abs(sample.SteeringAngle) > SteeringThreshold)
        {
            _isInCorner = true;
        }

        // Check for side-by-side racing
        // This is a simplified check; full implementation would use CarLeftRight iRacing variable
        _isSideBySide = DetectSideBySide(sample);
    }

    // ═══ COACHING-SPECIFIC SPEECH CHECK ═══
    private bool CanCoachingSpeak()
    {
        // Check minimum gap
        float timeSinceLastMessage = (float)(DateTime.UtcNow - _lastSpeakTime).TotalSeconds;
        if (timeSinceLastMessage < MinimumCoachingGap)
            return false;

        // Cannot speak during race start
        if (_isRaceStart)
            return false;

        // Cannot speak in brake zones
        if (_isInBrakeZone)
            return false;

        // Cannot speak in corners
        if (_isInCorner)
            return false;

        // Cannot speak during heavy braking
        if (_lastBrake > HeavyBrakingThreshold)
            return false;

        // Cannot speak during side-by-side
        if (_isSideBySide)
            return false;

        return true;
    }

    // ═══ INFO-SPECIFIC SPEECH CHECK ═══
    private bool CanInfoSpeak()
    {
        // Info only on long straights (brake < 5%, steering minimal)
        if (_lastBrake > 0.05f)
            return false;

        if (Math.Abs(_lastSteering) > SteeringThreshold * 0.5f)
            return false;

        float timeSinceLastMessage = (float)(DateTime.UtcNow - _lastSpeakTime).TotalSeconds;
        if (timeSinceLastMessage < MinimumMessageGap)
            return false;

        return true;
    }

    // ═══ SIDE-BY-SIDE DETECTION ═══
    private bool DetectSideBySide(TelemetrySample sample)
    {
        // Simplified detection: if lateral acceleration is high and we're not braking,
        // we might be in side-by-side. Full implementation would use iRacing's
        // CarLeftRight variable.
        //
        // In real code, you'd query the iRacing SDK for CarLeftRight:
        // - 0 = no car
        // - 1 = car to left
        // - 2 = car to right
        // - 3 = car both sides

        // For now, use lateral acceleration as a proxy
        // (though this isn't perfect)
        if (Math.Abs(sample.LatAccel) > 2.0f && sample.Brake < 0.2f)
        {
            return true;
        }

        return false;
    }

    // ═══ RECORD THAT A MESSAGE WAS SPOKEN ═══
    public void RecordMessageSpoken()
    {
        _lastSpeakTime = DateTime.UtcNow;
    }

    // ═══ STATE INSPECTION (for debugging) ═══
    public string GetDebugState()
    {
        return $"InBrakeZone={_isInBrakeZone} InCorner={_isInCorner} SideBySide={_isSideBySide} " +
               $"RaceStart={_isRaceStart} Brake={_lastBrake:F2} Steering={_lastSteering:F2}";
    }

    // ═══ STATE RESET ═══
    public void Reset()
    {
        _lastSpeakTime = DateTime.UtcNow;
        _cornerExitTime = DateTime.UtcNow;
        _isInBrakeZone = false;
        _isInCorner = false;
        _isSideBySide = false;
        _isRaceStart = false;
        _lastLap = 1;
        _lastBrake = 0f;
        _lastSteering = 0f;
    }
}
