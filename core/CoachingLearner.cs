// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Coaching Learner
// Learns what coaching instructions work for this driver.
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

/// <summary>
/// A coaching instruction with confidence score and effectiveness metrics.
/// </summary>
public class CoachingInstruction
{
    public string Id { get; set; } = "";
    public string CornerId { get; set; } = "";
    public string MistakeCategory { get; set; } = "";
    public string InstructionText { get; set; } = "";
    public float ConfidenceScore { get; set; } = 0.5f;
    public int TimesGiven { get; set; }
    public int TimesImproved { get; set; }
    public int TimesWorsened { get; set; }
    public int TimesNoChange { get; set; }
}

/// <summary>
/// Records the outcome of a coaching instruction.
/// </summary>
public class CoachingOutcome
{
    public string InstructionId { get; set; } = "";
    public string CornerId { get; set; } = "";
    public int LapGiven { get; set; }
    public int LapResult { get; set; }
    public float DeltaBefore { get; set; }
    public float DeltaAfter { get; set; }
    public string Result { get; set; } = ""; // improved, worsened, no_change
}

/// <summary>
/// Learns which coaching instructions work for this driver by tracking
/// outcomes and updating confidence scores.
/// </summary>
public class CoachingLearner
{
    private readonly ChiefDatabase _db;
    private readonly Dictionary<string, CoachingInstruction> _instructions = new();

    // Default instructions per mistake category
    private static readonly Dictionary<string, List<string>> DefaultInstructions = new()
    {
        ["braked_late"] = new()
        {
            "Brake earlier this lap",
            "Brake half a car length sooner",
            "Use the brake board as your marker"
        },
        ["braked_early"] = new()
        {
            "Later on the brakes",
            "Carry it deeper into the corner",
            "Trust the brake zone"
        },
        ["over_slowed"] = new()
        {
            "Release the brake sooner",
            "Carry more speed through",
            "Don't over-slow it"
        },
        ["snap_oversteer"] = new()
        {
            "Wait on throttle until wheel opens",
            "Slower hands on exit",
            "Unwind steering before throttle"
        },
        ["missed_apex"] = new()
        {
            "Turn in later",
            "Aim tighter to the apex",
            "Sacrifice entry speed for apex"
        },
        ["poor_exit"] = new()
        {
            "Focus on the drive off",
            "Give up entry speed for exit",
            "Patient on throttle"
        },
        ["lockup"] = new()
        {
            "Ease brake pressure",
            "Trail brake smoother",
            "Less initial stab on the brakes"
        }
    };

    public CoachingLearner(ChiefDatabase db)
    {
        _db = db;
        LoadFromDatabase();
    }

    /// <summary>
    /// Load instruction bank from database and initialize defaults.
    /// </summary>
    private void LoadFromDatabase()
    {
        // Load from database
        var dbInstructions = _db.GetAllCoachingInstructions();
        foreach (var instr in dbInstructions)
        {
            _instructions[instr.Id] = instr;
        }

        // Ensure all default instructions exist
        foreach (var (mistakeCategory, texts) in DefaultInstructions)
        {
            foreach (var text in texts)
            {
                // Create a key that represents corner_category_text
                var key = $"default_{mistakeCategory}_{text}";
                if (!_instructions.ContainsKey(key))
                {
                    var instr = new CoachingInstruction
                    {
                        Id = key,
                        CornerId = "all", // Default applies to all corners
                        MistakeCategory = mistakeCategory,
                        InstructionText = text,
                        ConfidenceScore = 0.5f
                    };
                    _instructions[key] = instr;
                }
            }
        }
    }

    /// <summary>
    /// Record that an instruction was given to the driver.
    /// </summary>
    public void RecordInstruction(string cornerId, string mistakeCategory,
        string instructionText, int lapNumber)
    {
        // Find or create instruction
        var matchingInstructions = _instructions.Values
            .Where(i => i.CornerId == cornerId || i.CornerId == "all")
            .Where(i => i.MistakeCategory == mistakeCategory)
            .Where(i => i.InstructionText == instructionText)
            .ToList();

        if (matchingInstructions.Count == 0)
        {
            // Create new instruction with moderate confidence
            var id = Guid.NewGuid().ToString();
            var instr = new CoachingInstruction
            {
                Id = id,
                CornerId = cornerId,
                MistakeCategory = mistakeCategory,
                InstructionText = instructionText,
                ConfidenceScore = 0.5f
            };
            _instructions[id] = instr;
            _db.InsertCoachingInstruction(instr);
        }
        else
        {
            // Update existing instruction
            var instr = matchingInstructions.First();
            instr.TimesGiven++;
            _db.UpdateCoachingInstructionGiven(instr.Id);
        }
    }

    /// <summary>
    /// Evaluate the outcome of an instruction by comparing corner performance
    /// before and after the instruction was given.
    /// </summary>
    public void EvaluateOutcome(string cornerId, int currentLap, int lapInstructionWasGiven,
        CornerPerformance? perfBefore, CornerPerformance? perfAfter)
    {
        if (perfBefore == null || perfAfter == null) return;

        // Find the instruction that was most recently given for this corner
        var matchingInstructions = _instructions.Values
            .Where(i => (i.CornerId == cornerId || i.CornerId == "all"))
            .OrderByDescending(i => i.TimesGiven)
            .FirstOrDefault();

        if (matchingInstructions == null) return;

        // Calculate deltas
        float deltaBefore = perfBefore.DeltaGainedLost;
        float deltaAfter = perfAfter.DeltaGainedLost;
        float improvement = deltaBefore - deltaAfter; // Positive = better (less time lost)

        string result = "no_change";
        if (improvement > 0.1f) result = "improved";
        else if (improvement < -0.1f) result = "worsened";

        // Record outcome
        var outcome = new CoachingOutcome
        {
            InstructionId = matchingInstructions.Id,
            CornerId = cornerId,
            LapGiven = lapInstructionWasGiven,
            LapResult = currentLap,
            DeltaBefore = deltaBefore,
            DeltaAfter = deltaAfter,
            Result = result
        };
        _db.InsertCoachingOutcome(outcome);

        // Update instruction stats and confidence
        if (result == "improved")
        {
            matchingInstructions.TimesImproved++;
            matchingInstructions.ConfidenceScore =
                System.Math.Min(1.0f, matchingInstructions.ConfidenceScore + 0.1f);
        }
        else if (result == "worsened")
        {
            matchingInstructions.TimesWorsened++;
            matchingInstructions.ConfidenceScore =
                System.Math.Max(0.0f, matchingInstructions.ConfidenceScore - 0.15f);
        }
        else
        {
            matchingInstructions.TimesNoChange++;
            matchingInstructions.ConfidenceScore =
                System.Math.Max(0.0f, matchingInstructions.ConfidenceScore - 0.02f);
        }

        _db.UpdateCoachingInstructionConfidence(matchingInstructions.Id,
            matchingInstructions.ConfidenceScore, result);
    }

    /// <summary>
    /// Get the best instruction for a corner and mistake category.
    /// Returns null if no good options available (all below 0.3 confidence).
    /// </summary>
    public string? GetBestInstruction(string cornerId, string mistakeCategory)
    {
        // First look for corner-specific instructions
        var specific = _instructions.Values
            .Where(i => i.CornerId == cornerId)
            .Where(i => i.MistakeCategory == mistakeCategory)
            .OrderByDescending(i => i.ConfidenceScore)
            .FirstOrDefault();

        if (specific != null && specific.ConfidenceScore >= 0.3f)
            return specific.InstructionText;

        // Fall back to default instructions for this mistake
        var defaultInstr = _instructions.Values
            .Where(i => i.CornerId == "all")
            .Where(i => i.MistakeCategory == mistakeCategory)
            .OrderByDescending(i => i.ConfidenceScore)
            .FirstOrDefault();

        if (defaultInstr != null && defaultInstr.ConfidenceScore >= 0.3f)
            return defaultInstr.InstructionText;

        // Pick from defaults even if confidence is low
        if (DefaultInstructions.TryGetValue(mistakeCategory, out var texts))
        {
            return texts.FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Get the confidence score for a specific instruction.
    /// </summary>
    public float GetInstructionConfidence(string cornerId, string mistakeCategory, string text)
    {
        // Look for exact match
        var instr = _instructions.Values
            .FirstOrDefault(i =>
                (i.CornerId == cornerId || i.CornerId == "all") &&
                i.MistakeCategory == mistakeCategory &&
                i.InstructionText == text);

        return instr?.ConfidenceScore ?? 0.5f; // Default to moderate confidence
    }

    /// <summary>
    /// Get all instructions for a corner/mistake combo.
    /// </summary>
    public List<CoachingInstruction> GetInstructionsForMistake(string cornerId, string mistakeCategory)
    {
        return _instructions.Values
            .Where(i =>
                (i.CornerId == cornerId || i.CornerId == "all") &&
                i.MistakeCategory == mistakeCategory)
            .OrderByDescending(i => i.ConfidenceScore)
            .ToList();
    }
}
