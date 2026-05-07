// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Talk Less Engine
// Anti-annoyance system that suppresses redundant or poorly-timed
// coaching. Enforces gap limits, corner limits, lap limits, and
// repeating mistake detection.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public class TalkLessEngine
{
    private readonly TalkTimingSystem _baseTiming;
    private readonly string _sessionMode; // practice, qualifying, race, testing
    private readonly object _lock = new();

    // Configuration
    private long _minGapBetweenCallsMs = 8_000; // 8-12 seconds configurable
    private int _maxMessagesPerCornerPerLap = 1;
    private int _maxMessagesPerLap_Race = 3;
    private int _maxMessagesPerLap_Qualifying = 5;
    private int _maxMessagesPerLap_Practice = 8;
    private int _maxMessagesPerLap_PracticePro = 12;

    // State tracking
    private long _lastSpeakTimeMs = 0;
    private int _messagesThisLap = 0;
    private string _currentCorner = "";
    private int _messagesThisCorner = 0;
    private Dictionary<string, Queue<CoachingDecision>> _cornerMessageHistory = new(); // corner → last 5 messages
    private int _currentLap = 0;
    private long _lapStartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // Improvement tracking
    private Dictionary<string, int> _repeatMistakeCount = new(); // corner+category → count
    private Dictionary<string, float> _cornerLastDelta = new(); // corner → last measured delta

    public TalkLessEngine(TalkTimingSystem baseTiming, string sessionMode)
    {
        _baseTiming = baseTiming ?? throw new ArgumentNullException(nameof(baseTiming));
        _sessionMode = sessionMode?.ToLower() ?? "practice";
    }

    /// <summary>
    /// Determine if a coaching decision should be spoken based on timing,
    /// frequency, and contextual rules.
    /// </summary>
    public TalkLessResult ShouldSpeak(CoachingDecision decision, EliteCoachingContext context)
    {
        lock (_lock)
        {
            // Critical/safety always get through (unless driver is talking)
            if (decision.Priority == CoachingPriority.Critical)
            {
                if (_baseTiming.CanSpeakPriority(1)) // Priority 1 = high
                {
                    return new TalkLessResult(true, "Critical safety message");
                }
                return new TalkLessResult(false, "Driver currently speaking");
            }

            // 1. Minimum gap enforcement (8-12 seconds, Critical exempt)
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeSinceLastSpeak = now - _lastSpeakTimeMs;
            if (timeSinceLastSpeak < _minGapBetweenCallsMs)
            {
                return new TalkLessResult(false,
                    $"Minimum gap not met ({timeSinceLastSpeak}ms < {_minGapBetweenCallsMs}ms)");
            }

            // 2. Side-by-side suppression (only spotter allowed)
            if (context.IsSideBySide && decision.Priority < CoachingPriority.Critical)
            {
                return new TalkLessResult(false, "Suppressed during side-by-side racing");
            }

            // 3. Race start suppression (first 15 seconds of lap 1)
            if (_sessionMode == "race" && context.IsRaceStart && context.CurrentLap == 1)
            {
                var raceStartElapsed = now - _lapStartTimeMs;
                if (raceStartElapsed < 15_000)
                {
                    return new TalkLessResult(false, "Suppressed during race start");
                }
            }

            // 4. Heavy braking suppression (non-critical only)
            if (context.CurrentSample.Brake > 0.5f && decision.Priority > CoachingPriority.Critical)
            {
                return new TalkLessResult(false, "Suppressed during heavy braking");
            }

            // 5. Prefer straights (unless emergency)
            if (decision.Priority > CoachingPriority.High && !context.IsOnStraight)
            {
                if (context.CurrentSample.Brake > 0.1f || Math.Abs(context.CurrentSample.SteeringAngle) > 0.15f)
                {
                    // Can deliver coaching 3-6 seconds before brake zone
                    if (context.TimeToNextBrakeZone < 3 || context.TimeToNextBrakeZone > 6)
                    {
                        return new TalkLessResult(false, "Not on straight and not in pre-brake window");
                    }
                }
            }

            // 6. Push lap detection (on best-lap pace = delta < -0.3s)
            if (context.IsOnBestLapPace && decision.Priority <= CoachingPriority.Medium)
            {
                return new TalkLessResult(false, "Suppressed: driver on best-lap pace");
            }

            // 7. Corner limit (max 1 per corner per lap)
            if (context.CurrentCorner != null && context.CurrentCorner.CornerName != _currentCorner)
            {
                _currentCorner = context.CurrentCorner.CornerName;
                _messagesThisCorner = 0;
            }

            if (_messagesThisCorner >= _maxMessagesPerCornerPerLap)
            {
                return new TalkLessResult(false, "Max messages already delivered to this corner this lap");
            }

            // 8. Lap limit (varies by session type)
            int maxPerLap = _sessionMode switch
            {
                "race" => _maxMessagesPerLap_Race,
                "qualifying" => _maxMessagesPerLap_Qualifying,
                "testing" => _maxMessagesPerLap_Practice,
                _ => _maxMessagesPerLap_Practice
            };

            if (_messagesThisLap >= maxPerLap)
            {
                return new TalkLessResult(false,
                    $"Max messages per lap reached ({_messagesThisLap}/{maxPerLap})");
            }

            // 9. Repeat suppression with escalation logic
            var repeatKey = $"{context.CurrentCorner?.CornerName ?? "unknown"}_{decision.Category}";
            if (_repeatMistakeCount.TryGetValue(repeatKey, out var repeatCount))
            {
                if (repeatCount >= 3)
                {
                    // Escalate instead of suppress: mark as high priority
                    return new TalkLessResult(true, "Escalating: repeated mistake");
                }

                // Check if driver improved since last message on this corner/category
                if (_cornerMessageHistory.TryGetValue(repeatKey, out var history) && history.Count >= 2)
                {
                    var lastMessage = history.Peek();
                    if (!lastMessage.DriverResponded && repeatCount >= 2)
                    {
                        return new TalkLessResult(false,
                            $"Suppressed: same mistake repeated {repeatCount}x, driver not responding");
                    }
                }
            }

            // All checks passed
            return new TalkLessResult(true, "Approved for speaking");
        }
    }

    /// <summary>
    /// Called when a new lap starts to reset lap and corner counters.
    /// </summary>
    public void OnNewLap(int lapNumber)
    {
        lock (_lock)
        {
            _currentLap = lapNumber;
            _messagesThisLap = 0;
            _messagesThisCorner = 0;
            _currentCorner = "";
            _lapStartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Called when driver moves to a new corner to reset corner counter.
    /// </summary>
    public void OnNewCorner(string cornerId)
    {
        lock (_lock)
        {
            _currentCorner = cornerId;
            _messagesThisCorner = 0;
        }
    }

    /// <summary>
    /// Record that a coaching decision was spoken, updating state tracking.
    /// </summary>
    public void RecordSpoken(CoachingDecision decision)
    {
        lock (_lock)
        {
            _lastSpeakTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _messagesThisLap++;
            _messagesThisCorner++;

            // Track in corner history
            var historyKey = $"{decision.CornerName}_{decision.Category}";
            if (!_cornerMessageHistory.ContainsKey(historyKey))
            {
                _cornerMessageHistory[historyKey] = new Queue<CoachingDecision>();
            }

            _cornerMessageHistory[historyKey].Enqueue(decision);

            // Keep only last 5 messages per corner/category
            if (_cornerMessageHistory[historyKey].Count > 5)
            {
                _cornerMessageHistory[historyKey].Dequeue();
            }

            // Update repeat mistake tracking
            var repeatKey = $"{decision.CornerName}_{decision.Category}";
            if (_repeatMistakeCount.ContainsKey(repeatKey))
            {
                _repeatMistakeCount[repeatKey]++;
            }
            else
            {
                _repeatMistakeCount[repeatKey] = 1;
            }
        }
    }

    /// <summary>
    /// Record measured improvement on a corner/category to update repeat tracking.
    /// </summary>
    public void RecordImprovement(string cornerName, string category)
    {
        lock (_lock)
        {
            var repeatKey = $"{cornerName}_{category}";
            if (_repeatMistakeCount.ContainsKey(repeatKey) && _repeatMistakeCount[repeatKey] > 0)
            {
                _repeatMistakeCount[repeatKey] = 0; // Reset counter
            }
        }
    }

    /// <summary>
    /// Set minimum gap between coaching calls (milliseconds).
    /// </summary>
    public void SetMinGapMs(long ms)
    {
        lock (_lock)
        {
            _minGapBetweenCallsMs = Math.Max(1000, ms); // Min 1 second
        }
    }

    /// <summary>
    /// Get current minimum gap (milliseconds).
    /// </summary>
    public long GetMinGapMs() => _minGapBetweenCallsMs;

    /// <summary>
    /// Reset all state (emergency).
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastSpeakTimeMs = 0;
            _messagesThisLap = 0;
            _messagesThisCorner = 0;
            _currentCorner = "";
            _currentLap = 0;
            _cornerMessageHistory.Clear();
            _repeatMistakeCount.Clear();
            _cornerLastDelta.Clear();
        }
    }
}

/// <summary>
/// Result of a "should speak" decision.
/// </summary>
public record TalkLessResult(bool Allowed, string Reason);
