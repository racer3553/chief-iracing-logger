// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Elite Coaching Brain
// Central orchestrator for all coaching decisions. Manages telemetry
// tick processing, module registration, priority system, confidence
// gating, timing rules, and voice delivery.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// Central decision engine for coaching. Orchestrates all subsystems:
/// priority queue, confidence gating, talk timing, and voice delivery.
/// </summary>
public class EliteCoachingBrain : IDisposable
{
    private readonly object _lock = new();
    private readonly EventHooks _events;
    private readonly VoiceEngine _voice;
    private readonly TalkTimingSystem _baseTiming;
    private readonly TrackMapService _trackMapService;
    private readonly ICornerPerformanceProvider _cornerPerformance;
    private readonly HardwareTuningCoach _hardwareCoach;
    private readonly ChiefDatabase _db;
    private readonly CoachingPrioritySystem _priority;
    private readonly TalkLessEngine _talkLess;
    private readonly ConfidenceGate _confidenceGate;

    private List<ICoachingModule> _modules = new();
    private EliteCoachingContext _currentContext;
    private List<CoachingDecision> _sessionDecisions = new();
    private List<CoachingDecision> _spokenDecisions = new();
    private string _sessionMode = "practice";
    private bool _disposed = false;

    // Events
    public event Action<CoachingDecision>? OnDecisionMade;
    public event Action<CoachingDecision>? OnDecisionSpoken;
    public event Action<CoachingDecision, string>? OnDecisionSuppressed;

    public EliteCoachingBrain(
        EventHooks events,
        VoiceEngine voice,
        TalkTimingSystem baseTiming,
        TrackMapService trackMapService,
        ICornerPerformanceProvider cornerPerformance,
        HardwareTuningCoach hardwareCoach,
        ChiefDatabase db,
        CoachingPrioritySystem priority,
        TalkLessEngine talkLess,
        ConfidenceGate confidenceGate)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _voice = voice ?? throw new ArgumentNullException(nameof(voice));
        _baseTiming = baseTiming ?? throw new ArgumentNullException(nameof(baseTiming));
        _trackMapService = trackMapService ?? throw new ArgumentNullException(nameof(trackMapService));
        _cornerPerformance = cornerPerformance ?? throw new ArgumentNullException(nameof(cornerPerformance));
        _hardwareCoach = hardwareCoach ?? throw new ArgumentNullException(nameof(hardwareCoach));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _talkLess = talkLess ?? throw new ArgumentNullException(nameof(talkLess));
        _confidenceGate = confidenceGate ?? throw new ArgumentNullException(nameof(confidenceGate));

        _currentContext = new EliteCoachingContext();
    }

    /// <summary>
    /// Process a single telemetry sample. Main entry point for real-time coaching.
    /// </summary>
    public void ProcessTelemetryTick(TelemetrySample sample)
    {
        if (_disposed || sample == null)
            return;

        lock (_lock)
        {
            // Build context from current state
            BuildContext(sample);

            // Update base timing system
            _baseTiming.UpdateState(sample);

            // Call all registered modules to submit decisions
            foreach (var module in _modules)
            {
                if (module.IsEnabled)
                {
                    try
                    {
                        module.ProcessTick(_currentContext);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Elite Brain: Module {module.ModuleName} threw exception: {ex.Message}");
                    }
                }
            }

            // Process decision queue
            ProcessDecisionQueue();
        }
    }

    /// <summary>
    /// Called when a lap is completed. Triggers module lap-end callbacks
    /// and one-lap-fix analysis.
    /// </summary>
    public void OnLapCompleted(int lap)
    {
        lock (_lock)
        {
            _currentContext.CurrentLap = lap + 1; // Move to next lap

            // Call all modules for lap completion
            foreach (var module in _modules)
            {
                if (module.IsEnabled)
                {
                    try
                    {
                        module.OnLapCompleted(_currentContext, lap);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Elite Brain: Module {module.ModuleName} lap callback threw: {ex.Message}");
                    }
                }
            }

            // Reset talk timing counters
            _talkLess.OnNewLap(lap + 1);
        }
    }

    /// <summary>
    /// Set the session mode (practice, qualifying, race, testing).
    /// </summary>
    public void SetSessionMode(string mode)
    {
        lock (_lock)
        {
            _sessionMode = mode?.ToLower() ?? "practice";
        }
    }

    /// <summary>
    /// Register a coaching module to be called on each telemetry tick.
    /// </summary>
    public void RegisterModule(ICoachingModule module)
    {
        if (module == null)
            return;

        lock (_lock)
        {
            if (!_modules.Any(m => m.ModuleName == module.ModuleName))
            {
                _modules.Add(module);
            }
        }
    }

    /// <summary>
    /// Get all coaching decisions made in this session.
    /// </summary>
    public List<CoachingDecision> GetSessionDecisions()
    {
        lock (_lock)
        {
            return new List<CoachingDecision>(_sessionDecisions);
        }
    }

    /// <summary>
    /// Get only the decisions that were actually spoken.
    /// </summary>
    public List<CoachingDecision> GetSpokenDecisions()
    {
        lock (_lock)
        {
            return new List<CoachingDecision>(_spokenDecisions);
        }
    }

    /// <summary>
    /// Get current coaching context (for UI/debugging).
    /// </summary>
    public EliteCoachingContext CurrentContext
    {
        get
        {
            lock (_lock)
            {
                return _currentContext;
            }
        }
    }

    /// <summary>
    /// Get suppressed decisions that were deferred for post-session review.
    /// </summary>
    public List<CoachingDecision> GetPostSessionNotes()
    {
        return _confidenceGate.GetPostSessionNotes();
    }

    /// <summary>
    /// Get count of pending decisions in priority queue.
    /// </summary>
    public int PendingDecisionsCount => _priority.PendingCount;

    /// <summary>
    /// Check if high-priority decisions are pending.
    /// </summary>
    public bool HasHighPriorityPending => _priority.IsHighPriorityPending();

    /// <summary>
    /// Clear all pending decisions (emergency reset).
    /// </summary>
    public void ClearPendingDecisions()
    {
        lock (_lock)
        {
            _priority.ClearAll();
        }
    }

    /// <summary>
    /// Reset session state (new session starting).
    /// </summary>
    public void ResetSession()
    {
        lock (_lock)
        {
            _sessionDecisions.Clear();
            _spokenDecisions.Clear();
            _priority.ClearAll();
            _talkLess.Reset();
            _confidenceGate.ClearPostSessionNotes();
            _currentContext = new EliteCoachingContext();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _priority.ClearAll();
            _modules.Clear();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE METHODS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Build the coaching context from current telemetry and game state.
    /// </summary>
    private void BuildContext(TelemetrySample sample)
    {
        if (_currentContext.PreviousSample == null)
        {
            _currentContext.PreviousSample = sample;
        }

        _currentContext.CurrentSample = sample;

        // Determine if on straight
        var isOnStraight = sample.Brake < 0.1f && Math.Abs(sample.SteeringAngle) < 0.15f;
        _currentContext.IsOnStraight = isOnStraight;

        // Get track position
        var corner = _trackMapService.GetCurrentCorner(sample.LapDistPct);
        var nextCorner = _trackMapService.GetNextCorner(sample.LapDistPct);
        _currentContext.CurrentCorner = corner;
        _currentContext.NextCorner = nextCorner;

        // Calculate time to next brake zone
        if (nextCorner != null)
        {
            var distToNextBrake = nextCorner.BrakeZoneDistPct - sample.LapDistPct;
            if (distToNextBrake < 0)
                distToNextBrake += 100; // Wrap to next lap

            // Approximate: 100 dist pct ≈ 120 seconds at 100 mph average
            var secondsPerDistPct = 1.2f; // Very rough approximation
            _currentContext.TimeToNextBrakeZone = distToNextBrake * secondsPerDistPct;
        }

        // Check if on best lap pace (delta < -0.3 seconds)
        _currentContext.IsOnBestLapPace = sample.LapDeltaToSessionBestLap < -0.3f;

        // Check race start (first 15 seconds)
        _currentContext.IsRaceStart = sample.Lap == 1 && sample.LapDistPct < 2; // Rough approximation

        // Side-by-side check (car nearby)
        _currentContext.IsSideBySide = false; // Will be set by spotter module if needed

        // Hardware diagnostics
        try
        {
            _hardwareCoach.AnalyzeSample(sample);
            _currentContext.HardwareScore = _hardwareCoach.CurrentScore;
        }
        catch
        {
            // Silent fail
        }

        _currentContext.PreviousSample = sample;
    }

    /// <summary>
    /// Process the priority queue, applying gating and timing rules,
    /// then deliver approved messages to voice engine.
    /// </summary>
    private void ProcessDecisionQueue()
    {
        while (true)
        {
            // Get highest priority pending decision
            var decision = _priority.GetNext();
            if (decision == null)
                break;

            // Record that this decision was made
            _sessionDecisions.Add(decision);
            OnDecisionMade?.Invoke(decision);

            // Pass through confidence gate
            var gateResult = _confidenceGate.Evaluate(decision);
            if (!gateResult.Passed)
            {
                decision.WasSuppressed = true;
                decision.SuppressReason =
                    $"Confidence {gateResult.ActualConfidence} < {gateResult.RequiredConfidence} for {gateResult.Category}";
                OnDecisionSuppressed?.Invoke(decision, decision.SuppressReason);
                continue;
            }

            // Pass through talk timing system
            var talkLessResult = _talkLess.ShouldSpeak(decision, _currentContext);
            if (!talkLessResult.Allowed)
            {
                decision.WasSuppressed = true;
                decision.SuppressReason = talkLessResult.Reason;
                OnDecisionSuppressed?.Invoke(decision, talkLessResult.Reason);
                continue;
            }

            // All gates passed - deliver to voice
            try
            {
                var voicePriority = CoachingPriorityToVoicePriority(decision.Priority);
                _voice.Enqueue(decision.VoiceText, voicePriority);

                decision.WasSpoken = true;
                _spokenDecisions.Add(decision);
                _talkLess.RecordSpoken(decision);

                // Log to database
                try
                {
                    _db.LogCoachingDecision(decision);
                }
                catch
                {
                    // Silent DB log failure
                }

                OnDecisionSpoken?.Invoke(decision);
            }
            catch (Exception ex)
            {
                decision.WasSuppressed = true;
                decision.SuppressReason = $"Voice engine error: {ex.Message}";
                OnDecisionSuppressed?.Invoke(decision, decision.SuppressReason);
            }
        }
    }

    /// <summary>
    /// Convert coaching priority to voice engine priority.
    /// </summary>
    private int CoachingPriorityToVoicePriority(CoachingPriority priority)
    {
        return priority switch
        {
            CoachingPriority.Critical => 0,   // Highest
            CoachingPriority.High => 1,
            CoachingPriority.Medium => 2,
            CoachingPriority.Low => 3,
            CoachingPriority.Minimal => 4,    // Lowest
            _ => 3
        };
    }
}

/// <summary>
/// Interface for coaching modules that plug into the Elite Brain.
/// Modules submit decisions to the priority system during ProcessTick.
/// </summary>
public interface ICoachingModule
{
    /// <summary>
    /// Display name of this module.
    /// </summary>
    string ModuleName { get; }

    /// <summary>
    /// True if this module should be active.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Called on each telemetry tick. Module should analyze context
    /// and submit decisions to the brain's priority system.
    /// </summary>
    void ProcessTick(EliteCoachingContext context);

    /// <summary>
    /// Called when a lap is completed.
    /// </summary>
    void OnLapCompleted(EliteCoachingContext context, int lap);
}

/// <summary>
/// Real-time coaching context available to modules.
/// Updated on each telemetry tick.
/// </summary>
public class EliteCoachingContext
{
    /// <summary>
    /// Current telemetry sample.
    /// </summary>
    public TelemetrySample CurrentSample { get; set; } = new();

    /// <summary>
    /// Previous sample for derivatives.
    /// </summary>
    public TelemetrySample? PreviousSample { get; set; }

    /// <summary>
    /// Current session ID.
    /// </summary>
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Session type: practice, qualifying, race, testing.
    /// </summary>
    public string SessionType { get; set; } = "practice";

    /// <summary>
    /// Vehicle class.
    /// </summary>
    public string Car { get; set; } = "";

    /// <summary>
    /// Track name.
    /// </summary>
    public string Track { get; set; } = "";

    /// <summary>
    /// Current lap number.
    /// </summary>
    public int CurrentLap { get; set; }

    /// <summary>
    /// Best lap time this session (seconds).
    /// </summary>
    public float BestLapTime { get; set; }

    /// <summary>
    /// Most recent lap time (seconds).
    /// </summary>
    public float LastLapTime { get; set; }

    /// <summary>
    /// Time delta to session best lap (negative = ahead).
    /// </summary>
    public float CurrentDelta { get; set; }

    /// <summary>
    /// Next corner in track map.
    /// </summary>
    public TrackCorner? NextCorner { get; set; }

    /// <summary>
    /// Current corner in track map.
    /// </summary>
    public TrackCorner? CurrentCorner { get; set; }

    /// <summary>
    /// Estimated time to next brake zone (seconds).
    /// </summary>
    public float TimeToNextBrakeZone { get; set; }

    /// <summary>
    /// True if driver is currently on a straight section.
    /// </summary>
    public bool IsOnStraight { get; set; }

    /// <summary>
    /// True if another car is beside us (side-by-side).
    /// </summary>
    public bool IsSideBySide { get; set; }

    /// <summary>
    /// True if this is the first few seconds of a race start.
    /// </summary>
    public bool IsRaceStart { get; set; }

    /// <summary>
    /// True if driver is currently on best-lap pace.
    /// </summary>
    public bool IsOnBestLapPace { get; set; }

    /// <summary>
    /// Hardware diagnosis score (if available).
    /// </summary>
    public HardwareDiagnosisScore? HardwareScore { get; set; }
}
