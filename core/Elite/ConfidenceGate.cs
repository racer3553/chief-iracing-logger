// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Confidence Gate
// Filters coaching decisions based on confidence thresholds per
// category. Low-confidence decisions are logged for post-session
// review rather than spoken.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public class ConfidenceGate
{
    private readonly object _lock = new();
    private ConfidenceThresholds _thresholds;
    private List<CoachingDecision> _suppressedForPostSession = new();

    public ConfidenceGate(ConfidenceThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? new ConfidenceThresholds();
    }

    /// <summary>
    /// Check if a coaching decision meets the confidence threshold for its category.
    /// </summary>
    public bool PassesGate(CoachingDecision decision)
    {
        if (decision == null)
            return false;

        lock (_lock)
        {
            var requiredConfidence = GetThresholdForCategory(decision.Category);
            return decision.ConfidenceScore >= requiredConfidence;
        }
    }

    /// <summary>
    /// Evaluate a decision in detail, including threshold and actual confidence.
    /// </summary>
    public GateResult Evaluate(CoachingDecision decision)
    {
        if (decision == null)
        {
            return new GateResult(
                Passed: false,
                RequiredConfidence: 0,
                ActualConfidence: 0,
                Category: "unknown",
                DecisionId: ""
            );
        }

        lock (_lock)
        {
            var required = GetThresholdForCategory(decision.Category);
            var passed = decision.ConfidenceScore >= required;

            if (!passed)
            {
                // Add to suppressed list for post-session review
                _suppressedForPostSession.Add(decision);
            }

            return new GateResult(
                Passed: passed,
                RequiredConfidence: required,
                ActualConfidence: decision.ConfidenceScore,
                Category: decision.Category,
                DecisionId: decision.Id
            );
        }
    }

    /// <summary>
    /// Get all decisions that were suppressed for low confidence but could
    /// be useful for post-session review and improvement.
    /// </summary>
    public List<CoachingDecision> GetPostSessionNotes()
    {
        lock (_lock)
        {
            return new List<CoachingDecision>(_suppressedForPostSession);
        }
    }

    /// <summary>
    /// Clear the suppressed list (e.g., after session ends and notes are reviewed).
    /// </summary>
    public void ClearPostSessionNotes()
    {
        lock (_lock)
        {
            _suppressedForPostSession.Clear();
        }
    }

    /// <summary>
    /// Update confidence thresholds.
    /// </summary>
    public void SetThresholds(ConfidenceThresholds thresholds)
    {
        lock (_lock)
        {
            _thresholds = thresholds ?? new ConfidenceThresholds();
        }
    }

    /// <summary>
    /// Get current thresholds.
    /// </summary>
    public ConfidenceThresholds GetThresholds()
    {
        lock (_lock)
        {
            return _thresholds;
        }
    }

    /// <summary>
    /// Get the threshold for a specific category.
    /// </summary>
    private int GetThresholdForCategory(string category)
    {
        return category?.ToLower() switch
        {
            "braking" => _thresholds.PredictiveCorner,
            "throttle" => _thresholds.PredictiveCorner,
            "steering" => _thresholds.PredictiveCorner,
            "line" => _thresholds.PredictiveCorner,
            "corner" => _thresholds.PredictiveCorner,
            "setup" => _thresholds.SetupHardware,
            "hardware" => _thresholds.SetupHardware,
            "tire" => _thresholds.TireStrategy,
            "fuel" => _thresholds.TireStrategy,
            "strategy" => _thresholds.TireStrategy,
            "racecraft" => _thresholds.Racecraft,
            "defense" => _thresholds.Racecraft,
            "pass" => _thresholds.Racecraft,
            "mental" => _thresholds.MentalReset,
            "focus" => _thresholds.MentalReset,
            "reset" => _thresholds.MentalReset,
            "praise" => _thresholds.Praise,
            "motivation" => _thresholds.Praise,
            "onelapfix" => _thresholds.OneLapFix,
            "one_lap_fix" => _thresholds.OneLapFix,
            _ => 70 // Default threshold for unknown categories
        };
    }

    /// <summary>
    /// Get count of suppressed decisions waiting for post-session review.
    /// </summary>
    public int SuppressedCount
    {
        get
        {
            lock (_lock)
            {
                return _suppressedForPostSession.Count;
            }
        }
    }
}

/// <summary>
/// Configurable confidence thresholds for each coaching category.
/// Thresholds are percentages (0-100).
/// </summary>
public class ConfidenceThresholds
{
    /// <summary>
    /// Critical safety decisions (always speak, no threshold).
    /// </summary>
    public int CriticalSafety { get; set; } = 0;

    /// <summary>
    /// Racecraft: defend, pass, strategy (default 65%).
    /// </summary>
    public int Racecraft { get; set; } = 65;

    /// <summary>
    /// Predictive corner instruction: braking, turn-in, apex (default 70%).
    /// </summary>
    public int PredictiveCorner { get; set; } = 70;

    /// <summary>
    /// One-lap-fix suggestions (default 70%).
    /// </summary>
    public int OneLapFix { get; set; } = 70;

    /// <summary>
    /// Tire and fuel strategy (default 60%).
    /// </summary>
    public int TireStrategy { get; set; } = 60;

    /// <summary>
    /// Setup and hardware tuning recommendations (default 80%).
    /// </summary>
    public int SetupHardware { get; set; } = 80;

    /// <summary>
    /// Mental reset and focus cues (default 75%).
    /// </summary>
    public int MentalReset { get; set; } = 75;

    /// <summary>
    /// Praise and motivation (default 50%).
    /// </summary>
    public int Praise { get; set; } = 50;
}

/// <summary>
/// Result of a confidence gate evaluation.
/// </summary>
public record GateResult(
    bool Passed,
    int RequiredConfidence,
    int ActualConfidence,
    string Category,
    string DecisionId = ""
);
