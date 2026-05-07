// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — A/B Test Coach
// Helps driver test one change at a time with structured methodology.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A/B test data model. Tracks baseline vs test performance for
/// a single change across two phases.
/// </summary>
public class ABTest
{
    /// <summary>
    /// Unique test ID.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Human-readable change description (e.g., "Lower damping by 3 points").
    /// </summary>
    public string ChangeDescription { get; set; } = "";

    /// <summary>
    /// Category: hardware, setup, driving.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Number of baseline laps completed.
    /// </summary>
    public int BaselineLaps { get; set; }

    /// <summary>
    /// Number of test laps completed.
    /// </summary>
    public int TestLaps { get; set; }

    /// <summary>
    /// Required clean laps per phase (default 5).
    /// </summary>
    public int RequiredCleanLaps { get; set; } = 5;

    /// <summary>
    /// Average baseline lap time.
    /// </summary>
    public float BaselineAvgLap { get; set; }

    /// <summary>
    /// Average test lap time.
    /// </summary>
    public float TestAvgLap { get; set; }

    /// <summary>
    /// Baseline performance at specific corner (if applicable).
    /// </summary>
    public float BaselineCornerDelta { get; set; }

    /// <summary>
    /// Test performance at specific corner (if applicable).
    /// </summary>
    public float TestCornerDelta { get; set; }

    /// <summary>
    /// Individual baseline lap times.
    /// </summary>
    public List<float> BaselineLapTimes { get; set; } = new();

    /// <summary>
    /// Individual test lap times.
    /// </summary>
    public List<float> TestLapTimes { get; set; } = new();

    /// <summary>
    /// Current phase: baseline, testing, complete.
    /// </summary>
    public string Status { get; set; } = "baseline";

    /// <summary>
    /// Result: improved, worsened, inconclusive.
    /// </summary>
    public string Result { get; set; } = "";

    /// <summary>
    /// Recommendation: keep, revert, test_more.
    /// </summary>
    public string Verdict { get; set; } = "";
}

/// <summary>
/// A/B testing coaching module. Guides driver through structured
/// baseline and test phases to measure impact of a single change.
/// </summary>
public class ABTestCoach : ICoachingModule
{
    private readonly CoachingPrioritySystem _priority;
    private readonly ChiefDatabase _db;
    private bool _isEnabled = false;
    private ABTest? _currentTest;
    private int _cleanLapCount = 0;
    private bool _previousLapWasClean = false;

    public string ModuleName => "ABTestCoach";
    public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }

    public ABTest? CurrentTest => _currentTest;

    public ABTestCoach(CoachingPrioritySystem priority, ChiefDatabase db)
    {
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public void ProcessTick(EliteCoachingContext context)
    {
        if (!IsEnabled || _currentTest == null)
            return;

        var sample = context.CurrentSample;

        // Track clean lap status (no off-track, incidents, etc.)
        // This would ideally integrate with event hooks
        _previousLapWasClean = IsLapClean(sample);
    }

    public void OnLapCompleted(EliteCoachingContext context, int lap)
    {
        if (!IsEnabled || _currentTest == null)
            return;

        // Check if lap was clean
        if (_previousLapWasClean)
        {
            _cleanLapCount++;
        }
        else
        {
            _cleanLapCount = 0;
        }

        float lapTime = context.LastLapTime;
        if (lapTime > 0)
        {
            if (_currentTest.Status == "baseline")
            {
                _currentTest.BaselineLapTimes.Add(lapTime);
                _currentTest.BaselineLaps++;

                // Check if baseline phase complete
                if (_cleanLapCount >= _currentTest.RequiredCleanLaps)
                {
                    CompleteBaselinePhase(context);
                }
                else
                {
                    int remaining = _currentTest.RequiredCleanLaps - _cleanLapCount;
                    SubmitDecision(context, "testing",
                        $"Baseline lap {_currentTest.BaselineLaps}. Need {remaining} more clean.",
                        $"Baseline. {remaining} more laps.", CoachingPriority.Low, 60);
                }
            }
            else if (_currentTest.Status == "testing")
            {
                _currentTest.TestLapTimes.Add(lapTime);
                _currentTest.TestLaps++;

                // Check if test phase complete
                if (_cleanLapCount >= _currentTest.RequiredCleanLaps)
                {
                    CompleteTestPhase(context);
                }
                else
                {
                    int remaining = _currentTest.RequiredCleanLaps - _cleanLapCount;
                    SubmitDecision(context, "testing",
                        $"Testing lap {_currentTest.TestLaps}. Need {remaining} more clean.",
                        $"Testing. {remaining} more.", CoachingPriority.Low, 60);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PUBLIC TEST CONTROL METHODS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Start a new A/B test with description and category.
    /// </summary>
    public void StartTest(string description, string category)
    {
        _currentTest = new ABTest
        {
            ChangeDescription = description,
            Category = category,
            Status = "baseline",
        };
        _cleanLapCount = 0;
    }

    /// <summary>
    /// Switch from baseline to test phase. Called after baseline laps complete.
    /// </summary>
    public void SwitchToTestPhase()
    {
        if (_currentTest == null)
            return;

        _currentTest.Status = "testing";
        _currentTest.BaselineAvgLap = _currentTest.BaselineLapTimes.Count > 0
            ? _currentTest.BaselineLapTimes.Average()
            : 0f;

        _cleanLapCount = 0;
    }

    /// <summary>
    /// Complete the test. Analyze results and generate verdict.
    /// </summary>
    public void CompleteTest()
    {
        if (_currentTest == null)
            return;

        _currentTest.TestAvgLap = _currentTest.TestLapTimes.Count > 0
            ? _currentTest.TestLapTimes.Average()
            : 0f;

        _currentTest.Status = "complete";

        // Determine result
        float diff = _currentTest.TestAvgLap - _currentTest.BaselineAvgLap;
        float threshold = 0.05f; // 50ms variance = inconclusive

        if (diff < -threshold)
        {
            _currentTest.Result = "improved";
            _currentTest.Verdict = "keep";
        }
        else if (diff > threshold)
        {
            _currentTest.Result = "worsened";
            _currentTest.Verdict = "revert";
        }
        else
        {
            _currentTest.Result = "inconclusive";
            _currentTest.Verdict = "test_more";
        }

        // Persist to database
        try
        {
            _db.InsertABTest(new ABTestRecord
            {
                Id = _currentTest.Id,
                SessionId = "",
                ChangeDescription = _currentTest.ChangeDescription,
                Category = _currentTest.Category,
                BaselineAvgLap = _currentTest.BaselineAvgLap,
                TestAvgLap = _currentTest.TestAvgLap,
                Result = _currentTest.Result,
                Verdict = _currentTest.Verdict,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            });
        }
        catch
        {
            // Silent DB error
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE METHODS
    // ═══════════════════════════════════════════════════════════════

    private void CompleteBaselinePhase(EliteCoachingContext context)
    {
        if (_currentTest == null)
            return;

        _currentTest.BaselineAvgLap = _currentTest.BaselineLapTimes.Average();

        SubmitDecision(context, "testing",
            $"Baseline complete (avg {_currentTest.BaselineAvgLap:F2}s). Make the change now.",
            "Baseline complete. Make the change now.", CoachingPriority.Low, 70);

        SwitchToTestPhase();
    }

    private void CompleteTestPhase(EliteCoachingContext context)
    {
        if (_currentTest == null)
            return;

        _currentTest.TestAvgLap = _currentTest.TestLapTimes.Average();

        CompleteTest();

        // Report result
        string resultText = "";
        if (_currentTest.Result == "improved")
        {
            float gain = _currentTest.BaselineAvgLap - _currentTest.TestAvgLap;
            resultText = $"Result: faster by {gain:F2}s average. Keep the change.";
        }
        else if (_currentTest.Result == "worsened")
        {
            float loss = _currentTest.TestAvgLap - _currentTest.BaselineAvgLap;
            resultText = $"Result: slower by {loss:F2}s average. Revert.";
        }
        else
        {
            resultText = "Result: inconclusive. 2 more laps needed.";
        }

        SubmitDecision(context, "testing", resultText, resultText, CoachingPriority.Low, 75);
    }

    private bool IsLapClean(TelemetrySample sample)
    {
        // A "clean" lap has no off-track or incidents
        // This is a simplified check; ideally would integrate with event hooks
        return sample != null && sample.IncidentCount == 0;
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

/// <summary>
/// Database model for persisting A/B test results.
/// </summary>
public class ABTestRecord
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string ChangeDescription { get; set; } = "";
    public string Category { get; set; } = "";
    public float BaselineAvgLap { get; set; }
    public float TestAvgLap { get; set; }
    public string Result { get; set; } = "";
    public string Verdict { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
