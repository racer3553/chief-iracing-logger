// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Coaching Priority System
// Manages a priority queue of pending coaching decisions.
// Prevents lower-priority messages from interrupting higher-priority
// ones. Thread-safe with expiration-based cleanup.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public class CoachingPrioritySystem
{
    private readonly object _lock = new();
    private readonly SortedDictionary<int, Queue<CoachingDecision>> _priorityQueues;
    private readonly Dictionary<string, long> _decisionExpiryMs = new();

    // Expiration times in milliseconds
    private readonly Dictionary<CoachingPriority, long> _expiryDurations = new()
    {
        { CoachingPriority.Critical, 10_000 },  // 10 seconds
        { CoachingPriority.High, 8_000 },       // 8 seconds
        { CoachingPriority.Medium, 6_000 },     // 6 seconds
        { CoachingPriority.Low, 5_000 },        // 5 seconds
        { CoachingPriority.Minimal, 4_000 }     // 4 seconds
    };

    public CoachingPrioritySystem()
    {
        _priorityQueues = new SortedDictionary<int, Queue<CoachingDecision>>(
            Comparer<int>.Create((a, b) => a.CompareTo(b))  // Ascending = lower number (higher priority) comes first
        );

        // Initialize queues for each priority level
        foreach (var priority in Enum.GetValues<CoachingPriority>())
        {
            _priorityQueues[(int)priority] = new Queue<CoachingDecision>();
        }
    }

    /// <summary>
    /// Submit a coaching decision to the priority queue.
    /// </summary>
    public void Submit(CoachingDecision decision)
    {
        if (decision == null)
            return;

        lock (_lock)
        {
            var priorityIndex = (int)decision.Priority;
            _priorityQueues[priorityIndex].Enqueue(decision);

            // Set expiry time
            var expiryTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                             _expiryDurations[decision.Priority];
            _decisionExpiryMs[decision.Id] = expiryTime;
        }
    }

    /// <summary>
    /// Get the next highest-priority non-expired decision.
    /// Returns null if no decision is available.
    /// </summary>
    public CoachingDecision? GetNext()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Iterate through priority levels (0 = highest)
            for (int priority = 0; priority <= (int)CoachingPriority.Minimal; priority++)
            {
                if (!_priorityQueues.ContainsKey(priority))
                    continue;

                var queue = _priorityQueues[priority];

                // Clean expired decisions from this queue
                while (queue.Count > 0)
                {
                    var decision = queue.Peek();
                    if (_decisionExpiryMs.TryGetValue(decision.Id, out var expiry) && now >= expiry)
                    {
                        // Expired, remove it
                        queue.Dequeue();
                        _decisionExpiryMs.Remove(decision.Id);
                    }
                    else
                    {
                        // Found a non-expired decision at this priority
                        return queue.Dequeue();
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Check if a Critical or High priority decision is pending.
    /// </summary>
    public bool IsHighPriorityPending()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Check Critical
            if (_priorityQueues[(int)CoachingPriority.Critical].Count > 0)
            {
                var critical = _priorityQueues[(int)CoachingPriority.Critical].Peek();
                if (_decisionExpiryMs.TryGetValue(critical.Id, out var expiry) && now < expiry)
                    return true;
            }

            // Check High
            if (_priorityQueues[(int)CoachingPriority.High].Count > 0)
            {
                var high = _priorityQueues[(int)CoachingPriority.High].Peek();
                if (_decisionExpiryMs.TryGetValue(high.Id, out var expiry) && now < expiry)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Flush (remove) all decisions below a certain priority level.
    /// For example, Flush(CoachingPriority.High) removes all Medium/Low/Minimal.
    /// </summary>
    public void Flush(CoachingPriority belowLevel)
    {
        lock (_lock)
        {
            var flushFromPriority = (int)belowLevel + 1;

            for (int priority = flushFromPriority; priority <= (int)CoachingPriority.Minimal; priority++)
            {
                if (_priorityQueues.ContainsKey(priority))
                {
                    var queue = _priorityQueues[priority];
                    while (queue.Count > 0)
                    {
                        var decision = queue.Dequeue();
                        _decisionExpiryMs.Remove(decision.Id);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get the count of pending (non-expired) decisions across all priorities.
    /// </summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                int count = 0;

                foreach (var queue in _priorityQueues.Values)
                {
                    // Count non-expired decisions in each queue
                    foreach (var decision in queue)
                    {
                        if (_decisionExpiryMs.TryGetValue(decision.Id, out var expiry) && now < expiry)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// Clear all pending decisions (emergency reset).
    /// </summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            foreach (var queue in _priorityQueues.Values)
            {
                queue.Clear();
            }
            _decisionExpiryMs.Clear();
        }
    }

    /// <summary>
    /// Get a snapshot of all pending (non-expired) decisions, ordered by priority.
    /// </summary>
    public List<CoachingDecision> GetAllPending()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = new List<CoachingDecision>();

            for (int priority = 0; priority <= (int)CoachingPriority.Minimal; priority++)
            {
                if (!_priorityQueues.ContainsKey(priority))
                    continue;

                var queue = _priorityQueues[priority];
                foreach (var decision in queue)
                {
                    if (_decisionExpiryMs.TryGetValue(decision.Id, out var expiry) && now < expiry)
                    {
                        result.Add(decision);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Remove a specific decision by ID (if it exists and hasn't expired).
    /// </summary>
    public bool RemoveById(string decisionId)
    {
        lock (_lock)
        {
            for (int priority = 0; priority <= (int)CoachingPriority.Minimal; priority++)
            {
                if (!_priorityQueues.ContainsKey(priority))
                    continue;

                var queue = _priorityQueues[priority];
                var decisions = queue.ToList();

                for (int i = 0; i < decisions.Count; i++)
                {
                    if (decisions[i].Id == decisionId)
                    {
                        // Rebuild queue without this decision
                        queue.Clear();
                        for (int j = 0; j < decisions.Count; j++)
                        {
                            if (j != i)
                            {
                                queue.Enqueue(decisions[j]);
                            }
                        }
                        _decisionExpiryMs.Remove(decisionId);
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
