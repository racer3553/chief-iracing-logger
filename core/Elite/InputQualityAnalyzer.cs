// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Input Quality Analyzer
// Scores driver input quality across brake, throttle, steering, and
// consistency dimensions. Updated every tick, finalized per lap.
// NOT an ICoachingModule — utility class for tracking input quality.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Input quality scores across all dimensions (0-100)</summary>
public class InputQualityScores
{
    /// <summary>Brake control smoothness: 0-100</summary>
    public int BrakeControl { get; set; }

    /// <summary>Throttle application smoothness: 0-100</summary>
    public int ThrottleControl { get; set; }

    /// <summary>Steering smoothness and correction count: 0-100</summary>
    public int SteeringSmoothness { get; set; }

    /// <summary>Lap-to-lap consistency in corner entry/apex/exit: 0-100</summary>
    public int CornerConsistency { get; set; }

    /// <summary>Racecraft discipline: consistency, incidents, off-tracks: 0-100</summary>
    public int RacecraftDiscipline { get; set; }

    /// <summary>Weighted average of all dimensions: 0-100</summary>
    public int OverallScore { get; set; }

    /// <summary>Name of weakest area</summary>
    public string WeakestArea { get; set; } = "";

    /// <summary>Name of strongest area</summary>
    public string StrongestArea { get; set; } = "";
}

/// <summary>
/// Analyzes driver input quality every tick and produces per-lap quality scores.
/// Tracks rolling buffers of last 5 laps and session average.
/// </summary>
public class InputQualityAnalyzer
{
    // Current lap telemetry buffer
    private Queue<TelemetrySample> _currentLapSamples = new();
    private const int MaxSamplesPerLap = 500; // ~25 seconds at 20Hz

    // Rolling lap history (last 5 laps)
    private Queue<InputQualityScores> _lapScores = new();
    private const int RollingLapCount = 5;

    // Session tracking
    private int _currentLap = 0;
    private List<InputQualityScores> _sessionScores = new();

    public InputQualityAnalyzer()
    {
    }

    /// <summary>
    /// Process a telemetry sample (called every tick).
    /// </summary>
    public void ProcessSample(TelemetrySample sample)
    {
        if (sample == null)
            return;

        // Detect lap transition
        if (sample.Lap != _currentLap)
        {
            if (_currentLap > 0)
            {
                OnLapCompleted();
            }
            _currentLap = sample.Lap;
            _currentLapSamples.Clear();
        }

        // Add sample to buffer
        _currentLapSamples.Enqueue(sample);
        if (_currentLapSamples.Count > MaxSamplesPerLap)
        {
            _currentLapSamples.Dequeue();
        }
    }

    /// <summary>
    /// Called when a lap is completed. Finalizes lap scores.
    /// </summary>
    private void OnLapCompleted()
    {
        if (_currentLapSamples.Count < 10)
            return; // Not enough data

        var scores = new InputQualityScores();

        // Calculate each dimension
        scores.BrakeControl = CalculateBrakeControl(_currentLapSamples.ToList());
        scores.ThrottleControl = CalculateThrottleControl(_currentLapSamples.ToList());
        scores.SteeringSmoothness = CalculateSteeringSmoothness(_currentLapSamples.ToList());
        scores.CornerConsistency = CalculateCornerConsistency(_currentLapSamples.ToList());
        scores.RacecraftDiscipline = CalculateRacecraftDiscipline(_currentLapSamples.ToList());

        // Calculate weighted overall score
        scores.OverallScore = (int)Math.Round(
            (scores.BrakeControl * 0.2 +
             scores.ThrottleControl * 0.2 +
             scores.SteeringSmoothness * 0.15 +
             scores.CornerConsistency * 0.25 +
             scores.RacecraftDiscipline * 0.2));

        // Identify strongest and weakest
        var byScore = new Dictionary<string, int>
        {
            ["Brake"] = scores.BrakeControl,
            ["Throttle"] = scores.ThrottleControl,
            ["Steering"] = scores.SteeringSmoothness,
            ["Consistency"] = scores.CornerConsistency,
            ["Racecraft"] = scores.RacecraftDiscipline
        };

        scores.StrongestArea = byScore.OrderByDescending(kvp => kvp.Value).First().Key;
        scores.WeakestArea = byScore.OrderBy(kvp => kvp.Value).First().Key;

        // Add to rolling history
        _lapScores.Enqueue(scores);
        if (_lapScores.Count > RollingLapCount)
        {
            _lapScores.Dequeue();
        }

        // Add to session history
        _sessionScores.Add(scores);
    }

    /// <summary>
    /// Get current lap scores (based on rolling history).
    /// </summary>
    public InputQualityScores GetCurrentScores()
    {
        if (_lapScores.Count == 0)
        {
            return new InputQualityScores { OverallScore = 0 };
        }

        // Return the most recent lap score
        return _lapScores.Last();
    }

    /// <summary>
    /// Get session average scores (all laps in this session).
    /// </summary>
    public InputQualityScores GetSessionAverageScores()
    {
        if (_sessionScores.Count == 0)
        {
            return new InputQualityScores { OverallScore = 0 };
        }

        var avg = new InputQualityScores
        {
            BrakeControl = (int)Math.Round(_sessionScores.Average(s => s.BrakeControl)),
            ThrottleControl = (int)Math.Round(_sessionScores.Average(s => s.ThrottleControl)),
            SteeringSmoothness = (int)Math.Round(_sessionScores.Average(s => s.SteeringSmoothness)),
            CornerConsistency = (int)Math.Round(_sessionScores.Average(s => s.CornerConsistency)),
            RacecraftDiscipline = (int)Math.Round(_sessionScores.Average(s => s.RacecraftDiscipline))
        };

        avg.OverallScore = (int)Math.Round(
            (avg.BrakeControl * 0.2 +
             avg.ThrottleControl * 0.2 +
             avg.SteeringSmoothness * 0.15 +
             avg.CornerConsistency * 0.25 +
             avg.RacecraftDiscipline * 0.2));

        var byScore = new Dictionary<string, int>
        {
            ["Brake"] = avg.BrakeControl,
            ["Throttle"] = avg.ThrottleControl,
            ["Steering"] = avg.SteeringSmoothness,
            ["Consistency"] = avg.CornerConsistency,
            ["Racecraft"] = avg.RacecraftDiscipline
        };

        avg.StrongestArea = byScore.OrderByDescending(kvp => kvp.Value).First().Key;
        avg.WeakestArea = byScore.OrderBy(kvp => kvp.Value).First().Key;

        return avg;
    }

    // ═══════════════════════════════════════════════════════════════
    // DIMENSION CALCULATORS
    // ═══════════════════════════════════════════════════════════════

    private int CalculateBrakeControl(List<TelemetrySample> samples)
    {
        if (samples.Count < 2) return 50;

        // Brake spike score: penalty for jumps > 0.4/sample
        float spikeScore = 100f;
        var brakeSamples = samples.Where(s => s.Brake > 0).ToList();
        if (brakeSamples.Count > 1)
        {
            int spikeCount = 0;
            for (int i = 1; i < brakeSamples.Count; i++)
            {
                float delta = Math.Abs(brakeSamples[i].Brake - brakeSamples[i - 1].Brake);
                if (delta > 0.4f) spikeCount++;
            }
            spikeScore = Math.Max(0, 100f - (spikeCount * 5f));
        }

        // Brake release smoothness
        float releaseScore = 100f;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Brake < samples[i - 1].Brake && samples[i - 1].Brake > 0.2f)
            {
                float releaseRate = samples[i - 1].Brake - samples[i].Brake;
                if (releaseRate > 0.3f)
                    releaseScore -= 5f; // Abrupt release penalty
            }
        }
        releaseScore = Math.Max(0, releaseScore);

        // Trail brake quality: smooth brake decrease while steering increases near turn-in
        float trailBrakeScore = 100f;
        // (Simple heuristic: overlapping brake and steering)
        int overlapSamples = samples.Count(s => s.Brake > 0.1f && Math.Abs(s.SteeringAngle) > 0.2f);
        if (overlapSamples > 0)
            trailBrakeScore = 90f; // Good trail braking detected

        // Weight: 40% spike, 30% release, 30% trail brake
        return (int)Math.Round(spikeScore * 0.4f + releaseScore * 0.3f + trailBrakeScore * 0.3f);
    }

    private int CalculateThrottleControl(List<TelemetrySample> samples)
    {
        if (samples.Count < 2) return 50;

        // Throttle ramp smoothness on exit
        float rampScore = 100f;
        for (int i = 1; i < samples.Count; i++)
        {
            float delta = Math.Abs(samples[i].Throttle - samples[i - 1].Throttle);
            if (delta > 0.3f && samples[i].Throttle > 0.2f)
            {
                rampScore -= 3f;
            }
        }
        rampScore = Math.Max(0, rampScore);

        // Throttle modulation: ability to hold partial throttle (not binary on/off)
        float modulationScore = 100f;
        var throttleLevels = samples.Where(s => s.Throttle > 0).Select(s => s.Throttle).ToList();
        float throttleVariance = throttleLevels.Count > 1 ?
            (float)Math.Sqrt(throttleLevels.Sum(t => Math.Pow(t - throttleLevels.Average(), 2)) / throttleLevels.Count) : 0;
        // Higher variance = better modulation (not always full throttle)
        modulationScore = Math.Min(100f, throttleVariance * 200f);

        // Brake/throttle overlap: penalty for both > 0.1
        float noOverlapScore = 100f;
        int overlapCount = samples.Count(s => s.Brake > 0.1f && s.Throttle > 0.1f);
        if (overlapCount > 5)
            noOverlapScore = Math.Max(30f, 100f - (overlapCount * 2f));

        // Weight: 50% ramp, 30% modulation, 20% no-overlap
        return (int)Math.Round(rampScore * 0.5f + modulationScore * 0.3f + noOverlapScore * 0.2f);
    }

    private int CalculateSteeringSmoothness(List<TelemetrySample> samples)
    {
        if (samples.Count < 2) return 50;

        // Steering noise: high-freq corrections (count rapid reversals per second)
        float noiseScore = 100f;
        int corrections = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            float delta = Math.Abs(samples[i].SteeringAngle - samples[i - 1].SteeringAngle);
            if (delta > 0.1f && (i > 1 ? Math.Sign(samples[i].SteeringAngle - samples[i - 1].SteeringAngle) !=
                Math.Sign(samples[i - 1].SteeringAngle - samples[i - 2].SteeringAngle) : false))
            {
                corrections++;
            }
        }
        noiseScore = Math.Max(0, 100f - (corrections * 2f));

        // Single-input quality: one smooth arc per corner
        float singleInputScore = noiseScore; // Correlated

        // Weight: 40% noise, 40% corrections, 20% smoothness
        return (int)Math.Round(noiseScore * 0.6f + singleInputScore * 0.4f);
    }

    private int CalculateCornerConsistency(List<TelemetrySample> samples)
    {
        // This requires historical corner data which we don't have in this lap
        // In a real implementation, compare entry/apex/exit speeds across laps
        // For now, return a neutral score
        return 75;
    }

    private int CalculateRacecraftDiscipline(List<TelemetrySample> samples)
    {
        if (samples.Count == 0) return 50;

        // Lap-to-lap consistency in lap times (would need historical data)
        // For now: off-track rate and incident rate

        // Off-track detection: position on track beyond normal bounds
        // (Simplified: if speed drops suddenly, assume off-track)
        int offTrackCount = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].Speed < samples[i - 1].Speed * 0.7f && samples[i - 1].Speed > 50)
            {
                offTrackCount++;
            }
        }

        float offTrackScore = Math.Max(0, 100f - (offTrackCount * 5f));

        // Incident check: yaw rate spikes
        int incidents = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].YawRate > 3.0f)
                incidents++;
        }

        float incidentScore = Math.Max(0, 100f - (incidents * 2f));

        // Average
        return (int)Math.Round((offTrackScore + incidentScore) / 2f);
    }
}
