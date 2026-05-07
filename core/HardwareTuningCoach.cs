// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Hardware Tuning Coach
// Monitors input traces and detects hardware/settings issues
// vs setup vs driving problems. Provides incremental tuning advice.
// ═══════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using System;

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// HARDWARE PROFILE
// ═══════════════════════════════════════

public class HardwareProfile
{
    public string WheelbaseType { get; set; } = "Simucube 2 Pro";
    public string PedalType { get; set; } = "Simagic P2000";
    public float FfbStrength { get; set; } = 100;
    public float Damping { get; set; } = 10;
    public float Smoothing { get; set; } = 0;
    public float Friction { get; set; } = 0;
    public float Inertia { get; set; } = 0;
    public float WheelRotation { get; set; } = 900;
    public float BrakeForce { get; set; } = 100;
    public float ThrottleCurve { get; set; } = 1.0f; // linearity
}

// ═══════════════════════════════════════
// HARDWARE DIAGNOSIS
// ═══════════════════════════════════════

public class HardwareDiagnosis
{
    public string DiagnosisId { get; set; } = "";
    public string Category { get; set; } = "";       // brake, steering, ffb, throttle, pedal_calibration
    public string Problem { get; set; } = "";         // Short description
    public string Recommendation { get; set; } = "";  // What to change
    public string VoiceCall { get; set; } = "";        // Short voice version
    public float Confidence { get; set; }              // 0.0 - 1.0
    public string Severity { get; set; } = "info";    // info, warning, critical
    public int SamplesAnalyzed { get; set; }
    public DateTime DetectedAt { get; set; }
}

public class HardwareDiagnosisScore
{
    public float BrakeSetupConfidence { get; set; } = 1.0f;    // 1.0 = good, 0.0 = bad
    public float SteeringSetupConfidence { get; set; } = 1.0f;
    public float FfbSetupConfidence { get; set; } = 1.0f;
    public float PedalCalibrationConfidence { get; set; } = 1.0f;
    public float DriverTechniqueConfidence { get; set; } = 1.0f;
    public List<HardwareDiagnosis> ActiveDiagnoses { get; set; } = new();
}

// ═══════════════════════════════════════
// HARDWARE TUNING COACH
// ═══════════════════════════════════════

public class HardwareTuningCoach
{
    private readonly EventHooks _eventHooks;
    private readonly VoiceEngine _voiceEngine;
    private readonly TalkTimingSystem _talkTiming;

    // Rolling buffers (last 200 samples)
    private readonly Queue<float> _brakeHistory = new();
    private readonly Queue<float> _throttleHistory = new();
    private readonly Queue<float> _steeringHistory = new();
    private readonly Queue<float> _steeringRateHistory = new();
    private readonly Queue<float> _yawRateHistory = new();
    private readonly Queue<float> _latAccelHistory = new();

    // Per-lap counters
    private int _brakeLockupCount = 0;
    private int _throttleSpikeCount = 0;
    private int _steeringCorrectionCount = 0;
    private int _brakeMaxReached = 0;
    private int _throttleMaxReached = 0;
    private int _frontLockupCount = 0;
    private int _rearYawSpikeCount = 0;

    // State tracking
    private float _prevBrake = 0f;
    private float _prevThrottle = 0f;
    private float _prevSteering = 0f;
    private float _prevYawRate = 0f;
    private int _samplesInCurrentLap = 0;
    private int _currentLap = 0;
    private long _lastAnalysisTickMs = 0;
    private bool _hasGivenAdviceThisRun = false;

    public HardwareProfile CurrentProfile { get; set; } = new();
    public HardwareDiagnosisScore CurrentScore { get; private set; } = new();
    public List<HardwareDiagnosis> SessionDiagnoses { get; } = new();

    public event Action<HardwareDiagnosis>? OnDiagnosisDetected;

    // ═══ CONSTRUCTOR ═══
    public HardwareTuningCoach(EventHooks eventHooks, VoiceEngine voiceEngine, TalkTimingSystem talkTiming)
    {
        _eventHooks = eventHooks ?? throw new ArgumentNullException(nameof(eventHooks));
        _voiceEngine = voiceEngine ?? throw new ArgumentNullException(nameof(voiceEngine));
        _talkTiming = talkTiming ?? throw new ArgumentNullException(nameof(talkTiming));
    }

    // ═══ MAIN TELEMETRY ANALYSIS ═══
    public void AnalyzeSample(TelemetrySample sample)
    {
        if (sample == null) return;

        // Track lap transition
        if (sample.Lap != _currentLap)
        {
            if (_currentLap > 0)
            {
                OnLapCompleted(_currentLap);
            }
            _currentLap = sample.Lap;
            _samplesInCurrentLap = 0;
            ResetLapCounters();
        }

        // Update rolling buffers (keep last 200 samples = ~10 seconds at 20Hz)
        const int bufferSize = 200;

        _brakeHistory.Enqueue(sample.Brake);
        if (_brakeHistory.Count > bufferSize) _brakeHistory.Dequeue();

        _throttleHistory.Enqueue(sample.Throttle);
        if (_throttleHistory.Count > bufferSize) _throttleHistory.Dequeue();

        _steeringHistory.Enqueue(sample.SteeringAngle);
        if (_steeringHistory.Count > bufferSize) _steeringHistory.Dequeue();

        _steeringRateHistory.Enqueue(Math.Abs(sample.SteeringAngle - _prevSteering));
        if (_steeringRateHistory.Count > bufferSize) _steeringRateHistory.Dequeue();

        _yawRateHistory.Enqueue(Math.Abs(sample.YawRate));
        if (_yawRateHistory.Count > bufferSize) _yawRateHistory.Dequeue();

        _latAccelHistory.Enqueue(Math.Abs(sample.LatAccel));
        if (_latAccelHistory.Count > bufferSize) _latAccelHistory.Dequeue();

        // Detect patterns
        DetectBrakeLockup(sample);
        DetectThrottleSpike(sample);
        DetectSteeringCorrection(sample);
        DetectBrakeExtremum(sample);
        DetectThrottleExtremum(sample);

        // Run full analysis every 100 samples (~5 seconds at 20Hz)
        _samplesInCurrentLap++;
        if (_samplesInCurrentLap % 100 == 0)
        {
            RunDetectionRules();
        }

        // Update state
        _prevBrake = sample.Brake;
        _prevThrottle = sample.Throttle;
        _prevSteering = sample.SteeringAngle;
        _prevYawRate = sample.YawRate;
        _lastAnalysisTickMs = sample.TimestampMs;
    }

    // ═══ PATTERN DETECTION ═══
    private void DetectBrakeLockup(TelemetrySample sample)
    {
        // Lockup = high brake AND wheel speed drops significantly (yaw spike)
        // Proxy: brake > 0.7 AND yaw rate increases rapidly
        if (sample.Brake > 0.7f && Math.Abs(sample.YawRate) > 0.5f && Math.Abs(_prevYawRate) < 0.3f)
        {
            _brakeLockupCount++;
        }
    }

    private void DetectThrottleSpike(TelemetrySample sample)
    {
        // Spike = throttle rise > 0.5 in < 0.1s = delta > 0.5 in one sample (assuming 20Hz = 0.05s)
        float deltaBrake = Math.Abs(sample.Throttle - _prevThrottle);
        if (deltaBrake > 0.25f)  // 0.25 per 50ms = 0.5 per 100ms
        {
            _throttleSpikeCount++;
        }
    }

    private void DetectSteeringCorrection(TelemetrySample sample)
    {
        // Correction = steering reversal (sign change in delta) with magnitude
        float delta = sample.SteeringAngle - _prevSteering;
        float absDelta = Math.Abs(delta);

        if (absDelta > 0.1f)  // Significant change (radians)
        {
            _steeringCorrectionCount++;
        }
    }

    private void DetectBrakeExtremum(TelemetrySample sample)
    {
        if (sample.Brake > 0.98f)
            _brakeMaxReached++;
    }

    private void DetectThrottleExtremum(TelemetrySample sample)
    {
        if (sample.Throttle > 0.98f)
            _throttleMaxReached++;
    }

    // ═══ LAP COMPLETION ═══
    public void OnLapCompleted(int lap)
    {
        RunFullDiagnosis();
        GiveVoiceAdvice();
    }

    // ═══ FULL DIAGNOSIS ═══
    public HardwareDiagnosisScore RunFullDiagnosis()
    {
        CurrentScore = new HardwareDiagnosisScore();

        // Run all 13 diagnostic rules
        Rule1_BrakeGainTooHigh();
        Rule2_BrakeTooSoft();
        Rule3_BrakeReleaseProblem();
        Rule4_TooMuchSteeringSmoothing();
        Rule5_NotEnoughSteeringSmoothing();
        Rule6_DampingTooHigh();
        Rule7_DampingTooLow();
        Rule8_FfbTooStrong();
        Rule9_FfbTooWeak();
        Rule10_BrakeBias();
        Rule11_ThrottleTooSensitive();
        Rule12_PedalCalibration();
        Rule13_SteeringRatio();

        return CurrentScore;
    }

    // ═══ DIAGNOSTIC RULES ═══

    private void Rule1_BrakeGainTooHigh()
    {
        // Track brake rise rate
        if (_brakeHistory.Count < 10) return;

        var deltas = new List<float>();
        float prevVal = _brakeHistory.First();
        foreach (var val in _brakeHistory.Skip(1))
        {
            if (val > prevVal)
                deltas.Add(val - prevVal);
            prevVal = val;
        }

        if (deltas.Count == 0) return;

        float avgRiseRate = deltas.Average();
        if (avgRiseRate > 0.4f && _brakeLockupCount > 2)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "brake_gain_high",
                Category = "brake",
                Problem = "Brake gain may be too aggressive",
                Recommendation = "Lower brake gain by 2-5% or increase pedal travel for finer control. Test for 5 laps before another change.",
                VoiceCall = "Brake gain may be too aggressive. Lower it slightly.",
                Confidence = 0.7f,
                Severity = "warning",
                SamplesAnalyzed = _brakeHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.BrakeSetupConfidence = 0.4f;
        }
    }

    private void Rule2_BrakeTooSoft()
    {
        // Track max brake pressure in braking zones (speed drops > 30km/h = 8.3 m/s)
        if (_brakeHistory.Count < 20) return;

        float maxBrake = _brakeHistory.Max();
        if (maxBrake < 0.7f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "brake_too_soft",
                Category = "brake",
                Problem = "Brake sensitivity may be too low",
                Recommendation = "Increase brake sensitivity or lower pedal force target by 5-10%. Test for 5 laps.",
                VoiceCall = "You may need more brake sensitivity.",
                Confidence = 0.6f,
                Severity = "info",
                SamplesAnalyzed = _brakeHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.BrakeSetupConfidence = 0.6f;
        }
    }

    private void Rule3_BrakeReleaseProblem()
    {
        // Track brake release rate (negative deltas when decreasing)
        if (_brakeHistory.Count < 10) return;

        var releaseRates = new List<float>();
        float prevVal = _brakeHistory.First();
        foreach (var val in _brakeHistory.Skip(1))
        {
            if (val < prevVal)
                releaseRates.Add(prevVal - val);
            prevVal = val;
        }

        if (releaseRates.Count == 0) return;

        float avgReleaseRate = releaseRates.Average();
        if (avgReleaseRate > 0.3f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "brake_release_abrupt",
                Category = "brake",
                Problem = "Brake release is too abrupt",
                Recommendation = "Add smoother pedal control or adjust brake curve for gradual release. Test for 5 laps.",
                VoiceCall = "Brake release is too abrupt. Smooth it out.",
                Confidence = 0.6f,
                Severity = "info",
                SamplesAnalyzed = _brakeHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.BrakeSetupConfidence = 0.7f;
        }
    }

    private void Rule4_TooMuchSteeringSmoothing()
    {
        // Measure steering response delay (time between input and yaw response)
        // Proxy: if steering changes but yaw rate lags, smoothing is too high
        if (_steeringHistory.Count < 20 || _yawRateHistory.Count < 20) return;

        // Simple check: high steering changes but low yaw response indicates lag
        float avgSteeringChange = _steeringRateHistory.Average();
        float avgYawRate = _yawRateHistory.Average();

        if (avgSteeringChange > 0.01f && avgYawRate < 0.1f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "steering_too_smooth",
                Category = "steering",
                Problem = "Steering may feel delayed due to smoothing",
                Recommendation = "Reduce steering smoothing by 1-2 points. Test for 5 laps.",
                VoiceCall = "Steering feels delayed. Reduce smoothing slightly.",
                Confidence = 0.5f,
                Severity = "info",
                SamplesAnalyzed = _steeringHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.SteeringSetupConfidence = 0.7f;
        }
    }

    private void Rule5_NotEnoughSteeringSmoothing()
    {
        // Track high-frequency steering noise: count small corrections per second
        if (_steeringRateHistory.Count < 100) return;

        // Small corrections: between 0.005 and 0.05 rad per sample (20Hz = 5 per second per 100 samples)
        var smallCorrections = _steeringRateHistory.Where(x => x > 0.005f && x < 0.05f).Count();
        float corrPerSecond = (smallCorrections / (float)_steeringRateHistory.Count) * 20f;  // 20Hz

        if (corrPerSecond > 8f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "steering_too_noisy",
                Category = "steering",
                Problem = "Steering inputs are noisy with small corrections",
                Recommendation = "Add a little steering smoothing (1-2 points) or practice smoother hand movements. Test for 5 laps.",
                VoiceCall = "Your hands are noisy. Smooth out your steering.",
                Confidence = 0.6f,
                Severity = "info",
                SamplesAnalyzed = _steeringRateHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.SteeringSetupConfidence = 0.7f;
        }
    }

    private void Rule6_DampingTooHigh()
    {
        // If steering response is consistently late to yaw rate changes
        // Proxy: steering changes but yaw responds slowly (cross-correlation lag)
        if (_steeringHistory.Count < 30 || _yawRateHistory.Count < 30) return;

        float avgSteeringRate = _steeringRateHistory.Average();
        float avgYawRate = _yawRateHistory.Average();

        // High steering input but low yaw response = damping may be high
        if (avgSteeringRate > 0.02f && avgYawRate < 0.15f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "damping_too_high",
                Category = "ffb",
                Problem = "Wheel may be over-damped",
                Recommendation = "Reduce damping by 2-5 points. Test for 5 laps before another change.",
                VoiceCall = "Wheel may be over-damped. Reduce damping.",
                Confidence = 0.55f,
                Severity = "info",
                SamplesAnalyzed = _yawRateHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.FfbSetupConfidence = 0.7f;
        }
    }

    private void Rule7_DampingTooLow()
    {
        // Steering oscillation on straights: high variance when speed > 100mph and brake/throttle low
        if (_steeringHistory.Count < 50) return;

        float steeringVariance = CalculateVariance(_steeringHistory.ToList());

        // High variance in steering = oscillation
        if (steeringVariance > 0.01f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "damping_too_low",
                Category = "ffb",
                Problem = "Wheel may need more damping or friction",
                Recommendation = "Increase damping by 2-5 points or add friction. Test for 5 laps.",
                VoiceCall = "Wheel is oscillating. Increase damping.",
                Confidence = 0.55f,
                Severity = "info",
                SamplesAnalyzed = _steeringHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.FfbSetupConfidence = 0.7f;
        }
    }

    private void Rule8_FfbTooStrong()
    {
        // If steering correction speed is slow (time to counter-steer > 0.3s when yaw spikes)
        // Proxy: large yaw rate but delayed steering response
        if (_yawRateHistory.Count < 20) return;

        float maxYawRate = _yawRateHistory.Max();
        if (maxYawRate > 0.5f)
        {
            // Driver needs to respond quickly to yaw
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "ffb_too_strong",
                Category = "ffb",
                Problem = "Force feedback may be too heavy",
                Recommendation = "Lower FFB strength by 2-5%. Test for 5 laps before another change.",
                VoiceCall = "Force feedback may be too heavy. Lower it slightly.",
                Confidence = 0.5f,
                Severity = "info",
                SamplesAnalyzed = _yawRateHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.FfbSetupConfidence = 0.8f;
        }
    }

    private void Rule9_FfbTooWeak()
    {
        // If driver overdrives entry and steering corrections are large
        // Proxy: high steering angles but slow to recover
        if (_steeringHistory.Count < 20) return;

        float maxSteering = _steeringHistory.Max();
        float avgSteering = _steeringHistory.Average();

        if (maxSteering > 1.2f && avgSteering > 0.3f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "ffb_too_weak",
                Category = "ffb",
                Problem = "Force feedback may be too light",
                Recommendation = "Increase FFB strength by 2-5% or detail. Test for 5 laps carefully.",
                VoiceCall = "Force feedback may be too light. Increase it.",
                Confidence = 0.5f,
                Severity = "info",
                SamplesAnalyzed = _steeringHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.FfbSetupConfidence = 0.7f;
        }
    }

    private void Rule10_BrakeBias()
    {
        // Front lockups: high yaw rate during braking with high brake input
        if (_frontLockupCount > 3)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "brake_bias_front",
                Category = "brake",
                Problem = "Front locking up too much",
                Recommendation = "Move brake bias rearward slightly (1-2%) if overall stability is good. Test for 5 laps.",
                VoiceCall = "Front is locking. Move brake bias rearward.",
                Confidence = 0.6f,
                Severity = "warning",
                SamplesAnalyzed = _brakeLockupCount,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.BrakeSetupConfidence = 0.65f;
        }

        if (_rearYawSpikeCount > 2)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "brake_bias_rear",
                Category = "brake",
                Problem = "Rear locking/yaw spike during braking",
                Recommendation = "Move brake bias forward slightly (1-2%). Test for 5 laps.",
                VoiceCall = "Rear is stepping out. Move brake bias forward.",
                Confidence = 0.6f,
                Severity = "warning",
                SamplesAnalyzed = _rearYawSpikeCount,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.BrakeSetupConfidence = 0.65f;
        }
    }

    private void Rule11_ThrottleTooSensitive()
    {
        // Throttle rise rate on corner exit (low speed + steering)
        if (_throttleHistory.Count < 20) return;

        var riseRates = new List<float>();
        float prevVal = _throttleHistory.First();
        foreach (var val in _throttleHistory.Skip(1))
        {
            if (val > prevVal)
                riseRates.Add(val - prevVal);
            prevVal = val;
        }

        if (riseRates.Count == 0) return;

        float maxRiseRate = riseRates.Max();
        if (maxRiseRate > 0.25f)  // >0.5 in 100ms proxy
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "throttle_too_sensitive",
                Category = "throttle",
                Problem = "Throttle curve may be too sharp",
                Recommendation = "Soften throttle response by 2-5%. Test for 5 laps.",
                VoiceCall = "Throttle is too sharp. Soften the curve.",
                Confidence = 0.6f,
                Severity = "info",
                SamplesAnalyzed = _throttleHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.PedalCalibrationConfidence = 0.75f;
        }
    }

    private void Rule12_PedalCalibration()
    {
        // If max throttle never reaches > 0.98 OR min never reaches < 0.02
        if (_throttleHistory.Count < 100)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "pedal_calib_throttle",
                Category = "pedal_calibration",
                Problem = "Throttle input not reaching full range",
                Recommendation = "Check pedal calibration. Input should reach 0.0 and 1.0 cleanly. Recalibrate if needed.",
                VoiceCall = "Check throttle pedal calibration.",
                Confidence = 0.8f,
                Severity = "warning",
                SamplesAnalyzed = _throttleHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.PedalCalibrationConfidence = 0.3f;
            return;
        }

        float maxThrottle = _throttleHistory.Max();
        float minThrottle = _throttleHistory.Min();

        if (maxThrottle < 0.98f || minThrottle > 0.02f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "pedal_calib_throttle",
                Category = "pedal_calibration",
                Problem = "Throttle pedal not returning to zero or maxing out",
                Recommendation = "Recalibrate throttle pedal in iRacing settings. Ensure full mechanical range.",
                VoiceCall = "Check throttle pedal calibration.",
                Confidence = 0.8f,
                Severity = "warning",
                SamplesAnalyzed = _throttleHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.PedalCalibrationConfidence = 0.4f;
        }

        // Same for brake
        if (_brakeHistory.Count > 100)
        {
            float maxBrake = _brakeHistory.Max();
            float minBrake = _brakeHistory.Min();

            if (maxBrake < 0.98f || minBrake > 0.02f)
            {
                var diagnosis = new HardwareDiagnosis
                {
                    DiagnosisId = "pedal_calib_brake",
                    Category = "pedal_calibration",
                    Problem = "Brake pedal not returning to zero or maxing out",
                    Recommendation = "Recalibrate brake pedal in iRacing settings. Ensure full mechanical range.",
                    VoiceCall = "Check brake pedal calibration.",
                    Confidence = 0.8f,
                    Severity = "warning",
                    SamplesAnalyzed = _brakeHistory.Count,
                    DetectedAt = DateTime.UtcNow
                };
                AddDiagnosis(diagnosis);
                CurrentScore.PedalCalibrationConfidence = 0.4f;
            }
        }
    }

    private void Rule13_SteeringRatio()
    {
        // Average |steering angle| > 1.5 rad across corners = too slow ratio
        if (_steeringHistory.Count < 50) return;

        float avgAbsSteering = _steeringHistory.Average(x => Math.Abs(x));

        if (avgAbsSteering > 1.5f)
        {
            var diagnosis = new HardwareDiagnosis
            {
                DiagnosisId = "steering_ratio_slow",
                Category = "steering",
                Problem = "Steering ratio may be too slow",
                Recommendation = "Increase wheel rotation setting or decrease steering sensitivity. Test for 5 laps.",
                VoiceCall = "Steering ratio may be too slow.",
                Confidence = 0.55f,
                Severity = "info",
                SamplesAnalyzed = _steeringHistory.Count,
                DetectedAt = DateTime.UtcNow
            };
            AddDiagnosis(diagnosis);
            CurrentScore.SteeringSetupConfidence = 0.75f;
        }

        // Tiny inputs (< 0.2 rad) cause large yaw = too sensitive
        if (avgAbsSteering < 0.2f)
        {
            float avgYawRate = _yawRateHistory.Average();
            if (avgYawRate > 0.5f)
            {
                var diagnosis = new HardwareDiagnosis
                {
                    DiagnosisId = "steering_ratio_fast",
                    Category = "steering",
                    Problem = "Steering ratio may be too fast/sensitive",
                    Recommendation = "Decrease wheel rotation setting or increase steering sensitivity adjustment. Test carefully.",
                    VoiceCall = "Steering ratio may be too fast.",
                    Confidence = 0.55f,
                    Severity = "info",
                    SamplesAnalyzed = _steeringHistory.Count,
                    DetectedAt = DateTime.UtcNow
                };
                AddDiagnosis(diagnosis);
                CurrentScore.SteeringSetupConfidence = 0.75f;
            }
        }
    }

    // ═══ VOICE ADVICE ═══
    public void GiveVoiceAdvice()
    {
        if (!_talkTiming.CanSpeak("coaching")) return;
        if (_hasGivenAdviceThisRun) return;
        if (CurrentScore.ActiveDiagnoses.Count == 0) return;

        // Pick highest confidence diagnosis
        var topDiagnosis = CurrentScore.ActiveDiagnoses
            .OrderByDescending(x => x.Confidence)
            .FirstOrDefault();

        if (topDiagnosis != null && topDiagnosis.Confidence > 0.55f)
        {
            _voiceEngine.Enqueue(topDiagnosis.VoiceCall, "coaching");
            _hasGivenAdviceThisRun = true;
        }
    }

    // ═══ HELPERS ═══
    private void AddDiagnosis(HardwareDiagnosis diagnosis)
    {
        SessionDiagnoses.Add(diagnosis);
        CurrentScore.ActiveDiagnoses.Add(diagnosis);
        OnDiagnosisDetected?.Invoke(diagnosis);
    }

    private void ResetLapCounters()
    {
        _brakeLockupCount = 0;
        _throttleSpikeCount = 0;
        _steeringCorrectionCount = 0;
        _brakeMaxReached = 0;
        _throttleMaxReached = 0;
        _frontLockupCount = 0;
        _rearYawSpikeCount = 0;
        _hasGivenAdviceThisRun = false;
    }

    private float CalculateVariance(List<float> values)
    {
        if (values.Count == 0) return 0f;
        float mean = values.Average();
        float sumSquaredDiff = values.Sum(x => (x - mean) * (x - mean));
        return sumSquaredDiff / values.Count;
    }
}
