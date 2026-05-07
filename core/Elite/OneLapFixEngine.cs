// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — One-Lap Fix Engine
// Identifies the SINGLE biggest time gain opportunity every lap.
// Provides focused, strategic end-of-lap coaching on lap completion.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

public class OneLapFixEngine : ICoachingModule
{
    private readonly CoachingPrioritySystem _priority;
    private readonly ICornerPerformanceProvider _perfProvider;
    private readonly TrackMapService _maps;

    /// <summary>The current one-lap-fix focus area (for UI exposure)</summary>
    public string CurrentFocus { get; private set; } = "";

    /// <summary>How much time the focus corner is costing</summary>
    public float CurrentFocusDeltaLoss { get; private set; } = 0f;

    public string ModuleName => "One-Lap Fix Engine";
    public bool IsEnabled { get; set; } = true;

    public OneLapFixEngine(
        CoachingPrioritySystem priority,
        ICornerPerformanceProvider perfProvider,
        TrackMapService maps)
    {
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _perfProvider = perfProvider ?? throw new ArgumentNullException(nameof(perfProvider));
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
    }

    public void ProcessTick(EliteCoachingContext context)
    {
        // One-lap fix runs only on lap complete, not every tick
    }

    public void OnLapCompleted(EliteCoachingContext context, int lap)
    {
        if (context?.CurrentSample == null)
            return;

        // Need at least one lap of data
        if (lap < 1)
            return;

        // Analyze all corners from this lap
        var corners = _maps.GetAllCorners();
        if (corners == null || corners.Count == 0)
            return;

        float maxDeltaLoss = 0f;
        string worstCornerId = "";
        CornerPerformance? worstCornerPerf = null;
        CornerPerformance? worstCornerBest = null;

        // Find the corner with largest delta loss
        foreach (var corner in corners)
        {
            var perf = _perfProvider.GetLastLapCorner(corner.Id);
            var best = _perfProvider.GetBestLapCorner(corner.Id);

            if (perf == null || best == null)
                continue;

            // Delta loss = time lost at this corner vs best
            float deltaLoss = Math.Abs(perf.DeltaGainedLost);

            if (deltaLoss > maxDeltaLoss)
            {
                maxDeltaLoss = deltaLoss;
                worstCornerId = corner.Id;
                worstCornerPerf = perf;
                worstCornerBest = best;
            }
        }

        // If we found the worst corner, generate instruction
        if (!string.IsNullOrEmpty(worstCornerId) && worstCornerPerf != null && worstCornerBest != null)
        {
            CurrentFocusDeltaLoss = maxDeltaLoss;

            var analysis = AnalyzeMainIssue(worstCornerPerf, worstCornerBest);

            if (!string.IsNullOrEmpty(analysis.Message))
            {
                CurrentFocus = analysis.Focus;

                var decision = new CoachingDecision
                {
                    Car = context.Car,
                    Track = context.Track,
                    CornerName = worstCornerPerf.CornerId,
                    LapNumber = context.CurrentLap,
                    LapDistPct = 0f,
                    SessionType = context.SessionType,
                    Category = "strategy",
                    Message = analysis.Message,
                    VoiceText = analysis.VoiceText,
                    Priority = CoachingPriority.Low, // Strategic advice, not urgent
                    ConfidenceScore = analysis.Confidence,
                    SpeakBeforeCornerSeconds = 0f, // End-of-lap, speak immediately
                    SessionId = context.SessionId,
                    RecommendedActionType = "driver",
                    ExpectedDeltaGain = maxDeltaLoss
                };

                _priority.Submit(decision);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ISSUE ANALYSIS
    // ═══════════════════════════════════════════════════════════════

    private class MainIssueAnalysis
    {
        public string Focus { get; set; } = "";
        public string Message { get; set; } = "";
        public string VoiceText { get; set; } = "";
        public int Confidence { get; set; } = 75;
    }

    private MainIssueAnalysis AnalyzeMainIssue(CornerPerformance perf, CornerPerformance best)
    {
        var analysis = new MainIssueAnalysis();

        // Priority 1: Exit speed loss (most impactful for lap time)
        float exitLoss = best.ExitSpeed - perf.ExitSpeed;
        if (exitLoss > 1.5f)
        {
            analysis.Focus = $"{perf.CornerId} (exit speed)";
            analysis.Message = $"Next lap, fix {perf.CornerId} exit. You're losing {exitLoss:F1} mph.";
            analysis.VoiceText = $"Next lap, fix {perf.CornerId} exit. Pick up throttle sooner.";
            analysis.Confidence = 85;
            return analysis;
        }

        // Priority 2: Brake point inconsistency (entry speed)
        float brakePointDiff = Math.Abs(perf.BrakeStartDistPct - best.BrakeStartDistPct);
        if (brakePointDiff > 1.0f)
        {
            analysis.Focus = $"{perf.CornerId} (brake point)";
            analysis.Message = $"{perf.CornerId} brake point is inconsistent. Use the board every lap.";
            analysis.VoiceText = $"Use the brake board at {perf.CornerId}. Consistent marker.";
            analysis.Confidence = 78;
            return analysis;
        }

        // Priority 3: Apex speed difference
        float apexLoss = best.ApexSpeed - perf.ApexSpeed;
        if (apexLoss > 2.0f)
        {
            analysis.Focus = $"{perf.CornerId} (apex speed)";
            analysis.Message = $"Your biggest gain is tighter to the apex at {perf.CornerId}.";
            analysis.VoiceText = $"Turn in later at {perf.CornerId}. Tighter apex.";
            analysis.Confidence = 75;
            return analysis;
        }

        // Priority 4: Throttle pickup delay
        float throttleDelay = perf.ThrottlePickupDistPct - best.ThrottlePickupDistPct;
        if (throttleDelay > 0.5f)
        {
            analysis.Focus = $"{perf.CornerId} (throttle pickup)";
            analysis.Message = $"Stop chasing entry speed. Focus drive off at {perf.CornerId}.";
            analysis.VoiceText = $"Give up entry. Focus drive off {perf.CornerId}.";
            analysis.Confidence = 72;
            return analysis;
        }

        // Priority 5: Steering correction count (overdriving)
        int correctionDiff = perf.SteeringCorrectionCount - best.SteeringCorrectionCount;
        if (correctionDiff > 2)
        {
            analysis.Focus = $"{perf.CornerId} (overdriving)";
            analysis.Message = $"You're overdriving {perf.CornerId} entry. Calm inputs, trust the car.";
            analysis.VoiceText = $"Calm inputs at {perf.CornerId}. Trust the car.";
            analysis.Confidence = 70;
            return analysis;
        }

        // No major issue detected (this shouldn't happen in OnLapCompleted context)
        return analysis;
    }
}
