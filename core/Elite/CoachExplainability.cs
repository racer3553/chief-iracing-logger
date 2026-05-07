// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Coach Explainability
// Post-session review and performance analysis.
// NOT an ICoachingModule — utility for session reporting.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Input quality metrics for a session.
/// </summary>
public class InputQualityScores
{
    /// <summary>
    /// Average braking consistency (0-1, higher is better).
    /// </summary>
    public float BrakingConsistency { get; set; }

    /// <summary>
    /// Average throttle smoothness (0-1, higher is better).
    /// </summary>
    public float ThrottleSmoothness { get; set; }

    /// <summary>
    /// Average steering precision (0-1, higher is better).
    /// </summary>
    public float SteeringPrecision { get; set; }

    /// <summary>
    /// Overall line quality (0-1, higher is better).
    /// </summary>
    public float LineQuality { get; set; }

    /// <summary>
    /// Overall consistency across laps (0-1, higher is better).
    /// </summary>
    public float LapConsistency { get; set; }

    public float Average() => (BrakingConsistency + ThrottleSmoothness + SteeringPrecision + LineQuality + LapConsistency) / 5f;
}

/// <summary>
/// Explanation for a single coaching decision and its outcome.
/// </summary>
public class DecisionExplanation
{
    /// <summary>
    /// The original coaching decision.
    /// </summary>
    public CoachingDecision Decision { get; set; } = new();

    /// <summary>
    /// Why this instruction was given.
    /// </summary>
    public string WhySaid { get; set; } = "";

    /// <summary>
    /// What actually happened after instruction.
    /// </summary>
    public string WhatHappened { get; set; } = "";

    /// <summary>
    /// Whether the instruction helped.
    /// </summary>
    public bool DidItWork { get; set; }

    /// <summary>
    /// Recommendation for next session.
    /// </summary>
    public string NextRecommendation { get; set; } = "";
}

/// <summary>
/// Complete session explanation and performance summary.
/// </summary>
public class SessionExplanation
{
    /// <summary>
    /// Session ID this explanation covers.
    /// </summary>
    public string SessionId { get; set; } = "";

    /// <summary>
    /// All decisions with explanations.
    /// </summary>
    public List<DecisionExplanation> Decisions { get; set; } = new();

    /// <summary>
    /// Text summary of session performance.
    /// </summary>
    public string OverallSummary { get; set; } = "";

    /// <summary>
    /// What to focus on next session.
    /// </summary>
    public string NextSessionFocus { get; set; } = "";

    /// <summary>
    /// Input quality breakdown by category.
    /// </summary>
    public InputQualityScores InputScores { get; set; } = new();

    /// <summary>
    /// Time delta loss per corner (cumulative).
    /// </summary>
    public Dictionary<string, float> CornerDeltaMap { get; set; } = new();

    /// <summary>
    /// Total decisions made (spoken + suppressed).
    /// </summary>
    public int TotalDecisions { get; set; }

    /// <summary>
    /// Number of decisions that were spoken.
    /// </summary>
    public int SpokenDecisions { get; set; }

    /// <summary>
    /// Number of decisions suppressed by gating.
    /// </summary>
    public int SuppressedDecisions { get; set; }

    /// <summary>
    /// Number of instructions where driver improved after coaching.
    /// </summary>
    public int ImprovedAfterCoaching { get; set; }

    /// <summary>
    /// Number of instructions where driver worsened after coaching.
    /// </summary>
    public int WorsenedAfterCoaching { get; set; }

    /// <summary>
    /// Percentage of coaching that led to improvement (0-100).
    /// </summary>
    public float CoachingEffectiveness { get; set; }
}

/// <summary>
/// Coach explainability system. Generates post-session reviews
/// that explain coaching decisions and their outcomes.
/// </summary>
public class CoachExplainability
{
    private readonly ChiefDatabase _db;
    private readonly CoachingMemory _memory;

    public CoachExplainability(ChiefDatabase db, CoachingMemory memory)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>
    /// Generate a complete session explanation from decisions and memory.
    /// </summary>
    public SessionExplanation GenerateSessionExplanation(string sessionId, List<CoachingDecision> decisions)
    {
        if (decisions == null || decisions.Count == 0)
        {
            return new SessionExplanation
            {
                SessionId = sessionId,
                OverallSummary = "No coaching decisions recorded.",
                CoachingEffectiveness = 0f,
            };
        }

        var explanation = new SessionExplanation
        {
            SessionId = sessionId,
            TotalDecisions = decisions.Count,
            SpokenDecisions = decisions.Count(d => d.WasSpoken),
            SuppressedDecisions = decisions.Count(d => d.WasSuppressed),
        };

        // Build decision explanations
        foreach (var decision in decisions.OrderBy(d => d.TimestampMs))
        {
            var decExplain = new DecisionExplanation
            {
                Decision = decision,
                WhySaid = GenerateWhySaid(decision),
                WhatHappened = GenerateWhatHappened(decision),
                DidItWork = decision.DriverResponded,
                NextRecommendation = GenerateNextRecommendation(decision),
            };

            explanation.Decisions.Add(decExplain);

            if (decision.DriverResponded && decision.ActualDeltaChange < 0)
                explanation.ImprovedAfterCoaching++;
            else if (decision.ActualDeltaChange > 0)
                explanation.WorsenedAfterCoaching++;
        }

        // Calculate effectiveness
        int totalWithOutcome = explanation.ImprovedAfterCoaching + explanation.WorsenedAfterCoaching;
        if (totalWithOutcome > 0)
        {
            explanation.CoachingEffectiveness = (explanation.ImprovedAfterCoaching / (float)totalWithOutcome) * 100f;
        }

        // Build corner delta map
        explanation.CornerDeltaMap = decisions
            .GroupBy(d => d.CornerName)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(d => d.ActualDeltaChange)
            );

        // Generate summary text
        explanation.OverallSummary = GenerateTextSummary(explanation);
        explanation.NextSessionFocus = GenerateNextSessionFocus(explanation);

        return explanation;
    }

    /// <summary>
    /// Generate a human-readable text summary of the session.
    /// </summary>
    public string GenerateTextSummary(SessionExplanation explanation)
    {
        var sb = new StringBuilder();
        int lapCount = explanation.Decisions.GroupBy(d => d.Decision.LapNumber).Count();
        string car = explanation.Decisions.FirstOrDefault()?.Decision.Car ?? "Unknown";
        string track = explanation.Decisions.FirstOrDefault()?.Decision.Track ?? "Unknown";

        sb.AppendLine($"Session: {lapCount} laps, {car} at {track}.");
        sb.AppendLine($"Chief gave {explanation.TotalDecisions} coaching calls ({explanation.SpokenDecisions} spoken, {explanation.SuppressedDecisions} suppressed).");

        // Find biggest loss corner
        if (explanation.CornerDeltaMap.Count > 0)
        {
            var worstCorner = explanation.CornerDeltaMap.OrderByDescending(kvp => kvp.Value).First();
            sb.AppendLine($"{worstCorner.Key} was your biggest loss ({worstCorner.Value:F1}s avg delta).");
        }

        if (explanation.ImprovedAfterCoaching > 0)
        {
            sb.AppendLine($"After coaching, {explanation.ImprovedAfterCoaching} corners showed improvement.");
        }

        sb.AppendLine($"Overall coaching effectiveness: {explanation.CoachingEffectiveness:F0}%.");

        return sb.ToString();
    }

    /// <summary>
    /// Generate recommendation for next session focus.
    /// </summary>
    public string GenerateNextSessionFocus(SessionExplanation explanation)
    {
        // Find the corner that would give most time back
        if (explanation.CornerDeltaMap.Count == 0)
            return "No specific focus needed.";

        var worstCorners = explanation.CornerDeltaMap
            .OrderByDescending(kvp => kvp.Value)
            .Take(2)
            .ToList();

        if (worstCorners.Count == 0)
            return "Continue working on overall consistency.";

        var sb = new StringBuilder();
        sb.Append("Next session: focus on ");
        sb.Append(string.Join(" and ", worstCorners.Select(kvp => $"{kvp.Key}")));
        sb.Append(".");

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private string GenerateWhySaid(CoachingDecision decision)
    {
        return decision.Category switch
        {
            "braking" => "Detected inconsistent or late braking into this corner.",
            "throttle" => "Detected wheelspin or poor throttle management on exit.",
            "steering" => "Detected abrupt steering inputs or over-correction.",
            "line" => "Detected suboptimal corner line affecting exit speed.",
            "tire_management" => "Detected tire degradation or abuse pattern.",
            "racecraft" => "Detected strategic or positioning opportunity.",
            "mental" => "Detected signs of driver fatigue or frustration.",
            "confidence" => "Reinforcing pace to maintain momentum.",
            "testing" => "Guiding structured A/B testing.",
            _ => "Coaching to improve performance."
        };
    }

    private string GenerateWhatHappened(CoachingDecision decision)
    {
        if (decision.ActualDeltaChange < -0.1f)
            return $"Driver improved by {Math.Abs(decision.ActualDeltaChange):F2}s at this corner.";
        else if (decision.ActualDeltaChange > 0.1f)
            return $"Driver lost {decision.ActualDeltaChange:F2}s at this corner.";
        else
            return "No significant change after instruction.";
    }

    private string GenerateNextRecommendation(CoachingDecision decision)
    {
        if (decision.DriverResponded && decision.ActualDeltaChange < -0.1f)
            return "Continue using this coaching approach.";
        else if (decision.ActualDeltaChange > 0.1f)
            return "Try a different coaching angle or timing.";
        else
            return "Monitor this area in next session.";
    }
}
