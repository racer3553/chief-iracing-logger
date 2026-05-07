// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Driving Problem Classifier
// Classifies driving problems into root cause categories:
// driver technique, car setup, hardware settings, track conditions, traffic.
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// PROBLEM CAUSE ENUM
// ═══════════════════════════════════════

public enum ProblemCause
{
    DriverTechnique,
    CarSetup,
    HardwareSetting,
    TrackCondition,
    TrafficSituation
}

// ═══════════════════════════════════════
// PROBLEM CLASSIFICATION
// ═══════════════════════════════════════

public class ProblemClassification
{
    public string Problem { get; set; } = "";           // e.g., "Loose on exit"
    public ProblemCause PrimaryCause { get; set; }
    public float PrimaryCauseConfidence { get; set; }
    public ProblemCause? SecondaryCause { get; set; }
    public float? SecondaryCauseConfidence { get; set; }
    public string Explanation { get; set; } = "";        // Why this classification
    public string Recommendation { get; set; } = "";     // What to do
    public string VoiceCall { get; set; } = "";           // Short voice version
    public Dictionary<ProblemCause, float> CauseScores { get; set; } = new(); // All scores
}

// ═══════════════════════════════════════
// DRIVING PROBLEM CLASSIFIER
// ═══════════════════════════════════════

public class DrivingProblemClassifier
{
    private readonly HardwareTuningCoach _hardwareCoach;

    public event Action<ProblemClassification>? OnClassification;

    // ═══ CONSTRUCTOR ═══
    public DrivingProblemClassifier(HardwareTuningCoach hardwareCoach)
    {
        _hardwareCoach = hardwareCoach ?? throw new ArgumentNullException(nameof(hardwareCoach));
    }

    // ═══ MAIN CLASSIFICATION METHOD ═══
    public ProblemClassification ClassifyProblem(
        string problemType,
        TelemetrySample[] recentSamples,
        Dictionary<string, object>? eventData = null)
    {
        var classification = new ProblemClassification
        {
            Problem = problemType,
            CauseScores = new()
        };

        // Initialize all scores
        foreach (ProblemCause cause in Enum.GetValues(typeof(ProblemCause)))
        {
            classification.CauseScores[cause] = 0f;
        }

        // Route to specific problem classifier
        switch (problemType.ToLower())
        {
            case "loose_on_exit":
                ClassifyLooseOnExit(classification, recentSamples, eventData);
                break;
            case "tight_on_entry":
                ClassifyTightOnEntry(classification, recentSamples, eventData);
                break;
            case "brake_lockup":
                ClassifyBrakeLockup(classification, recentSamples, eventData);
                break;
            case "oversteer_mid_corner":
                ClassifyOversteerMidCorner(classification, recentSamples, eventData);
                break;
            case "understeer_mid_corner":
                ClassifyUndersteerMidCorner(classification, recentSamples, eventData);
                break;
            case "snap_oversteer":
                ClassifySnapOversteer(classification, recentSamples, eventData);
                break;
            case "off_track":
                ClassifyOffTrack(classification, recentSamples, eventData);
                break;
            case "inconsistent_braking":
                ClassifyInconsistentBraking(classification, recentSamples, eventData);
                break;
            default:
                // Unknown problem: default to driver technique
                classification.CauseScores[ProblemCause.DriverTechnique] = 0.5f;
                classification.PrimaryCause = ProblemCause.DriverTechnique;
                classification.PrimaryCauseConfidence = 0.5f;
                classification.Explanation = "Unknown problem type.";
                classification.Recommendation = "Review telemetry data.";
                break;
        }

        // Determine primary and secondary causes
        DeterminePrimaryCause(classification);

        // Apply hardware coach cross-reference
        ApplyHardwareCrossReference(classification);

        // Generate recommendations
        GenerateRecommendation(classification);

        OnClassification?.Invoke(classification);
        return classification;
    }

    // ═══ PROBLEM-SPECIFIC CLASSIFIERS ═══

    private void ClassifyLooseOnExit(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length == 0) return;

        var recent = samples.TakeLast(20).ToArray();
        if (recent.Length == 0) return;

        float avgThrottle = recent.Average(s => s.Throttle);
        float avgSteering = recent.Average(s => Math.Abs(s.SteeringAngle));
        float maxThrottleAccel = GetMaxThrottleAcceleration(recent);
        float avgRearTireTemp = GetAvgRearTireTemp(recent);
        float avgYawRate = recent.Average(s => Math.Abs(s.YawRate));

        // ─ DRIVER TECHNIQUE: throttle applied too early with too much wheel
        if (maxThrottleAccel > 0.3f && avgSteering > 0.4f)
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.75f;
        }

        // ─ SETUP: rear instability (high yaw rate with moderate inputs)
        if (avgYawRate > 0.4f && avgThrottle < 0.6f && avgSteering < 0.5f)
        {
            c.CauseScores[ProblemCause.CarSetup] = 0.7f;
        }

        // ─ HARDWARE: throttle curve too sharp
        if (maxThrottleAccel > 0.5f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] = 0.65f;
        }

        // ─ TRACK: temperature affecting rear grip
        if (avgRearTireTemp > 100f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.6f;
        }
    }

    private void ClassifyTightOnEntry(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length == 0) return;

        var recent = samples.TakeLast(20).ToArray();
        if (recent.Length == 0) return;

        float avgSpeed = recent.Average(s => s.Speed);
        float avgBrake = recent.Average(s => s.Brake);
        float avgSteering = recent.Average(s => Math.Abs(s.SteeringAngle));
        float maxSteering = recent.Max(s => Math.Abs(s.SteeringAngle));
        float avgLatAccel = recent.Average(s => Math.Abs(s.LatAccel));

        // ─ DRIVER TECHNIQUE: entry speed too high
        if (avgSpeed > 50f && avgBrake < 0.3f)  // High speed, low braking
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.8f;
        }

        // ─ SETUP: understeer (high steering angle needed for low lateral accel)
        if (maxSteering > 1.0f && avgLatAccel < 0.8f)
        {
            c.CauseScores[ProblemCause.CarSetup] = 0.75f;
        }

        // ─ HARDWARE: FFB too weak to feel front grip limit
        if (avgSteering > 0.6f && avgLatAccel > 1.0f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] = 0.6f;
        }

        // ─ TRACK: low grip area
        if (avgLatAccel < 0.7f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.65f;
        }
    }

    private void ClassifyBrakeLockup(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length == 0) return;

        var recent = samples.TakeLast(20).ToArray();
        if (recent.Length == 0) return;

        float maxBrake = recent.Max(s => s.Brake);
        float brakeDelta = GetMaxBrakeDelta(recent);
        float maxYawRate = recent.Max(s => Math.Abs(s.YawRate));
        float avgSpeed = recent.Average(s => s.Speed);

        // ─ DRIVER TECHNIQUE: too much brake pressure too quickly
        if (brakeDelta > 0.5f && maxBrake > 0.8f)
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.8f;
        }

        // ─ SETUP: brake bias (front vs rear)
        if (maxYawRate > 0.3f && maxBrake > 0.7f)
        {
            c.CauseScores[ProblemCause.CarSetup] = 0.7f;
        }

        // ─ HARDWARE: brake gain too high
        if (brakeDelta > 0.4f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] = 0.75f;
        }

        // ─ TRACK: low grip (lockup at normal pressure)
        if (maxBrake < 0.6f && maxYawRate > 0.2f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.65f;
        }
    }

    private void ClassifyOversteerMidCorner(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length == 0) return;

        var recent = samples.TakeLast(20).ToArray();
        if (recent.Length == 0) return;

        float avgThrottle = recent.Average(s => s.Throttle);
        float avgSteering = recent.Average(s => Math.Abs(s.SteeringAngle));
        float maxYawRate = recent.Max(s => Math.Abs(s.YawRate));
        float avgLatAccel = recent.Average(s => Math.Abs(s.LatAccel));
        float avgRearTireTemp = GetAvgRearTireTemp(recent);

        // ─ DRIVER TECHNIQUE: lifting throttle mid-corner (unstable)
        if (avgThrottle < 0.3f && maxYawRate > 0.3f)
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.75f;
        }

        // ─ SETUP: rear too loose
        if (avgLatAccel > 1.2f && maxYawRate > 0.4f && avgThrottle > 0.5f)
        {
            c.CauseScores[ProblemCause.CarSetup] = 0.8f;
        }

        // ─ HARDWARE: FFB weak (driver unaware of yaw)
        if (maxYawRate > 0.5f && avgSteering < 0.4f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] = 0.65f;
        }

        // ─ TRACK: high track temp / low grip
        if (avgRearTireTemp > 105f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.7f;
        }
    }

    private void ClassifyUndersteerMidCorner(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length == 0) return;

        var recent = samples.TakeLast(20).ToArray();
        if (recent.Length == 0) return;

        float avgSpeed = recent.Average(s => s.Speed);
        float maxSteering = recent.Max(s => Math.Abs(s.SteeringAngle));
        float avgLatAccel = recent.Average(s => Math.Abs(s.LatAccel));
        float avgFrontTireTemp = GetAvgFrontTireTemp(recent);

        // ─ DRIVER TECHNIQUE: going too fast for current grip
        if (avgSpeed > 60f && maxSteering > 1.2f)
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.8f;
        }

        // ─ SETUP: front too tight, not enough downforce/wing
        if (maxSteering > 1.0f && avgLatAccel < 1.0f)
        {
            c.CauseScores[ProblemCause.CarSetup] = 0.8f;
        }

        // ─ HARDWARE: steering delayed (smoothing too high)
        if (maxSteering > 0.8f && avgLatAccel > 1.0f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] = 0.6f;
        }

        // ─ TRACK: front tires overheating / low grip
        if (avgFrontTireTemp > 100f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.7f;
        }
    }

    private void ClassifySnapOversteer(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length == 0) return;

        var recent = samples.TakeLast(20).ToArray();
        if (recent.Length == 0) return;

        float maxThrottleDelta = GetMaxThrottleAcceleration(recent);
        float maxYawRate = recent.Max(s => Math.Abs(s.YawRate));
        float avgLatAccel = recent.Average(s => Math.Abs(s.LatAccel));
        float avgRearTireTemp = GetAvgRearTireTemp(recent);

        // ─ DRIVER TECHNIQUE: aggressive throttle application
        if (maxThrottleDelta > 0.4f && maxYawRate > 0.5f)
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.85f;
        }

        // ─ SETUP: rear too soft (low damping/stiff)
        if (maxYawRate > 0.5f && avgLatAccel > 1.2f)
        {
            c.CauseScores[ProblemCause.CarSetup] = 0.75f;
        }

        // ─ HARDWARE: throttle curve too sharp
        if (maxThrottleDelta > 0.5f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] = 0.75f;
        }

        // ─ TRACK: rear tires at temperature limit
        if (avgRearTireTemp > 108f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.65f;
        }
    }

    private void ClassifyOffTrack(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length == 0) return;

        var recent = samples.TakeLast(20).ToArray();
        if (recent.Length == 0) return;

        float avgSpeed = recent.Average(s => s.Speed);
        float maxSteering = recent.Max(s => Math.Abs(s.SteeringAngle));
        float avgBrake = recent.Average(s => s.Brake);

        // Check event data for traffic proximity
        bool trafficiNear = eventData != null && eventData.ContainsKey("traffic_near") && (bool)eventData["traffic_near"];

        // ─ DRIVER TECHNIQUE: driver error, too much speed/steering
        if (maxSteering > 1.5f || avgSpeed > 70f)
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.85f;
        }

        // ─ TRAFFIC: forced off track by other car
        if (trafficiNear)
        {
            c.CauseScores[ProblemCause.TrafficSituation] = 0.9f;
        }

        // ─ TRACK: slippery surface section
        if (avgBrake > 0.5f && maxSteering > 1.2f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.7f;
        }
    }

    private void ClassifyInconsistentBraking(
        ProblemClassification c,
        TelemetrySample[] samples,
        Dictionary<string, object>? eventData)
    {
        if (samples == null || samples.Length < 10) return;

        var recent = samples.TakeLast(50).ToArray();
        if (recent.Length == 0) return;

        // Calculate brake input variance across multiple braking events
        float brakeVariance = CalculateVariance(recent.Select(s => s.Brake).ToList());
        float brakeDeltaVariance = CalculateVariance(
            GetBrakeDeltaSequence(recent).Select(d => Math.Abs(d)).ToList());

        // ─ DRIVER TECHNIQUE: inconsistent pedal inputs
        if (brakeVariance > 0.05f)
        {
            c.CauseScores[ProblemCause.DriverTechnique] = 0.75f;
        }

        // ─ HARDWARE: pedal calibration or feel issues
        if (brakeDeltaVariance > 0.1f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] = 0.7f;
        }

        // ─ TRACK: grip changes (temperature variation)
        if (brakeVariance > 0.08f)
        {
            c.CauseScores[ProblemCause.TrackCondition] = 0.65f;
        }
    }

    // ═══ CAUSE DETERMINATION ═══
    private void DeterminePrimaryCause(ProblemClassification c)
    {
        var sortedCauses = c.CauseScores
            .OrderByDescending(kv => kv.Value)
            .ToList();

        if (sortedCauses.Count > 0)
        {
            c.PrimaryCause = sortedCauses[0].Key;
            c.PrimaryCauseConfidence = sortedCauses[0].Value;
        }

        if (sortedCauses.Count > 1 && sortedCauses[1].Value > 0.4f)
        {
            c.SecondaryCause = sortedCauses[1].Key;
            c.SecondaryCauseConfidence = sortedCauses[1].Value;
        }

        // Generate explanation
        c.Explanation = ExplainClassification(c);
    }

    private void ApplyHardwareCrossReference(ProblemClassification c)
    {
        // If hardware coach has detected issues, weight hardware causes higher
        var hardwareScore = _hardwareCoach.CurrentScore;

        if (hardwareScore.BrakeSetupConfidence < 0.7f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] *= 1.3f;
        }
        if (hardwareScore.SteeringSetupConfidence < 0.7f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] *= 1.2f;
        }
        if (hardwareScore.PedalCalibrationConfidence < 0.7f)
        {
            c.CauseScores[ProblemCause.HardwareSetting] *= 1.2f;
        }

        // Re-determine causes after adjustment
        DeterminePrimaryCause(c);
    }

    private void GenerateRecommendation(ProblemClassification c)
    {
        c.Recommendation = c.PrimaryCause switch
        {
            ProblemCause.DriverTechnique =>
                "Review your inputs on telemetry. This is a driving technique issue. Practice smoother inputs and better brake/throttle application.",

            ProblemCause.CarSetup =>
                "Do not change hardware settings yet. This is a car setup issue. Try adjusting suspension, wings, or brake balance incrementally.",

            ProblemCause.HardwareSetting =>
                "Check your wheel/pedal settings. Adjust FFB, damping, brake gain, or throttle curve by small amounts (2-5%). Test for several laps before another change.",

            ProblemCause.TrackCondition =>
                "The track conditions may be limiting grip here. Monitor tire temperatures and adjust brake/throttle timing as grip improves.",

            ProblemCause.TrafficSituation =>
                "This was caused by proximity to other cars. Practice defensive driving or adjust your line to avoid traffic.",

            _ => "Unknown cause. Review telemetry data."
        };

        // Generate short voice version
        c.VoiceCall = c.PrimaryCause switch
        {
            ProblemCause.DriverTechnique => "That's a driving technique issue. Smooth your inputs.",
            ProblemCause.CarSetup => "Car setup issue. Adjust suspension or balance.",
            ProblemCause.HardwareSetting => "Check your hardware settings.",
            ProblemCause.TrackCondition => "Track conditions are limiting grip here.",
            ProblemCause.TrafficSituation => "You were impacted by traffic.",
            _ => "Unknown cause."
        };
    }

    private string ExplainClassification(ProblemClassification c)
    {
        return c.PrimaryCause switch
        {
            ProblemCause.DriverTechnique =>
                $"Likely driver input: {GetDetailedDrivingExplanation(c.Problem)}. Focus on technique.",

            ProblemCause.CarSetup =>
                $"Likely car setup: {GetDetailedSetupExplanation(c.Problem)}. Do not change hardware.",

            ProblemCause.HardwareSetting =>
                $"Likely hardware: {GetDetailedHardwareExplanation(c.Problem)}. Adjust settings carefully.",

            ProblemCause.TrackCondition =>
                "Likely track condition: Grip or temperature is limiting performance. Monitor and adapt.",

            ProblemCause.TrafficSituation =>
                "Likely traffic: Another car caused or contributed to this incident.",

            _ => "Unknown classification."
        };
    }

    private string GetDetailedDrivingExplanation(string problem)
    {
        return problem.ToLower() switch
        {
            "loose_on_exit" => "throttle applied too early with too much wheel",
            "tight_on_entry" => "entry speed too high or brake release timing off",
            "brake_lockup" => "brake pressure applied too aggressively",
            "oversteer_mid_corner" => "lifting throttle or turning in too late",
            "understeer_mid_corner" => "speed too high for available grip",
            "snap_oversteer" => "throttle application too aggressive after apex",
            "inconsistent_braking" => "brake inputs are inconsistent lap to lap",
            _ => "driving input issue"
        };
    }

    private string GetDetailedSetupExplanation(string problem)
    {
        return problem.ToLower() switch
        {
            "loose_on_exit" => "rear is unstable even with smooth throttle. Try rear stability.",
            "tight_on_entry" => "front understeer. Try more front downforce or adjust wing.",
            "brake_lockup" => "brake bias needs adjustment. Try moving forward or rearward.",
            "oversteer_mid_corner" => "rear too loose. Add rear stiffness or reduce downforce.",
            "understeer_mid_corner" => "front too tight. Reduce front wing or add front stiffness.",
            "snap_oversteer" => "rear damping too soft. Stiffen rear or add rear wing.",
            _ => "setup issue"
        };
    }

    private string GetDetailedHardwareExplanation(string problem)
    {
        return problem.ToLower() switch
        {
            "loose_on_exit" => "throttle curve too sharp. Soften the response.",
            "tight_on_entry" => "FFB too weak. Increase strength or detail.",
            "brake_lockup" => "brake gain too high. Lower the sensitivity.",
            "oversteer_mid_corner" => "FFB weak. Increase strength.",
            "understeer_mid_corner" => "steering delayed. Reduce smoothing.",
            "snap_oversteer" => "throttle curve too sharp. Soften or add throttle linearity.",
            "inconsistent_braking" => "pedal calibration issue. Recalibrate in iRacing.",
            _ => "hardware setting"
        };
    }

    // ═══ HELPERS ═══
    private float GetMaxThrottleAcceleration(TelemetrySample[] samples)
    {
        if (samples.Length < 2) return 0f;
        float maxDelta = 0f;
        for (int i = 1; i < samples.Length; i++)
        {
            maxDelta = Math.Max(maxDelta, Math.Abs(samples[i].Throttle - samples[i - 1].Throttle));
        }
        return maxDelta;
    }

    private float GetMaxBrakeDelta(TelemetrySample[] samples)
    {
        if (samples.Length < 2) return 0f;
        float maxDelta = 0f;
        for (int i = 1; i < samples.Length; i++)
        {
            maxDelta = Math.Max(maxDelta, Math.Abs(samples[i].Brake - samples[i - 1].Brake));
        }
        return maxDelta;
    }

    private List<float> GetBrakeDeltaSequence(TelemetrySample[] samples)
    {
        var deltas = new List<float>();
        for (int i = 1; i < samples.Length; i++)
        {
            deltas.Add(samples[i].Brake - samples[i - 1].Brake);
        }
        return deltas;
    }

    private float GetAvgFrontTireTemp(TelemetrySample[] samples)
    {
        float sum = 0f;
        int count = 0;
        foreach (var sample in samples)
        {
            if (sample.LFTireTemp != null && sample.LFTireTemp.Length > 0)
            {
                sum += sample.LFTireTemp[1];  // Middle (M) temp
                count++;
            }
            if (sample.RFTireTemp != null && sample.RFTireTemp.Length > 0)
            {
                sum += sample.RFTireTemp[1];
                count++;
            }
        }
        return count > 0 ? sum / count : 0f;
    }

    private float GetAvgRearTireTemp(TelemetrySample[] samples)
    {
        float sum = 0f;
        int count = 0;
        foreach (var sample in samples)
        {
            if (sample.LRTireTemp != null && sample.LRTireTemp.Length > 0)
            {
                sum += sample.LRTireTemp[1];  // Middle (M) temp
                count++;
            }
            if (sample.RRTireTemp != null && sample.RRTireTemp.Length > 0)
            {
                sum += sample.RRTireTemp[1];
                count++;
            }
        }
        return count > 0 ? sum / count : 0f;
    }

    private float CalculateVariance(List<float> values)
    {
        if (values.Count == 0) return 0f;
        float mean = values.Average();
        float sumSquaredDiff = values.Sum(x => (x - mean) * (x - mean));
        return sumSquaredDiff / values.Count;
    }
}
