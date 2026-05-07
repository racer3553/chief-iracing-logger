// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Voice Script Library
// Predefined short voice scripts organized by category.
// NOT an ICoachingModule — static library for voice text.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Static library of pre-written voice scripts for coaching.
/// All scripts are short (max 7 words) for in-car delivery.
/// </summary>
public static class VoiceScriptLibrary
{
    // ═══════════════════════════════════════════════════════════════
    // BRAKING SCRIPTS (Max 6 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] BrakingScripts = new[]
    {
        "Brake earlier.",
        "Release smoother.",
        "Do not stab brake.",
        "Trail it longer.",
        "Same marker, less peak.",
        "Deeper on the brakes.",
        "Straight brake. No turning.",
        "Ease initial pressure.",
        "Progressive release.",
        "Brake point is earlier.",
    };

    // ═══════════════════════════════════════════════════════════════
    // THROTTLE SCRIPTS (Max 6 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] ThrottleScripts = new[]
    {
        "Wait on throttle.",
        "Roll throttle.",
        "Pick it up sooner.",
        "Wheel open, then throttle.",
        "Patient. Do not spike.",
        "Smooth application.",
        "Commit to throttle.",
        "Hold maintenance throttle.",
        "Gradual buildup.",
        "No wheelspin.",
    };

    // ═══════════════════════════════════════════════════════════════
    // STEERING SCRIPTS (Max 5 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] SteeringScripts = new[]
    {
        "Quiet hands.",
        "One input.",
        "Less wheel.",
        "Let it rotate.",
        "Smooth arc.",
        "Slow hands.",
        "Unwind sooner.",
        "Do not fight it.",
        "Trail into corner.",
        "Load it progressively.",
    };

    // ═══════════════════════════════════════════════════════════════
    // LINE SCRIPTS (Max 5 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] LineScripts = new[]
    {
        "Late apex.",
        "Sacrifice entry.",
        "Use exit curb.",
        "Tighten apex.",
        "Give up entry.",
        "Wider entry.",
        "Diamond the corner.",
        "V the corner.",
        "Outside to apex.",
        "Rotate earlier.",
    };

    // ═══════════════════════════════════════════════════════════════
    // MENTAL SCRIPTS (Max 3 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] MentalScripts = new[]
    {
        "Reset.",
        "Breathe.",
        "Commit.",
        "Rhythm.",
        "Calm.",
        "Focus.",
        "Simplify.",
        "Build up.",
        "Relax.",
        "Smooth.",
    };

    // ═══════════════════════════════════════════════════════════════
    // RACECRAFT SCRIPTS (Max 6 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] RacecraftScripts = new[]
    {
        "Defend inside.",
        "Set up exit pass.",
        "Do not fight here.",
        "Pressure him.",
        "Draft. Save tires.",
        "Let him go.",
        "Close the door.",
        "Fake inside, go outside.",
        "Block the inside.",
        "Take the line.",
    };

    // ═══════════════════════════════════════════════════════════════
    // TIRE MANAGEMENT SCRIPTS (Max 7 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] TireScripts = new[]
    {
        "Save right front.",
        "Rears are hot.",
        "Roll throttle.",
        "Less wheel. Save tires.",
        "Manage entry.",
        "Stop scrubbing.",
        "Tires are good. Push.",
        "Wheelspin on exit. Patience.",
        "Quiet inputs.",
        "Smooth buildup.",
    };

    // ═══════════════════════════════════════════════════════════════
    // PRAISE SCRIPTS (Max 5 words)
    // ═══════════════════════════════════════════════════════════════

    private static readonly string[] PraiseScripts = new[]
    {
        "Good lap.",
        "Better.",
        "Repeat that.",
        "Nice exit.",
        "Clean.",
        "Strong lap.",
        "Keep it.",
        "On pace.",
        "Very smooth.",
        "Perfect.",
    };

    // ═══════════════════════════════════════════════════════════════
    // PUBLIC METHODS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Get a braking script for a specific issue.
    /// </summary>
    public static string GetBraking(string specificIssue)
    {
        if (string.IsNullOrEmpty(specificIssue))
            return BrakingScripts[new Random().Next(BrakingScripts.Length)];

        return specificIssue.ToLower() switch
        {
            "release" => "Release smoother.",
            "deep" or "deeper" => "Deeper on the brakes.",
            "trail" => "Trail it longer.",
            "early" or "earlier" => "Brake earlier.",
            "stab" or "slam" => "Do not stab brake.",
            "smooth" or "progressive" => "Ease initial pressure.",
            _ => BrakingScripts[new Random().Next(BrakingScripts.Length)]
        };
    }

    /// <summary>
    /// Get a throttle script for a specific issue.
    /// </summary>
    public static string GetThrottle(string specificIssue)
    {
        if (string.IsNullOrEmpty(specificIssue))
            return ThrottleScripts[new Random().Next(ThrottleScripts.Length)];

        return specificIssue.ToLower() switch
        {
            "wait" => "Wait on throttle.",
            "roll" => "Roll throttle.",
            "smooth" => "Smooth application.",
            "patient" => "Patient. Do not spike.",
            "early" or "sooner" => "Pick it up sooner.",
            "wheelspin" => "No wheelspin.",
            _ => ThrottleScripts[new Random().Next(ThrottleScripts.Length)]
        };
    }

    /// <summary>
    /// Get a steering script for a specific issue.
    /// </summary>
    public static string GetSteering(string specificIssue)
    {
        if (string.IsNullOrEmpty(specificIssue))
            return SteeringScripts[new Random().Next(SteeringScripts.Length)];

        return specificIssue.ToLower() switch
        {
            "quiet" or "smooth" => "Quiet hands.",
            "less" => "Less wheel.",
            "one" => "One input.",
            "slow" => "Slow hands.",
            "unwind" => "Unwind sooner.",
            "rotate" => "Let it rotate.",
            _ => SteeringScripts[new Random().Next(SteeringScripts.Length)]
        };
    }

    /// <summary>
    /// Get a line script for a specific issue.
    /// </summary>
    public static string GetLine(string specificIssue)
    {
        if (string.IsNullOrEmpty(specificIssue))
            return LineScripts[new Random().Next(LineScripts.Length)];

        return specificIssue.ToLower() switch
        {
            "late" or "apex" => "Late apex.",
            "entry" => "Sacrifice entry.",
            "curb" or "exit" => "Use exit curb.",
            "tight" => "Tighten apex.",
            "wide" or "wider" => "Wider entry.",
            "v" or "diamond" => "V the corner.",
            _ => LineScripts[new Random().Next(LineScripts.Length)]
        };
    }

    /// <summary>
    /// Get a mental script for a specific issue.
    /// </summary>
    public static string GetMental(string specificIssue)
    {
        if (string.IsNullOrEmpty(specificIssue))
            return MentalScripts[new Random().Next(MentalScripts.Length)];

        return specificIssue.ToLower() switch
        {
            "reset" or "calm" => "Reset.",
            "breathe" => "Breathe.",
            "commit" => "Commit.",
            "rhythm" => "Rhythm.",
            "focus" => "Focus.",
            "simplify" => "Simplify.",
            "build" => "Build up.",
            _ => MentalScripts[new Random().Next(MentalScripts.Length)]
        };
    }

    /// <summary>
    /// Get a racecraft script for a specific situation.
    /// </summary>
    public static string GetRacecraft(string situation)
    {
        if (string.IsNullOrEmpty(situation))
            return RacecraftScripts[new Random().Next(RacecraftScripts.Length)];

        return situation.ToLower() switch
        {
            "defend" => "Defend inside.",
            "pass" => "Set up exit pass.",
            "dirty" or "traffic" => "Do not fight here.",
            "pressure" => "Pressure him.",
            "draft" => "Draft. Save tires.",
            "yield" or "let" => "Let him go.",
            "block" => "Close the door.",
            "fake" => "Fake inside, go outside.",
            _ => RacecraftScripts[new Random().Next(RacecraftScripts.Length)]
        };
    }

    /// <summary>
    /// Get a tire management script for a specific issue.
    /// </summary>
    public static string GetTireManagement(string issue)
    {
        if (string.IsNullOrEmpty(issue))
            return TireScripts[new Random().Next(TireScripts.Length)];

        return issue.ToLower() switch
        {
            "rf" or "right_front" => "Save right front.",
            "rear" or "hot" => "Rears are hot.",
            "roll" => "Roll throttle.",
            "wheel" => "Less wheel. Save tires.",
            "scrub" => "Stop scrubbing.",
            "push" or "good" => "Tires are good. Push.",
            "wheelspin" => "Wheelspin on exit. Patience.",
            _ => TireScripts[new Random().Next(TireScripts.Length)]
        };
    }

    /// <summary>
    /// Get a praise script (encouragement).
    /// </summary>
    public static string GetPraise()
    {
        return PraiseScripts[new Random().Next(PraiseScripts.Length)];
    }

    /// <summary>
    /// Build a complete corner call from components.
    /// Max 10 words during corners, 15 on straights.
    /// Example: "Turn 5. Brake at 3. Smooth release."
    /// </summary>
    public static string BuildCornerCall(string cornerName, string? brakeNote, string? exitNote, string? lineNote)
    {
        var parts = new List<string> { cornerName + "." };

        if (!string.IsNullOrEmpty(brakeNote))
            parts.Add(brakeNote);
        if (!string.IsNullOrEmpty(lineNote))
            parts.Add(lineNote);
        if (!string.IsNullOrEmpty(exitNote))
            parts.Add(exitNote);

        string fullCall = string.Join(" ", parts);
        return Shorten(fullCall, 10);
    }

    /// <summary>
    /// Truncate text to maximum word count.
    /// </summary>
    public static string Shorten(string text, int maxWords)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
            return text;

        return string.Join(" ", words.Take(maxWords));
    }

    /// <summary>
    /// Get a random script from any category.
    /// </summary>
    public static string GetRandom()
    {
        var allCategories = new[]
        {
            BrakingScripts,
            ThrottleScripts,
            SteeringScripts,
            LineScripts,
            MentalScripts,
            RacecraftScripts,
            TireScripts,
            PraiseScripts,
        };

        var randomCategory = allCategories[new Random().Next(allCategories.Length)];
        return randomCategory[new Random().Next(randomCategory.Length)];
    }
}
