// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Mental Reset Brain
// Detects driver spiraling and provides mental reset coaching.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Mental state coaching module. Detects signs of driver panic, overdriving,
/// inconsistency, and provides psychological reset cues to refocus attention.
/// </summary>
public class MentalResetBrain : ICoachingModule
{
    private readonly CoachingPrioritySystem _priority;
    private bool _isEnabled = true;
    private int _consecutiveOffTracks = 0;
    private int _missedBrakingCount = 0;
    private Queue<int> _recentMissedBraking = new();
    private List<float> _recentLapTimes = new();
    private int _steeringPanicCount = 0;
    private int _brakePanicCount = 0;
    private float _lastMentalReset = -3.5f;
    private float _overdrivingScore = 0f;

    public string ModuleName => "MentalResetBrain";
    public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }

    public MentalResetBrain(CoachingPrioritySystem priority)
    {
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
    }

    public void ProcessTick(EliteCoachingContext context)
    {
        if (!IsEnabled || context?.CurrentSample == null)
            return;

        var sample = context.CurrentSample;
        var prevSample = context.PreviousSample;

        if (prevSample == null)
            return;

        // Detect steering panic: rapid large corrections
        DetectSteeringPanic(sample, prevSample);

        // Detect brake panic: sudden slam from nothing
        DetectBrakePanic(sample, prevSample);

        // Detect overdriving: creeping entry speeds after mistakes
        DetectOverdriving(context, sample);
    }

    public void OnLapCompleted(EliteCoachingContext context, int lap)
    {
        if (!IsEnabled || lap < 1)
            return;

        // Track recent lap times
        _recentLapTimes.Add(context.LastLapTime);
        if (_recentLapTimes.Count > 10)
            _recentLapTimes.RemoveAt(0);

        // Check if we can give mental reset
        float currentTime = context.CurrentSample?.LapCurrentLapTime ?? 0;
        bool allowReset = (currentTime - _lastMentalReset) >= 3.5f;

        if (!allowReset)
            return;

        // Evaluate mental state
        float lapTimeVariance = CalculateLapTimeVariance();
        bool isInconsistent = lapTimeVariance > 0.5f;
        bool isGettingWorse = IsLapTimesDeteriorating();

        // Mental reset conditions
        if (_consecutiveOffTracks >= 2)
        {
            SubmitDecision(context, "mental", "Off-Track Series",
                "Reset. Breathe. One clean corner.", CoachingPriority.Medium, 75);
            _consecutiveOffTracks = 0;
            _lastMentalReset = currentTime;
        }
        else if (isGettingWorse && _recentLapTimes.Count >= 3)
        {
            SubmitDecision(context, "mental", "Deteriorating Performance",
                "Stop chasing time. Build rhythm.", CoachingPriority.Medium, 75);
            _lastMentalReset = currentTime;
        }
        else if (_steeringPanicCount >= 3)
        {
            SubmitDecision(context, "mental", "Steering Panic",
                "Relax hands. Hit markers.", CoachingPriority.Medium, 75);
            _steeringPanicCount = 0;
            _lastMentalReset = currentTime;
        }
        else if (_brakePanicCount >= 2)
        {
            SubmitDecision(context, "mental", "Brake Panic",
                "Smooth inputs. Plan ahead.", CoachingPriority.Medium, 75);
            _brakePanicCount = 0;
            _lastMentalReset = currentTime;
        }
        else if (_overdrivingScore > 0.7f && lap > 2)
        {
            SubmitDecision(context, "mental", "Overdriving",
                "Slow down first. Build back up.", CoachingPriority.Medium, 75);
            _overdrivingScore = 0f;
            _lastMentalReset = currentTime;
        }
        else if (isInconsistent)
        {
            SubmitDecision(context, "mental", "Inconsistency",
                "Simplify. Focus on braking points only.", CoachingPriority.Medium, 75);
            _lastMentalReset = currentTime;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE DETECTION METHODS
    // ═══════════════════════════════════════════════════════════════

    private void DetectSteeringPanic(TelemetrySample current, TelemetrySample previous)
    {
        // Rapid steering input: > 1.0 radian change in < 0.2 seconds (at 60 Hz = 12 ticks)
        // At 60 Hz, one tick is ~16ms, so 3-4 ticks ≈ 50-60ms
        // We'll check single-tick for approximation
        float steeringDelta = Math.Abs(current.SteeringAngle - previous.SteeringAngle);

        if (steeringDelta > 0.3f)  // Large steering change in one tick
        {
            _steeringPanicCount++;
        }
        else if (_steeringPanicCount > 0)
        {
            _steeringPanicCount = Math.Max(0, _steeringPanicCount - 1);
        }
    }

    private void DetectBrakePanic(TelemetrySample current, TelemetrySample previous)
    {
        // Brake slam: from < 0.1 to > 0.9 in one tick
        if (previous.Brake < 0.1f && current.Brake > 0.9f)
        {
            _brakePanicCount++;
        }
        else if (_brakePanicCount > 0 && current.Brake < 0.5f)
        {
            _brakePanicCount = Math.Max(0, _brakePanicCount - 1);
        }
    }

    private void DetectOverdriving(EliteCoachingContext context, TelemetrySample sample)
    {
        // Track if entry speeds are increasing after a bad lap
        if (_recentLapTimes.Count >= 2)
        {
            float lastLapTime = _recentLapTimes[_recentLapTimes.Count - 1];
            float prevLapTime = _recentLapTimes[_recentLapTimes.Count - 2];

            // If last lap was slower, driver might be overdriving to compensate
            if (lastLapTime > prevLapTime + 0.3f)
            {
                // Now check if steering angle is creeping up
                _overdrivingScore = Math.Min(1.0f, _overdrivingScore + 0.15f);
            }
            else
            {
                _overdrivingScore = Math.Max(0f, _overdrivingScore - 0.05f);
            }
        }
    }

    private float CalculateLapTimeVariance()
    {
        if (_recentLapTimes.Count < 3)
            return 0f;

        // Calculate variance of last 5 laps
        var recentTimes = _recentLapTimes.Skip(Math.Max(0, _recentLapTimes.Count - 5)).ToList();
        if (recentTimes.Count == 0)
            return 0f;

        float mean = recentTimes.Average();
        float variance = recentTimes.Select(t => (t - mean) * (t - mean)).Average();
        return (float)Math.Sqrt(variance);
    }

    private bool IsLapTimesDeteriorating()
    {
        if (_recentLapTimes.Count < 4)
            return false;

        // Check if last 3 laps are getting progressively slower
        var last3 = _recentLapTimes.Skip(Math.Max(0, _recentLapTimes.Count - 3)).ToList();
        return last3[0] < last3[1] && last3[1] < last3[2];
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
}
