// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Mistake Classifier
// Classifies every problem into its root cause with confidence scoring.
// NOT an ICoachingModule — utility used by other modules and coaches.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Root cause classification for driving problems</summary>
public enum MistakeCause
{
    /// <summary>Driver technique issue (braking point, turn-in, throttle application)</summary>
    DriverTechnique,

    /// <summary>Car setup problem (suspension, aero, balance)</summary>
    CarSetup,

    /// <summary>Hardware settings (FFB, brake gain, pedal curve, damping)</summary>
    HardwareSetting,

    /// <summary>Track condition (grip level, temperature change, surface condition)</summary>
    TrackCondition,

    /// <summary>Traffic situation (car nearby, drafting effects)</summary>
    TrafficSituation,

    /// <summary>Tire degradation or temperature issue</summary>
    TireDegradation,

    /// <summary>Fuel load affecting performance</summary>
    FuelLoad
}

/// <summary>Result of mistake classification with confidence scoring</summary>
public class MistakeClassification
{
    /// <summary>Brief description of the problem detected</summary>
    public string Problem { get; set; } = "";

    /// <summary>Primary root cause</summary>
    public MistakeCause PrimaryCause { get; set; }

    /// <summary>Confidence in primary cause (0-1)</summary>
    public float PrimaryConfidence { get; set; }

    /// <summary>Secondary root cause if applicable</summary>
    public MistakeCause? SecondaryCause { get; set; }

    /// <summary>Confidence in secondary cause</summary>
    public float? SecondaryConfidence { get; set; }

    /// <summary>Detailed explanation of the problem and its cause</summary>
    public string Explanation { get; set; } = "";

    /// <summary>Short explanation for voice delivery (max 15 words)</summary>
    public string ShortExplanation { get; set; } = "";

    /// <summary>All cause scores for debugging and analysis</summary>
    public Dictionary<MistakeCause, float> AllScores { get; set; } = new();
}

/// <summary>
/// Classifies driving problems into root causes: driver, setup, hardware, track, traffic, tires, fuel.
/// Uses telemetry traces, performance deltas, and hardware diagnostics to narrow down the cause.
/// </summary>
public class MistakeClassifier
{
    private readonly HardwareTuningCoach _hardwareCoach;

    public MistakeClassifier(HardwareTuningCoach hardwareCoach)
    {
        _hardwareCoach = hardwareCoach ?? throw new ArgumentNullException(nameof(hardwareCoach));
    }

    /// <summary>
    /// Classify a problem into its root cause(s).
    /// </summary>
    public MistakeClassification Classify(
        string problemType,
        TelemetrySample[] samples,
        CornerPerformance? perf,
        CornerPerformance? bestPerf,
        HardwareDiagnosisScore? hwScore)
    {
        var result = new MistakeClassification { Problem = problemType };

        // Initialize all cause scores
        foreach (var cause in Enum.GetValues<MistakeCause>())
        {
            result.AllScores[cause] = 0f;
        }

        // Route to problem-specific classifier
        switch (problemType)
        {
            case "loose_on_exit":
                ClassifyLooseOnExit(samples, perf, hwScore, result);
                break;

            case "tight_on_entry":
                ClassifyTightOnEntry(samples, perf, hwScore, result);
                break;

            case "brake_lockup":
                ClassifyBrakeLockup(samples, perf, hwScore, result);
                break;

            case "oversteer_mid":
                ClassifyOversteerMid(samples, perf, hwScore, result);
                break;

            case "understeer_mid":
                ClassifyUndersteerMid(samples, perf, hwScore, result);
                break;

            case "snap_oversteer":
                ClassifySnapOversteer(samples, perf, hwScore, result);
                break;

            case "off_track":
                ClassifyOffTrack(samples, perf, result);
                break;

            case "inconsistent_braking":
                ClassifyInconsistentBraking(samples, hwScore, result);
                break;

            case "poor_exit_speed":
                ClassifyPoorExitSpeed(samples, perf, hwScore, result);
                break;

            case "excessive_tire_wear":
                ClassifyExcessiveTireWear(samples, perf, result);
                break;

            default:
                result.PrimaryCause = MistakeCause.DriverTechnique;
                result.PrimaryConfidence = 0.5f;
                result.Explanation = $"Unknown problem type: {problemType}";
                break;
        }

        // Determine primary and secondary causes from scores
        var sortedCauses = result.AllScores.OrderByDescending(kvp => kvp.Value).ToList();

        if (sortedCauses.Count > 0 && sortedCauses[0].Value > 0)
        {
            result.PrimaryCause = sortedCauses[0].Key;
            result.PrimaryConfidence = sortedCauses[0].Value;
        }

        if (sortedCauses.Count > 1 && sortedCauses[1].Value > 0.1f)
        {
            result.SecondaryCause = sortedCauses[1].Key;
            result.SecondaryConfidence = sortedCauses[1].Value;
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // PROBLEM CLASSIFIERS
    // ═══════════════════════════════════════════════════════════════

    private void ClassifyLooseOnExit(TelemetrySample[] samples, CornerPerformance? perf,
        HardwareDiagnosisScore? hwScore, MistakeClassification result)
    {
        // Check throttle trace: spike = driver technique
        var throttleSpikes = samples.Count(s => s.Throttle > 0.5f && (samples.IndexOf(s) > 0 ?
            Math.Abs(s.Throttle - samples[Array.IndexOf(samples, s) - 1].Throttle) > 0.3f : false));

        if (throttleSpikes > 2)
        {
            result.AllScores[MistakeCause.DriverTechnique] = 0.80f;
            result.Explanation = "Throttle application is spiking on exit. Smooth, progressive throttle.";
            result.ShortExplanation = "Smooth throttle application.";
        }

        // Yaw rate even with smooth throttle = setup (rear stability)
        var avgYawRate = samples.Average(s => Math.Abs(s.YawRate));
        if (avgYawRate > 1.5f)
        {
            result.AllScores[MistakeCause.CarSetup] += 0.70f;
            result.Explanation += " Car is loose (rear ARB or spring too soft).";
        }

        // Check tire temps: rear tire temps elevated = tire issues
        if (perf != null)
        {
            result.AllScores[MistakeCause.TireDegradation] += 0.40f;
        }

        // Check hardware scores: throttle curve too sharp
        if (hwScore != null && hwScore.DriverTechniqueConfidence < 0.5f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.50f;
        }

        if (string.IsNullOrEmpty(result.Explanation))
        {
            result.Explanation = "Loose on exit detected.";
            result.ShortExplanation = "Car is loose on exit.";
            result.AllScores[MistakeCause.CarSetup] = 0.65f;
        }
    }

    private void ClassifyTightOnEntry(TelemetrySample[] samples, CornerPerformance? perf,
        HardwareDiagnosisScore? hwScore, MistakeClassification result)
    {
        // Entry speed too high = driver technique
        if (perf != null && perf.EntrySpeed > 0)
        {
            result.AllScores[MistakeCause.DriverTechnique] = 0.75f;
            result.Explanation = "Over-speeding entry causes understeer.";
            result.ShortExplanation = "Brake earlier.";
        }

        // Understeer gradient = setup (front balance, aero)
        result.AllScores[MistakeCause.CarSetup] += 0.60f;

        // Front tire temps elevated = tire issue
        result.AllScores[MistakeCause.TireDegradation] += 0.35f;

        // Steering response lag = hardware (steering damping)
        if (hwScore != null && hwScore.SteeringSetupConfidence < 0.6f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.45f;
        }

        if (string.IsNullOrEmpty(result.Explanation))
        {
            result.Explanation = "Understeer on entry.";
            result.ShortExplanation = "Car pushing in.";
        }
    }

    private void ClassifyBrakeLockup(TelemetrySample[] samples, CornerPerformance? perf,
        HardwareDiagnosisScore? hwScore, MistakeClassification result)
    {
        // Check brake application rate: sharp rise = driver
        var brakeSamples = samples.Where(s => s.Brake > 0).ToList();
        float avgBrakeRate = 0;
        if (brakeSamples.Count > 1)
        {
            var rates = new List<float>();
            for (int i = 1; i < brakeSamples.Count; i++)
            {
                rates.Add(Math.Abs(brakeSamples[i].Brake - brakeSamples[i - 1].Brake));
            }
            avgBrakeRate = rates.Average();
        }

        if (avgBrakeRate > 0.15f)
        {
            result.AllScores[MistakeCause.DriverTechnique] = 0.70f;
            result.Explanation = "Sharp brake application causing lockup. Ease in smoothly.";
            result.ShortExplanation = "Smooth brake pressure.";
        }

        // Brake bias issue = setup
        result.AllScores[MistakeCause.CarSetup] += 0.50f;

        // Brake curve shape = hardware
        if (hwScore != null && hwScore.BrakeSetupConfidence < 0.5f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.65f;
            result.Explanation = "Brake pedal may be too sensitive or gain too high.";
        }

        if (string.IsNullOrEmpty(result.Explanation))
        {
            result.Explanation = "Brake lockup detected.";
            result.ShortExplanation = "Ease brake pressure.";
            result.AllScores[MistakeCause.HardwareSetting] = 0.70f;
        }
    }

    private void ClassifyOversteerMid(TelemetrySample[] samples, CornerPerformance? perf,
        HardwareDiagnosisScore? hwScore, MistakeClassification result)
    {
        // Check throttle lift: if driver lifted, it's technique
        var avgThrottle = samples.Average(s => s.Throttle);
        if (avgThrottle < 0.3f)
        {
            result.AllScores[MistakeCause.DriverTechnique] = 0.70f;
            result.Explanation = "Driver lifted throttle mid-corner. Commit to the corner.";
            result.ShortExplanation = "Commit to corner.";
        }

        // Rear spring/ARB = setup
        result.AllScores[MistakeCause.CarSetup] += 0.65f;

        // FFB strength = hardware
        if (hwScore != null && hwScore.FfbSetupConfidence < 0.6f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.40f;
        }

        if (string.IsNullOrEmpty(result.Explanation))
        {
            result.Explanation = "Mid-corner oversteer.";
            result.ShortExplanation = "Car is loose mid-corner.";
            result.AllScores[MistakeCause.CarSetup] = 0.75f;
        }
    }

    private void ClassifyUndersteerMid(TelemetrySample[] samples, CornerPerformance? perf,
        HardwareDiagnosisScore? hwScore, MistakeClassification result)
    {
        // Entry speed too high = driver
        result.AllScores[MistakeCause.DriverTechnique] = 0.60f;
        result.Explanation = "Over-speeding through the corner.";
        result.ShortExplanation = "Slow entry speed.";

        // Front balance = setup
        result.AllScores[MistakeCause.CarSetup] = 0.75f;

        // Steering smoothing = hardware
        if (hwScore != null && hwScore.SteeringSetupConfidence < 0.6f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.40f;
        }

        // Front tire wear
        result.AllScores[MistakeCause.TireDegradation] += 0.35f;
    }

    private void ClassifySnapOversteer(TelemetrySample[] samples, CornerPerformance? perf,
        HardwareDiagnosisScore? hwScore, MistakeClassification result)
    {
        // Throttle spike rate = driver
        var throttleSamples = samples.Where(s => s.Throttle > 0.3f).ToList();
        float maxSpikeRate = 0;
        if (throttleSamples.Count > 1)
        {
            for (int i = 1; i < throttleSamples.Count; i++)
            {
                float rate = Math.Abs(throttleSamples[i].Throttle - throttleSamples[i - 1].Throttle);
                if (rate > maxSpikeRate) maxSpikeRate = rate;
            }
        }

        if (maxSpikeRate > 0.4f)
        {
            result.AllScores[MistakeCause.DriverTechnique] = 0.80f;
            result.Explanation = "Throttle application too aggressive. Wait until wheel opens.";
            result.ShortExplanation = "Wait on throttle.";
        }

        // Rear stability = setup
        result.AllScores[MistakeCause.CarSetup] += 0.60f;

        // Throttle curve = hardware
        if (hwScore != null && hwScore.DriverTechniqueConfidence < 0.5f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.50f;
        }

        if (string.IsNullOrEmpty(result.Explanation))
        {
            result.Explanation = "Snap oversteer on exit.";
            result.ShortExplanation = "Wait on throttle.";
            result.AllScores[MistakeCause.DriverTechnique] = 0.75f;
        }
    }

    private void ClassifyOffTrack(TelemetrySample[] samples, CornerPerformance? perf, MistakeClassification result)
    {
        // Off-track is almost always driver
        result.AllScores[MistakeCause.DriverTechnique] = 0.90f;
        result.Explanation = "Off-track excursion. Adjust braking or turn-in.";
        result.ShortExplanation = "Ran out of road.";
    }

    private void ClassifyInconsistentBraking(TelemetrySample[] samples, HardwareDiagnosisScore? hwScore,
        MistakeClassification result)
    {
        // High input variance = driver
        var brakeValues = samples.Where(s => s.Brake > 0).Select(s => s.Brake).ToList();
        float brakeVariance = brakeValues.Count > 1 ?
            (float)Math.Sqrt(brakeValues.Sum(b => Math.Pow(b - brakeValues.Average(), 2)) / brakeValues.Count) : 0;

        if (brakeVariance > 0.2f)
        {
            result.AllScores[MistakeCause.DriverTechnique] = 0.75f;
            result.Explanation = "Brake input variance is high. Use consistent pedal pressure.";
            result.ShortExplanation = "Brake more consistently.";
        }

        // Brake feel = hardware
        if (hwScore != null && hwScore.BrakeSetupConfidence < 0.6f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.55f;
        }

        // Track temp change affects grip
        result.AllScores[MistakeCause.TrackCondition] += 0.30f;

        if (string.IsNullOrEmpty(result.Explanation))
        {
            result.Explanation = "Inconsistent braking detected.";
            result.ShortExplanation = "Be more consistent.";
            result.AllScores[MistakeCause.DriverTechnique] = 0.65f;
        }
    }

    private void ClassifyPoorExitSpeed(TelemetrySample[] samples, CornerPerformance? perf,
        HardwareDiagnosisScore? hwScore, MistakeClassification result)
    {
        // Throttle pickup timing = driver
        result.AllScores[MistakeCause.DriverTechnique] = 0.70f;
        result.Explanation = "Waiting too long to apply throttle on exit.";
        result.ShortExplanation = "Apply throttle sooner.";

        // Diff settings = setup
        result.AllScores[MistakeCause.CarSetup] += 0.50f;

        // Throttle curve = hardware
        if (hwScore != null && hwScore.DriverTechniqueConfidence < 0.5f)
        {
            result.AllScores[MistakeCause.HardwareSetting] += 0.45f;
        }
    }

    private void ClassifyExcessiveTireWear(TelemetrySample[] samples, CornerPerformance? perf,
        MistakeClassification result)
    {
        // High steering angle excess = driver
        var maxSteeringAngle = samples.Max(s => Math.Abs(s.SteeringAngle));
        result.AllScores[MistakeCause.DriverTechnique] = maxSteeringAngle > 1.5f ? 0.65f : 0.40f;
        result.Explanation = "Excessive steering input or aggressive driving style.";
        result.ShortExplanation = "Smooth your steering.";

        // Camber/pressure = setup
        result.AllScores[MistakeCause.CarSetup] += 0.55f;

        // Driving consistency = technique
        result.AllScores[MistakeCause.DriverTechnique] += 0.35f;

        // Tire degradation
        result.AllScores[MistakeCause.TireDegradation] += 0.50f;
    }
}
