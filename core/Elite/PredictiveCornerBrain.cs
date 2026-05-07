// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Predictive Corner Brain
// Coaches driver BEFORE the commitment zone. The crown jewel module.
// Delivers time-critical coaching at optimal moments with dynamic
// adjustments based on lap history and mistake patterns.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

public class PredictiveCornerBrain : ICoachingModule
{
    private readonly CoachingPrioritySystem _priority;
    private readonly TrackMapService _maps;
    private readonly ICornerPerformanceProvider _perfProvider;
    private readonly CoachingLearner _learner;

    private readonly HashSet<int> _calledCornersThisLap = new();
    private int _lastLapNumber = -1;

    public string ModuleName => "Predictive Corner Brain";
    public bool IsEnabled { get; set; } = true;

    public PredictiveCornerBrain(
        CoachingPrioritySystem priority,
        TrackMapService maps,
        ICornerPerformanceProvider perfProvider,
        CoachingLearner learner)
    {
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        _perfProvider = perfProvider ?? throw new ArgumentNullException(nameof(perfProvider));
        _learner = learner ?? throw new ArgumentNullException(nameof(learner));
    }

    public void ProcessTick(EliteCoachingContext context)
    {
        if (context?.NextCorner == null || context.CurrentSample == null)
            return;

        // Reset called corners on new lap
        if (context.CurrentLap != _lastLapNumber)
        {
            _calledCornersThisLap.Clear();
            _lastLapNumber = context.CurrentLap;
        }

        var nextCorner = context.NextCorner;
        var currentDist = context.CurrentSample.LapDistPct;
        var currentSpeed = context.CurrentSample.Speed;

        // Calculate time to brake zone
        float distToBrake = nextCorner.BrakeZoneDistPct - currentDist;
        if (distToBrake < 0)
            distToBrake += 100; // Wrap to next lap

        // Rough estimate: average 120 seconds per 100% lap at 100 mph
        // Adjust based on current speed: 120 * (currentSpeed / 100)
        float secondsPerDistPct = Math.Max(0.5f, 120f / 100f * (currentSpeed / 100f));
        float timeToCorner = distToBrake * secondsPerDistPct;

        // Skip if already called this corner or if it's far away
        if (_calledCornersThisLap.Contains(nextCorner.CornerNumber) || timeToCorner > 10)
            return;

        // ═══════════════════════════════════════════════════════════════
        // 6 SECONDS: Main instruction
        // ═══════════════════════════════════════════════════════════════
        if (timeToCorner is >= 5.5f and < 6.5f)
        {
            GenerateMainInstruction(context, nextCorner);
            return;
        }

        // ═══════════════════════════════════════════════════════════════
        // 3 SECONDS: Short reminder if needed
        // ═══════════════════════════════════════════════════════════════
        if (timeToCorner is >= 2.5f and < 3.5f)
        {
            GenerateReminder(context, nextCorner);
            return;
        }

        // ═══════════════════════════════════════════════════════════════
        // 1 SECOND: Brake marker call only if enabled
        // ═══════════════════════════════════════════════════════════════
        if (timeToCorner is >= 0.5f and < 1.5f)
        {
            GenerateBrakeMarker(context, nextCorner);
            return;
        }
    }

    public void OnLapCompleted(EliteCoachingContext context, int lap)
    {
        // Cleanup happens on ProcessTick when lap number changes
    }

    // ═══════════════════════════════════════════════════════════════
    // MAIN INSTRUCTION (6s window)
    // ═══════════════════════════════════════════════════════════════

    private void GenerateMainInstruction(EliteCoachingContext context, TrackCorner corner)
    {
        if (_calledCornersThisLap.Contains(corner.CornerNumber))
            return;

        var lastLapPerf = _perfProvider.GetLastLapCorner(corner.Id);
        var bestLapPerf = _perfProvider.GetBestLapCorner(corner.Id);

        string message = "";
        string voiceText = "";
        int confidence = 70;
        string category = "braking";

        // Analyze what went wrong last lap
        if (lastLapPerf != null && bestLapPerf != null)
        {
            // Detect the biggest issue from last lap
            var issue = AnalyzeCornerIssue(corner, lastLapPerf, bestLapPerf);

            if (!string.IsNullOrEmpty(issue.Problem))
            {
                message = issue.Message;
                voiceText = issue.VoiceText;
                category = issue.Category;
                confidence = issue.Confidence;
            }
            else
            {
                // No specific mistake detected
                GenerateGenericCornerPrep(corner, out message, out voiceText, out category);
            }
        }
        else
        {
            // No performance data yet
            GenerateGenericCornerPrep(corner, out message, out voiceText, out category);
            confidence -= 20; // Lower confidence with no track map
        }

        // Clamp confidence and create decision
        confidence = Math.Clamp(confidence, 0, 100);

        var decision = new CoachingDecision
        {
            Car = context.Car,
            Track = context.Track,
            CornerName = corner.CornerName,
            CornerNumber = corner.CornerNumber,
            LapNumber = context.CurrentLap,
            LapDistPct = context.CurrentSample.LapDistPct,
            SessionType = context.SessionType,
            Category = category,
            Message = message,
            VoiceText = voiceText,
            Priority = CoachingPriority.Medium,
            ConfidenceScore = confidence,
            SpeakBeforeCornerSeconds = 6.0f,
            SessionId = context.SessionId,
            RecommendedActionType = "driver",
            ExpectedDeltaGain = 0.1f // Conservative estimate
        };

        _priority.Submit(decision);
        _calledCornersThisLap.Add(corner.CornerNumber);
    }

    // ═══════════════════════════════════════════════════════════════
    // REMINDER (3s window) — only if last lap had mistake here
    // ═══════════════════════════════════════════════════════════════

    private void GenerateReminder(EliteCoachingContext context, TrackCorner corner)
    {
        if (_calledCornersThisLap.Contains(corner.CornerNumber))
            return;

        var lastLapPerf = _perfProvider.GetLastLapCorner(corner.Id);

        // Only remind if there was a mistake last lap
        if (lastLapPerf == null || string.IsNullOrEmpty(lastLapPerf.MistakeCategory))
            return;

        // Get the issue again to create reminder
        var bestLapPerf = _perfProvider.GetBestLapCorner(corner.Id);
        var issue = AnalyzeCornerIssue(corner, lastLapPerf, bestLapPerf ?? lastLapPerf);

        if (string.IsNullOrEmpty(issue.VoiceText))
            return; // No reminder needed

        // Make reminder shorter (max 5 words)
        string reminderVoice = issue.VoiceText.Length > 25 ? "Patience on throttle." : issue.VoiceText;

        var decision = new CoachingDecision
        {
            Car = context.Car,
            Track = context.Track,
            CornerName = corner.CornerName,
            CornerNumber = corner.CornerNumber,
            LapNumber = context.CurrentLap,
            LapDistPct = context.CurrentSample.LapDistPct,
            SessionType = context.SessionType,
            Category = issue.Category,
            Message = "Reminder from last lap",
            VoiceText = reminderVoice,
            Priority = CoachingPriority.Low,
            ConfidenceScore = 60,
            SpeakBeforeCornerSeconds = 3.0f,
            SessionId = context.SessionId,
            RecommendedActionType = "driver"
        };

        _priority.Submit(decision);
    }

    // ═══════════════════════════════════════════════════════════════
    // BRAKE MARKER (1s window) — only if enabled
    // ═══════════════════════════════════════════════════════════════

    private void GenerateBrakeMarker(EliteCoachingContext context, TrackCorner corner)
    {
        if (_calledCornersThisLap.Contains(corner.CornerNumber))
            return;

        if (string.IsNullOrEmpty(corner.BrakeMarker))
            return; // No brake marker defined

        var decision = new CoachingDecision
        {
            Car = context.Car,
            Track = context.Track,
            CornerName = corner.CornerName,
            CornerNumber = corner.CornerNumber,
            LapNumber = context.CurrentLap,
            LapDistPct = context.CurrentSample.LapDistPct,
            SessionType = context.SessionType,
            Category = "braking",
            Message = $"Brake marker at {corner.BrakeMarker}",
            VoiceText = $"Brake at the {corner.BrakeMarker}",
            Priority = CoachingPriority.Minimal,
            ConfidenceScore = 85,
            SpeakBeforeCornerSeconds = 1.0f,
            SessionId = context.SessionId,
            RecommendedActionType = "driver"
        };

        _priority.Submit(decision);
    }

    // ═══════════════════════════════════════════════════════════════
    // ISSUE ANALYSIS
    // ═══════════════════════════════════════════════════════════════

    private class CornerIssue
    {
        public string Problem { get; set; } = "";
        public string Message { get; set; } = "";
        public string VoiceText { get; set; } = "";
        public string Category { get; set; } = "braking";
        public int Confidence { get; set; } = 70;
    }

    private CornerIssue AnalyzeCornerIssue(TrackCorner corner, CornerPerformance lastLap, CornerPerformance bestLap)
    {
        var issue = new CornerIssue();

        // Exit speed loss is most impactful for lap time
        if (lastLap.ExitSpeed < bestLap.ExitSpeed - 2)
        {
            float speedLoss = bestLap.ExitSpeed - lastLap.ExitSpeed;
            issue.Problem = "poor_exit";
            issue.Message = $"Exit speed loss of {speedLoss:F1} mph last lap";
            issue.VoiceText = "Pick up throttle sooner.";
            issue.Category = "throttle";
            issue.Confidence = 80;
            return issue;
        }

        // Brake point inconsistency
        if (Math.Abs(lastLap.BrakeStartDistPct - bestLap.BrakeStartDistPct) > 0.5f)
        {
            issue.Problem = "inconsistent_braking";
            issue.Message = "Brake point is inconsistent. Use visual markers.";
            issue.VoiceText = "Use the brake board every lap.";
            issue.Category = "braking";
            issue.Confidence = 75;
            return issue;
        }

        // Apex speed difference
        if (lastLap.ApexSpeed < bestLap.ApexSpeed - 1)
        {
            float apexLoss = bestLap.ApexSpeed - lastLap.ApexSpeed;
            issue.Problem = "missed_apex";
            issue.Message = $"Apex speed loss of {apexLoss:F1} mph";
            issue.VoiceText = "Turn in later, tighter apex.";
            issue.Category = "steering";
            issue.Confidence = 70;
            return issue;
        }

        // Throttle pickup delay
        if (lastLap.ThrottlePickupDistPct > bestLap.ThrottlePickupDistPct + 0.2f)
        {
            issue.Problem = "poor_exit";
            issue.Message = "Throttle pickup is too late";
            issue.VoiceText = "Pick up throttle sooner.";
            issue.Category = "throttle";
            issue.Confidence = 72;
            return issue;
        }

        // Steering correction count (overdriving)
        if (lastLap.SteeringCorrectionCount > bestLap.SteeringCorrectionCount + 2)
        {
            issue.Problem = "snap_oversteer";
            issue.Message = "Overdriving entry with steering corrections";
            issue.VoiceText = "Calm inputs, trust the car.";
            issue.Category = "steering";
            issue.Confidence = 68;
            return issue;
        }

        // No specific issue detected
        return issue;
    }

    // ═══════════════════════════════════════════════════════════════
    // GENERIC CORNER PREP
    // ═══════════════════════════════════════════════════════════════

    private void GenerateGenericCornerPrep(TrackCorner corner, out string message, out string voice, out string category)
    {
        category = "braking";
        message = $"Approaching {corner.CornerName}";
        voice = $"{corner.CornerName}. Focus.";

        if (!string.IsNullOrEmpty(corner.DefaultVoiceCall))
        {
            voice = corner.DefaultVoiceCall;
        }
    }
}
