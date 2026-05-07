// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Push Lap Brain
// Detects best-lap-pace driving and gets out of the way.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;

/// <summary>
/// Minimal interference coaching module. When driver is on best-lap pace,
/// reduces coaching to essentials only. Provides confidence calls at sector
/// boundaries if pace is maintained.
/// </summary>
public class PushLapBrain : ICoachingModule
{
    private readonly CoachingPrioritySystem _priority;
    private bool _isEnabled = true;
    private bool _isOnHotLap = false;
    private float _hotLapStartDistPct = 0f;

    public string ModuleName => "PushLapBrain";
    public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }

    /// <summary>
    /// True when driver is currently on a push/hot lap (best-lap pace).
    /// </summary>
    public bool IsOnPushLap => _isOnHotLap;

    public PushLapBrain(CoachingPrioritySystem priority)
    {
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
    }

    public void ProcessTick(EliteCoachingContext context)
    {
        if (!IsEnabled || context?.CurrentSample == null)
            return;

        var sample = context.CurrentSample;

        // Detect hot lap: on best-lap pace (delta < -0.3s) and lap > 3
        bool shouldBeOnHotLap = context.IsOnBestLapPace && context.CurrentLap > 3;

        if (shouldBeOnHotLap && !_isOnHotLap)
        {
            // Just entered hot lap mode
            _isOnHotLap = true;
            _hotLapStartDistPct = sample.LapDistPct;
        }
        else if (!shouldBeOnHotLap && _isOnHotLap)
        {
            // Just exited hot lap mode
            _isOnHotLap = false;
        }

        // If on hot lap, give minimal calls at sector boundaries
        if (_isOnHotLap)
        {
            ProcessHotLapCoaching(context);
        }
    }

    public void OnLapCompleted(EliteCoachingContext context, int lap)
    {
        // Reset hot lap flag at lap boundary
        if (_isOnHotLap && context.IsOnBestLapPace)
        {
            // Still on pace in next lap, keep hot lap mode
        }
        else
        {
            _isOnHotLap = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE METHODS
    // ═══════════════════════════════════════════════════════════════

    private void ProcessHotLapCoaching(EliteCoachingContext context)
    {
        var sample = context.CurrentSample;
        float distPct = sample.LapDistPct;

        // First sector (0-0.33): early encouragement if on pace
        if (distPct < 0.33f)
        {
            if (Math.Abs(context.CurrentDelta) > -0.2f)
            {
                // Still ahead of pace at 1/3 point
                SubmitMinimalDecision(context, "confidence",
                    "Good lap. Stay calm.", CoachingPriority.Minimal, 70, 0);
            }
        }
        // Second sector (0.33-0.66): reinforcement
        else if (distPct >= 0.33f && distPct < 0.66f)
        {
            if (Math.Abs(context.CurrentDelta) > -0.2f)
            {
                SubmitMinimalDecision(context, "confidence",
                    "On pace. Don't overdrive.", CoachingPriority.Minimal, 70, 0);
            }
        }
        // Final sector (0.66-0.85): commitment reminder
        else if (distPct >= 0.66f && distPct < 0.85f)
        {
            if (context.NextCorner != null && Math.Abs(context.CurrentDelta) > -0.15f)
            {
                SubmitMinimalDecision(context, "confidence",
                    "Commit here.", CoachingPriority.Minimal, 65, 0);
            }
        }
        // Final stretch (0.85+): finish cue
        else if (distPct >= 0.85f)
        {
            SubmitMinimalDecision(context, "confidence",
                "Finish the lap. Clean.", CoachingPriority.Minimal, 65, 0);
        }
    }

    private void SubmitMinimalDecision(EliteCoachingContext context, string category, string voiceText,
        CoachingPriority priority, int confidence, float speakBeforeSeconds)
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
            Message = voiceText,
            VoiceText = voiceText,
            Priority = priority,
            ConfidenceScore = confidence,
            SpeakBeforeCornerSeconds = speakBeforeSeconds,
            SessionId = context.SessionId,
        };

        _priority.Submit(decision);
    }
}
