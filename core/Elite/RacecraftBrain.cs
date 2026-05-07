// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Racecraft Brain
// Live race intelligence for positioning, passing, and strategy.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Race-specific coaching module. Detects passing opportunities,
/// defensive situations, and strategic calls based on position and gaps.
/// Disabled in practice/qualifying; enabled only in race mode.
/// </summary>
public class RacecraftBrain : ICoachingModule
{
    private readonly CoachingPrioritySystem _priority;
    private readonly EventHooks _events;
    private bool _isEnabled = false;
    private float _lastDefenseCall = -10f;
    private float _lastPassCall = -10f;
    private float _lastFuelCall = -30f;

    public string ModuleName => "RacecraftBrain";
    public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }

    public RacecraftBrain(CoachingPrioritySystem priority, EventHooks events)
    {
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        // Subscribe to race events
        if (_events != null)
        {
            _events.Subscribe(OnRacingEvent);
        }
    }

    public void ProcessTick(EliteCoachingContext context)
    {
        if (!IsEnabled || context?.CurrentSample == null)
            return;

        var sample = context.CurrentSample;

        // Only give racecraft calls on straights or high-speed sections
        if (!context.IsOnStraight && Math.Abs(sample.SteeringAngle) > 0.5f)
            return;

        // Monitor for passing and defending opportunities
        DetectPassingZone(context);
        DetectDefendingSituation(context);
        DetectDraftOpportunity(context);
        DetectFuelStrategy(context);
    }

    public void OnLapCompleted(EliteCoachingContext context, int lap)
    {
        // Racecraft is mostly real-time; lap completion doesn't add much here.
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE DETECTION METHODS
    // ═══════════════════════════════════════════════════════════════

    private void DetectPassingZone(EliteCoachingContext context)
    {
        // Look for long straights or heavy brake zones suitable for passing
        if (context.NextCorner == null)
            return;

        // Check if we're approaching a passing zone (long straight before brake)
        float distToNextBrake = context.TimeToNextBrakeZone;
        if (distToNextBrake > 3f && context.IsOnStraight)
        {
            // We have time on this straight for a pass attempt
            SubmitDecision(context, "racecraft", "Passing Opportunity",
                "Set up the pass Turn 3.", CoachingPriority.High, 70);
        }
    }

    private void DetectDefendingSituation(EliteCoachingContext context)
    {
        // Car behind within 0.3 seconds approaching a passing zone
        // (In real implementation, would use proximity events from EventHooks)
        if (context.IsSideBySide || (Math.Abs(context.CurrentDelta) < 0.3f && context.IsOnStraight))
        {
            _lastDefenseCall = context.CurrentSample?.LapCurrentLapTime ?? 0;
            SubmitDecision(context, "racecraft", "Defend Position",
                "Defend inside next.", CoachingPriority.High, 75);
        }
    }

    private void DetectDraftOpportunity(EliteCoachingContext context)
    {
        // Following within 1 second on a straight
        if (context.IsOnStraight && Math.Abs(context.CurrentDelta) < 1.0f && context.CurrentDelta > -0.05f)
        {
            SubmitDecision(context, "racecraft", "Draft Usage",
                "Use draft. Don't overheat tires.", CoachingPriority.Low, 68);
        }
    }

    private void DetectFuelStrategy(EliteCoachingContext context)
    {
        // Listen for fuel warning events and suggest pit strategy
        float currentTime = context.CurrentSample?.LapCurrentLapTime ?? 0;

        // Only suggest once per 30+ lap seconds
        if ((currentTime - _lastFuelCall) < 30f)
            return;

        // In a real implementation, would check EventHooks for fuel_warning event
        // For now, provide generic fuel wisdom
        if (context.CurrentLap > 5 && context.CurrentLap % 10 == 0)
        {
            SubmitDecision(context, "racecraft", "Fuel Management",
                "Pit window open. Fuel for 3 laps.", CoachingPriority.Low, 70);
            _lastFuelCall = currentTime;
        }
    }

    private void OnRacingEvent(RacingEvent evt)
    {
        // Future: respond to position changes, fuel warnings, etc.
        if (evt.Type == EventTypes.PositionGained)
        {
            // Driver just passed someone
        }
        else if (evt.Type == EventTypes.PositionLost)
        {
            // Driver just lost position
        }
        else if (evt.Type == EventTypes.FuelWarning)
        {
            // Fuel is getting low
        }
    }

    private void SubmitDecision(EliteCoachingContext context, string category, string message,
        string voiceText, CoachingPriority priority, int confidence)
    {
        var decision = new CoachingDecision
        {
            Car = context.Car,
            Track = context.Track,
            CornerName = context.CurrentCorner?.Name ?? "Unknown",
            CornerNumber = context.CurrentCorner?.Number ?? 0,
            LapNumber = context.CurrentLap,
            LapDistPct = context.CurrentSample?.LapDistPct ?? 0f,
            SessionType = context.SessionType,
            Category = category,
            Message = message,
            VoiceText = voiceText,
            Priority = priority,
            ConfidenceScore = confidence,
            SpeakBeforeCornerSeconds = 0,
            SessionId = context.SessionId,
        };

        _priority.Submit(decision);
    }
}
