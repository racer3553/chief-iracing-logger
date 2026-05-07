// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Hardware vs Driver Resolver
// The tiebreaker: decides if an issue is driver input or hardware settings.
// NOT an ICoachingModule — utility for problem diagnosis.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Verdict on whether to change setup, fix driver input, or adjust hardware</summary>
public enum InputVerdict
{
    /// <summary>Issue is driver technique — don't change setup</summary>
    DoNotChangeSetup,

    /// <summary>Hardware adjustment will help (FFB, brake gain, throttle curve)</summary>
    TryHardwareAdjustment,

    /// <summary>Fix driver input quality first before setup changes</summary>
    FixDriverInputFirst,

    /// <summary>Setup change is likely valid — inputs look clean</summary>
    SetupChangeLikelyValid,

    /// <summary>Need more data to make a determination</summary>
    NeedMoreData
}

/// <summary>Resolution result with explanation and confidence</summary>
public class ResolutionResult
{
    /// <summary>The verdict on what to do</summary>
    public InputVerdict Verdict { get; set; }

    /// <summary>Detailed explanation for UI display</summary>
    public string Explanation { get; set; } = "";

    /// <summary>Short voice version (max 15 words)</summary>
    public string VoiceText { get; set; } = "";

    /// <summary>Confidence in this verdict (0-1)</summary>
    public float Confidence { get; set; }

    /// <summary>Evidence supporting the verdict</summary>
    public Dictionary<string, object> Evidence { get; set; } = new();
}

/// <summary>
/// Resolves whether a problem is caused by driver input, hardware settings,
/// or setup. Uses input quality scores, hardware diagnostics, and telemetry analysis.
/// </summary>
public class HardwareVsDriverResolver
{
    private readonly HardwareTuningCoach _hardwareCoach;
    private readonly InputQualityAnalyzer _inputAnalyzer;

    public HardwareVsDriverResolver(HardwareTuningCoach hardwareCoach, InputQualityAnalyzer inputAnalyzer)
    {
        _hardwareCoach = hardwareCoach ?? throw new ArgumentNullException(nameof(hardwareCoach));
        _inputAnalyzer = inputAnalyzer ?? throw new ArgumentNullException(nameof(inputAnalyzer));
    }

    /// <summary>
    /// Resolve whether an issue is driver or hardware related.
    /// </summary>
    public ResolutionResult Resolve(
        string problemType,
        TelemetrySample[] recentSamples,
        MistakeClassification? classification)
    {
        var result = new ResolutionResult();

        // Need minimum data
        if (recentSamples == null || recentSamples.Length < 10)
        {
            result.Verdict = InputVerdict.NeedMoreData;
            result.Explanation = "Need at least 10 samples to diagnose.";
            result.VoiceText = "Need more data.";
            result.Confidence = 0.3f;
            return result;
        }

        // Get hardware and input quality scores
        var hwScore = _hardwareCoach.CurrentScore;
        var inputScores = _inputAnalyzer.GetCurrentScores();

        // Route based on problem type
        switch (problemType)
        {
            case "loose_on_exit":
                return ResolveLooseOnExit(recentSamples, hwScore, inputScores, classification);

            case "tight_on_entry":
                return ResolveTightOnEntry(recentSamples, hwScore, inputScores, classification);

            case "brake_lockup":
                return ResolveBrakeLockup(recentSamples, hwScore, inputScores, classification);

            case "snap_oversteer":
                return ResolveSnapOversteer(recentSamples, hwScore, inputScores, classification);

            case "inconsistent_braking":
                return ResolveInconsistentBraking(recentSamples, hwScore, inputScores, classification);

            default:
                result.Verdict = InputVerdict.DoNotChangeSetup;
                result.Explanation = $"Unknown problem type: {problemType}. Focus on driver technique.";
                result.VoiceText = "Focus on smooth inputs.";
                result.Confidence = 0.5f;
                return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PROBLEM-SPECIFIC RESOLVERS
    // ═══════════════════════════════════════════════════════════════

    private ResolutionResult ResolveLooseOnExit(TelemetrySample[] samples,
        HardwareDiagnosisScore? hwScore, InputQualityScores inputScores, MistakeClassification? classification)
    {
        var result = new ResolutionResult();

        // Check throttle spike rate on exit
        float maxThrottleRate = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            float rate = Math.Abs(samples[i].Throttle - samples[i - 1].Throttle);
            if (rate > maxThrottleRate) maxThrottleRate = rate;
        }

        // If throttle is spiking regularly (> 0.3/sample), driver needs to smooth it
        if (maxThrottleRate > 0.3f && inputScores.ThrottleControl < 60)
        {
            result.Verdict = InputVerdict.FixDriverInputFirst;
            result.Explanation = "Throttle application is spiking sharply. Smooth, progressive throttle application needed.";
            result.VoiceText = "Apply throttle smoothly, not aggressively.";
            result.Confidence = 0.85f;
            result.Evidence["throttleSpike"] = maxThrottleRate;
            result.Evidence["throttleScore"] = inputScores.ThrottleControl;
            return result;
        }

        // If inputs look clean but hardware curve is wrong
        if (inputScores.ThrottleControl > 70 && hwScore != null && hwScore.DriverTechniqueConfidence < 0.5f)
        {
            result.Verdict = InputVerdict.TryHardwareAdjustment;
            result.Explanation = "Inputs look clean but throttle curve may be too sharp. Try adjusting throttle linearity.";
            result.VoiceText = "Try a less aggressive throttle curve.";
            result.Confidence = 0.75f;
            result.Evidence["inputQuality"] = inputScores.ThrottleControl;
            result.Evidence["hwConfidence"] = hwScore.DriverTechniqueConfidence;
            return result;
        }

        // If car is loose, it's likely setup (rear ARB, spring)
        if (classification?.PrimaryCause == MistakeCause.CarSetup)
        {
            result.Verdict = InputVerdict.SetupChangeLikelyValid;
            result.Explanation = "Inputs are good but car is loose. Increase rear spring or ARB.";
            result.VoiceText = "Car is loose. Setup adjustment needed.";
            result.Confidence = 0.70f;
            return result;
        }

        // Default: driver technique
        result.Verdict = InputVerdict.DoNotChangeSetup;
        result.Explanation = "Loose on exit is typically driver technique. Focus on smooth throttle application.";
        result.VoiceText = "Smooth, patient throttle.";
        result.Confidence = 0.65f;
        return result;
    }

    private ResolutionResult ResolveTightOnEntry(TelemetrySample[] samples,
        HardwareDiagnosisScore? hwScore, InputQualityScores inputScores, MistakeClassification? classification)
    {
        var result = new ResolutionResult();

        // High entry speed = driver technique
        float avgSpeed = samples.Average(s => s.Speed);
        if (avgSpeed > 100 && inputScores.BrakeControl < 65)
        {
            result.Verdict = InputVerdict.FixDriverInputFirst;
            result.Explanation = "Over-speeding entry. Brake earlier and more smoothly.";
            result.VoiceText = "Brake earlier into corners.";
            result.Confidence = 0.80f;
            result.Evidence["avgSpeed"] = avgSpeed;
            result.Evidence["brakeScore"] = inputScores.BrakeControl;
            return result;
        }

        // If brake is smooth but car still understeers, it's setup
        if (inputScores.BrakeControl > 75 && hwScore != null && hwScore.SteeringSetupConfidence < 0.6f)
        {
            result.Verdict = InputVerdict.SetupChangeLikelyValid;
            result.Explanation = "Inputs are clean but car understeers. Increase front downforce or adjust front bias.";
            result.VoiceText = "Setup needs adjustment for front grip.";
            result.Confidence = 0.70f;
            return result;
        }

        // Default: driver technique
        result.Verdict = InputVerdict.DoNotChangeSetup;
        result.Explanation = "Tight on entry usually means braking too late. Adjust brake point.";
        result.VoiceText = "Brake earlier.";
        result.Confidence = 0.70f;
        return result;
    }

    private ResolutionResult ResolveBrakeLockup(TelemetrySample[] samples,
        HardwareDiagnosisScore? hwScore, InputQualityScores inputScores, MistakeClassification? classification)
    {
        var result = new ResolutionResult();

        // Check brake application sharpness
        float maxBrakeRate = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            float rate = Math.Abs(samples[i].Brake - samples[i - 1].Brake);
            if (rate > maxBrakeRate) maxBrakeRate = rate;
        }

        // Rule 1: Brake reaches 100% too fast (< 0.05s) → hardware issue
        int reachMax = samples.Count(s => s.Brake > 0.99f);
        if (reachMax > 2 && maxBrakeRate > 0.4f)
        {
            result.Verdict = InputVerdict.TryHardwareAdjustment;
            result.Explanation = "Brake gain may be too high — reaching max pressure too quickly.";
            result.VoiceText = "Brake gain is too high. Reduce it.";
            result.Confidence = 0.85f;
            result.Evidence["brakeReachMax"] = reachMax;
            result.Evidence["brakeRate"] = maxBrakeRate;
            return result;
        }

        // Rule 2: Max brake never exceeds 0.7 in heavy zones → pedal issue
        float maxBrake = samples.Max(s => s.Brake);
        if (maxBrake < 0.7f)
        {
            result.Verdict = InputVerdict.TryHardwareAdjustment;
            result.Explanation = "Brake pedal sensitivity may be too low. Increase brake force.";
            result.VoiceText = "Increase brake pedal sensitivity.";
            result.Confidence = 0.75f;
            result.Evidence["maxBrake"] = maxBrake;
            return result;
        }

        // Rule 3: Inputs are spiky → driver
        if (inputScores.BrakeControl < 55)
        {
            result.Verdict = InputVerdict.FixDriverInputFirst;
            result.Explanation = "Brake input is too abrupt. Use a smoother, more progressive brake application.";
            result.VoiceText = "Smooth your brake application.";
            result.Confidence = 0.80f;
            result.Evidence["brakeScore"] = inputScores.BrakeControl;
            return result;
        }

        // Rule 4: If hardware score is low, try adjustment
        if (hwScore != null && hwScore.BrakeSetupConfidence < 0.5f)
        {
            result.Verdict = InputVerdict.TryHardwareAdjustment;
            result.Explanation = "Brake curve shape may be too sharp. Try adjusting brake linearity.";
            result.VoiceText = "Adjust brake curve smoothness.";
            result.Confidence = 0.70f;
            result.Evidence["hwBrakeConfidence"] = hwScore.BrakeSetupConfidence;
            return result;
        }

        // Default
        result.Verdict = InputVerdict.DoNotChangeSetup;
        result.Explanation = "Brake lockup is usually driver technique. Focus on smooth brake application.";
        result.VoiceText = "Ease off the brake pedal.";
        result.Confidence = 0.65f;
        return result;
    }

    private ResolutionResult ResolveSnapOversteer(TelemetrySample[] samples,
        HardwareDiagnosisScore? hwScore, InputQualityScores inputScores, MistakeClassification? classification)
    {
        var result = new ResolutionResult();

        // Check throttle spike rate
        float maxThrottleRate = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            float rate = Math.Abs(samples[i].Throttle - samples[i - 1].Throttle);
            if (rate > maxThrottleRate) maxThrottleRate = rate;
        }

        // Rule: Throttle spikes on every corner exit (> 0.5 in < 0.1s) → hardware
        int exitSampleCount = 0;
        var throttleSpikeCount = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if (samples[i].Throttle > 0.5f)
            {
                exitSampleCount++;
                if (Math.Abs(samples[i].Throttle - samples[i - 1].Throttle) > 0.4f)
                    throttleSpikeCount++;
            }
        }

        if (exitSampleCount > 0 && throttleSpikeCount / (float)exitSampleCount > 0.5f)
        {
            result.Verdict = InputVerdict.TryHardwareAdjustment;
            result.Explanation = "Throttle curve is too sharp. Try reducing throttle linearity value.";
            result.VoiceText = "Throttle curve needs adjustment.";
            result.Confidence = 0.80f;
            result.Evidence["throttleSpikeRatio"] = throttleSpikeCount / (float)exitSampleCount;
            return result;
        }

        // If inputs are clean, it's setup
        if (inputScores.ThrottleControl > 75)
        {
            result.Verdict = InputVerdict.SetupChangeLikelyValid;
            result.Explanation = "Inputs look good but car is snapping oversteer. Increase rear downforce or reduce rear spring.";
            result.VoiceText = "Setup needs adjustment for rear stability.";
            result.Confidence = 0.75f;
            return result;
        }

        // Default: driver
        result.Verdict = InputVerdict.FixDriverInputFirst;
        result.Explanation = "Wait until wheel opens before applying full throttle.";
        result.VoiceText = "Wait on throttle until wheel opens.";
        result.Confidence = 0.70f;
        return result;
    }

    private ResolutionResult ResolveInconsistentBraking(TelemetrySample[] samples,
        HardwareDiagnosisScore? hwScore, InputQualityScores inputScores, MistakeClassification? classification)
    {
        var result = new ResolutionResult();

        // Calculate brake input variance
        var brakeSamples = samples.Where(s => s.Brake > 0).Select(s => s.Brake).ToList();
        float brakeVariance = 0;
        if (brakeSamples.Count > 1)
        {
            float avg = brakeSamples.Average();
            brakeVariance = (float)Math.Sqrt(brakeSamples.Sum(b => Math.Pow(b - avg, 2)) / brakeSamples.Count);
        }

        // High variance in inputs = driver
        if (brakeVariance > 0.25f && inputScores.BrakeControl < 60)
        {
            result.Verdict = InputVerdict.FixDriverInputFirst;
            result.Explanation = "Brake pedal input is inconsistent. Use consistent pressure at each corner.";
            result.VoiceText = "Use the same brake pressure every lap.";
            result.Confidence = 0.85f;
            result.Evidence["brakeVariance"] = brakeVariance;
            result.Evidence["brakeScore"] = inputScores.BrakeControl;
            return result;
        }

        // If inputs look consistent but hardware score is low
        if (inputScores.BrakeControl > 70 && hwScore != null && hwScore.BrakeSetupConfidence < 0.6f)
        {
            result.Verdict = InputVerdict.TryHardwareAdjustment;
            result.Explanation = "Brake feel is inconsistent. Try adjusting brake force or curve shape.";
            result.VoiceText = "Brake feel needs adjustment.";
            result.Confidence = 0.70f;
            result.Evidence["hwBrakeConfidence"] = hwScore.BrakeSetupConfidence;
            return result;
        }

        // Track temp change affects consistency
        result.Verdict = InputVerdict.DoNotChangeSetup;
        result.Explanation = "Consistency may improve as tires and track warm up.";
        result.VoiceText = "Consistency will improve as track warms.";
        result.Confidence = 0.60f;
        return result;
    }
}
