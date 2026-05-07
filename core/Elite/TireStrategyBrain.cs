// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Tire Strategy Brain
// Detects tire abuse, degradation, and strategy issues in real-time.
// Submits coaching decisions for tire management.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Tire management coaching module. Monitors tire temperatures, wear,
/// and driving inputs to detect abuse patterns and recommend corrections.
/// </summary>
public class TireStrategyBrain : ICoachingModule
{
    private readonly CoachingPrioritySystem _priority;
    private Dictionary<int, TireState> _lapTireStates = new();
    private int _lastWarnLap = -3;
    private bool _isEnabled = true;

    public string ModuleName => "TireStrategyBrain";
    public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }

    public TireStrategyBrain(CoachingPrioritySystem priority)
    {
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
    }

    public void ProcessTick(EliteCoachingContext context)
    {
        if (!IsEnabled || context?.CurrentSample == null)
            return;

        var sample = context.CurrentSample;
        int lap = context.CurrentLap;

        // Track max tire temps this lap
        if (!_lapTireStates.ContainsKey(lap))
        {
            _lapTireStates[lap] = new TireState();
        }

        var tireState = _lapTireStates[lap];
        UpdateTireMaxTemps(tireState, sample);

        // Detect real-time issues
        DetectRFOverheating(context, tireState);
        DetectRearOverheating(context, tireState);
        DetectEntryAbuse(context, sample);
        DetectWheelspin(context, sample);
        DetectSteeringAbuse(context, sample);
        DetectSliding(context, sample);
    }

    public void OnLapCompleted(EliteCoachingContext context, int lap)
    {
        if (!IsEnabled || lap < 1)
            return;

        // Get the tire state from the completed lap
        if (!_lapTireStates.TryGetValue(lap, out var tireState))
            return;

        // Analyze tire state
        bool allowWarn = (lap - _lastWarnLap) >= 3;
        if (!allowWarn)
            return;

        float rfAvgTemp = GetAverageTemp(tireState.RFMaxTemps);
        float lfAvgTemp = GetAverageTemp(tireState.LFMaxTemps);
        float rearAvgTemp = (GetAverageTemp(tireState.LRMaxTemps) + GetAverageTemp(tireState.RRMaxTemps)) / 2f;
        float frontAvgTemp = (lfAvgTemp + rfAvgTemp) / 2f;

        // Check for tire fade pattern
        bool lapTimesIncreasing = CheckLapTimeFade(context);

        if (rfAvgTemp > lfAvgTemp + 15f)
        {
            SubmitDecision(context, "tire_management", "RF Overheating",
                "Save right front. Slow entry, less wheel.", CoachingPriority.Low, 65);
            _lastWarnLap = lap;
        }
        else if (rearAvgTemp > frontAvgTemp + 20f)
        {
            SubmitDecision(context, "tire_management", "Rear Overheating",
                "Rears are hot. Roll throttle on exit.", CoachingPriority.Low, 65);
            _lastWarnLap = lap;
        }
        else if (lapTimesIncreasing && tireState.AvgTireTemp > 70f)
        {
            SubmitDecision(context, "tire_management", "Tire Fade",
                "Tires are fading. Manage inputs.", CoachingPriority.Low, 65);
            _lastWarnLap = lap;
        }
        else if (tireState.AvgTireTemp < 50f && lap > 2)
        {
            SubmitDecision(context, "tire_management", "Tires Cold",
                "You can push now. Tires are good.", CoachingPriority.Low, 60);
            _lastWarnLap = lap;
        }

        // Cleanup old lap states
        if (_lapTireStates.Count > 20)
        {
            var oldLaps = _lapTireStates.Keys.Where(l => l < lap - 10).ToList();
            foreach (var oldLap in oldLaps)
            {
                _lapTireStates.Remove(oldLap);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE METHODS
    // ═══════════════════════════════════════════════════════════════

    private void UpdateTireMaxTemps(TireState state, TelemetrySample sample)
    {
        if (sample.LFTireTemp != null && sample.LFTireTemp.Length >= 3)
        {
            state.LFMaxTemps[0] = Math.Max(state.LFMaxTemps[0], sample.LFTireTemp[0]); // Left
            state.LFMaxTemps[1] = Math.Max(state.LFMaxTemps[1], sample.LFTireTemp[1]); // Middle
            state.LFMaxTemps[2] = Math.Max(state.LFMaxTemps[2], sample.LFTireTemp[2]); // Right
        }

        if (sample.RFTireTemp != null && sample.RFTireTemp.Length >= 3)
        {
            state.RFMaxTemps[0] = Math.Max(state.RFMaxTemps[0], sample.RFTireTemp[0]);
            state.RFMaxTemps[1] = Math.Max(state.RFMaxTemps[1], sample.RFTireTemp[1]);
            state.RFMaxTemps[2] = Math.Max(state.RFMaxTemps[2], sample.RFTireTemp[2]);
        }

        if (sample.LRTireTemp != null && sample.LRTireTemp.Length >= 3)
        {
            state.LRMaxTemps[0] = Math.Max(state.LRMaxTemps[0], sample.LRTireTemp[0]);
            state.LRMaxTemps[1] = Math.Max(state.LRMaxTemps[1], sample.LRTireTemp[1]);
            state.LRMaxTemps[2] = Math.Max(state.LRMaxTemps[2], sample.LRTireTemp[2]);
        }

        if (sample.RRTireTemp != null && sample.RRTireTemp.Length >= 3)
        {
            state.RRMaxTemps[0] = Math.Max(state.RRMaxTemps[0], sample.RRTireTemp[0]);
            state.RRMaxTemps[1] = Math.Max(state.RRMaxTemps[1], sample.RRTireTemp[1]);
            state.RRMaxTemps[2] = Math.Max(state.RRMaxTemps[2], sample.RRTireTemp[2]);
        }

        state.SampleCount++;
    }

    private void DetectRFOverheating(EliteCoachingContext context, TireState state)
    {
        if (state.RFOverheatWarned)
            return;

        float rfAvg = GetAverageTemp(state.RFMaxTemps);
        float lfAvg = GetAverageTemp(state.LFMaxTemps);

        if (rfAvg > lfAvg + 15f && rfAvg > 85f)
        {
            SubmitDecision(context, "tire_management", "RF Overheat",
                "Save right front. Less wheel.", CoachingPriority.Low, 65);
            state.RFOverheatWarned = true;
        }
    }

    private void DetectRearOverheating(EliteCoachingContext context, TireState state)
    {
        if (state.RearOverheatWarned)
            return;

        float rearAvg = (GetAverageTemp(state.LRMaxTemps) + GetAverageTemp(state.RRMaxTemps)) / 2f;
        float frontAvg = (GetAverageTemp(state.LFMaxTemps) + GetAverageTemp(state.RFMaxTemps)) / 2f;

        if (rearAvg > frontAvg + 20f && rearAvg > 90f)
        {
            SubmitDecision(context, "tire_management", "Rear Overheat",
                "Rears are hot. Roll throttle on exit.", CoachingPriority.Low, 65);
            state.RearOverheatWarned = true;
        }
    }

    private void DetectEntryAbuse(EliteCoachingContext context, TelemetrySample sample)
    {
        // High steering variance at turn-in = entry abuse
        if (!context.IsOnStraight && Math.Abs(sample.SteeringAngle) > 0.5f)
        {
            float frontTempVariance = GetTempVariance(
                Math.Max(sample.LFTireTemp?[0] ?? 0, sample.LFTireTemp?[2] ?? 0),
                sample.LFTireTemp?[1] ?? 0
            );

            if (frontTempVariance > 15f)
            {
                SubmitDecision(context, "tire_management", "Entry Scrub",
                    "Stop abusing entry. You're killing the run.", CoachingPriority.Low, 65);
            }
        }
    }

    private void DetectWheelspin(EliteCoachingContext context, TelemetrySample sample)
    {
        // Sudden rear temp spike with high throttle = wheelspin
        if (sample.Throttle > 0.7f && sample.Speed > 20f)
        {
            float rearTempAvg = (
                (sample.RRTireTemp?[1] ?? 0) + (sample.LRTireTemp?[1] ?? 0)
            ) / 2f;

            if (rearTempAvg > 100f)
            {
                SubmitDecision(context, "tire_management", "Wheelspin",
                    "Wheelspin on exit. Patience.", CoachingPriority.Low, 65);
            }
        }
    }

    private void DetectSteeringAbuse(EliteCoachingContext context, TelemetrySample sample)
    {
        // Excessive steering angle with high lateral G = scrub
        if (Math.Abs(sample.SteeringAngle) > 1.0f && Math.Abs(sample.LatAccel) > 1.5f)
        {
            SubmitDecision(context, "tire_management", "Steering Scrub",
                "Quiet hands. Less wheel.", CoachingPriority.Low, 62);
        }
    }

    private void DetectSliding(EliteCoachingContext context, TelemetrySample sample)
    {
        // High yaw rate relative to steering = sliding/drifting
        if (Math.Abs(sample.YawRate) > 0.5f && Math.Abs(sample.SteeringAngle) > 0.3f)
        {
            float yawToSteeringRatio = Math.Abs(sample.YawRate) / (Math.Abs(sample.SteeringAngle) + 0.01f);
            if (yawToSteeringRatio > 2.0f)
            {
                SubmitDecision(context, "tire_management", "Sliding",
                    "Let it rotate. Smooth arc.", CoachingPriority.Low, 65);
            }
        }
    }

    private bool CheckLapTimeFade(EliteCoachingContext context)
    {
        // Simple check: if last lap was slower and tires are hot
        if (context.LastLapTime > 0 && context.BestLapTime > 0)
        {
            float delta = context.LastLapTime - context.BestLapTime;
            return delta > 0.5f; // More than 0.5s slower than best = fading
        }
        return false;
    }

    private float GetAverageTemp(float[] temps)
    {
        if (temps == null || temps.Length == 0)
            return 0f;
        return temps.Average();
    }

    private float GetTempVariance(float tempLeft, float tempMid)
    {
        return Math.Abs(tempLeft - tempMid);
    }

    private void SubmitDecision(EliteCoachingContext context, string category, string message,
        string voiceText, CoachingPriority priority, int confidence)
    {
        var decision = new CoachingDecision
        {
            Car = context.Car,
            Track = context.Track,
            CornerName = context.CurrentCorner?.Name ?? "Unknown",
            CornerNumber = context.CurrentCorner?.Number ?? 0,
            LapNumber = context.CurrentLap,
            LapDistPct = context.CurrentSample?.LapDistPct ?? 0f,
            SessionType = context.SessionType,
            Category = category,
            Message = message,
            VoiceText = voiceText,
            Priority = priority,
            ConfidenceScore = confidence,
            SpeakBeforeCornerSeconds = 0,
            SessionId = context.SessionId,
        };

        _priority.Submit(decision);
    }

    // ═══════════════════════════════════════════════════════════════
    // TIRE STATE TRACKER
    // ═══════════════════════════════════════════════════════════════

    private class TireState
    {
        public float[] LFMaxTemps { get; } = { 0, 0, 0 };
        public float[] RFMaxTemps { get; } = { 0, 0, 0 };
        public float[] LRMaxTemps { get; } = { 0, 0, 0 };
        public float[] RRMaxTemps { get; } = { 0, 0, 0 };
        public int SampleCount { get; set; }
        public bool RFOverheatWarned { get; set; }
        public bool RearOverheatWarned { get; set; }

        public float AvgTireTemp => (
            (LFMaxTemps[1] + RFMaxTemps[1] + LRMaxTemps[1] + RRMaxTemps[1]) / 4f
        );
    }
}
