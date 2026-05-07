// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Coaching Memory
// Persistent memory of what coaching worked.
// NOT an ICoachingModule — utility for outcome tracking.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Memory entry tracking a coaching instruction and its outcome.
/// </summary>
public class MemoryEntry
{
    /// <summary>
    /// Unique instruction ID.
    /// </summary>
    public string InstructionId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Corner where instruction was given.
    /// </summary>
    public string CornerId { get; set; } = "";

    /// <summary>
    /// Vehicle class (e.g., "GT3", "GTE").
    /// </summary>
    public string Car { get; set; } = "";

    /// <summary>
    /// Track name (e.g., "Road America").
    /// </summary>
    public string Track { get; set; } = "";

    /// <summary>
    /// Coaching category (braking, steering, line, etc).
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// The actual instruction text given to driver.
    /// </summary>
    public string InstructionText { get; set; } = "";

    /// <summary>
    /// Lap number when instruction was given.
    /// </summary>
    public int LapGiven { get; set; }

    /// <summary>
    /// Delta to best lap when instruction was given.
    /// </summary>
    public float DeltaBefore { get; set; }

    /// <summary>
    /// Delta to best lap after instruction took effect.
    /// </summary>
    public float DeltaAfter { get; set; }

    /// <summary>
    /// Input quality metric before instruction (0-1).
    /// </summary>
    public float InputQualityBefore { get; set; }

    /// <summary>
    /// Input quality metric after instruction (0-1).
    /// </summary>
    public float InputQualityAfter { get; set; }

    /// <summary>
    /// Outcome: improved, worsened, no_change.
    /// </summary>
    public string Result { get; set; } = "";

    /// <summary>
    /// Confidence adjustment based on result.
    /// </summary>
    public float ConfidenceAdjustment { get; set; }

    /// <summary>
    /// Whether to keep recommending this instruction in future.
    /// </summary>
    public bool KeepRecommendation { get; set; }

    /// <summary>
    /// Timestamp when instruction was recorded.
    /// </summary>
    public long TimestampMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Coaching memory system. Tracks instruction outcomes to build
/// confidence scores for future recommendations.
/// </summary>
public class CoachingMemory
{
    private readonly ChiefDatabase _db;
    private readonly object _lock = new();
    private List<MemoryEntry> _sessionMemory = new();
    private Dictionary<string, float> _cornerConfidenceCache = new();
    private MemoryEntry? _lastRecordedInstruction;

    public CoachingMemory(ChiefDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Record that an instruction was given to the driver.
    /// Call this immediately after submitting a coaching decision.
    /// </summary>
    public void RecordInstructionGiven(CoachingDecision decision)
    {
        if (decision == null)
            return;

        lock (_lock)
        {
            var entry = new MemoryEntry
            {
                InstructionId = decision.Id,
                CornerId = decision.CornerName,
                Car = decision.Car,
                Track = decision.Track,
                Category = decision.Category,
                InstructionText = decision.VoiceText,
                LapGiven = decision.LapNumber,
                DeltaBefore = decision.ExpectedDeltaGain, // Placeholder; would be updated on completion
                Result = "pending",
            };

            _sessionMemory.Add(entry);
            _lastRecordedInstruction = entry;
        }
    }

    /// <summary>
    /// Evaluate the outcome of the last instruction given.
    /// Call this after the lap following the instruction completes.
    /// </summary>
    public void EvaluateLastInstruction(string cornerId, int currentLap, float currentDelta, float inputQuality)
    {
        lock (_lock)
        {
            if (_lastRecordedInstruction == null)
                return;

            float deltaDifference = _lastRecordedInstruction.DeltaBefore - currentDelta;

            // Determine result
            if (deltaDifference > 0.1f) // Improved by more than 100ms
            {
                _lastRecordedInstruction.Result = "improved";
                _lastRecordedInstruction.ConfidenceAdjustment = 0.1f;
                _lastRecordedInstruction.KeepRecommendation = true;
            }
            else if (deltaDifference < -0.1f) // Worsened by more than 100ms
            {
                _lastRecordedInstruction.Result = "worsened";
                _lastRecordedInstruction.ConfidenceAdjustment = -0.15f;
                _lastRecordedInstruction.KeepRecommendation = false;
            }
            else
            {
                _lastRecordedInstruction.Result = "no_change";
                _lastRecordedInstruction.ConfidenceAdjustment = -0.02f;
                _lastRecordedInstruction.KeepRecommendation = true;
            }

            _lastRecordedInstruction.DeltaAfter = currentDelta;
            _lastRecordedInstruction.InputQualityAfter = inputQuality;
        }
    }

    /// <summary>
    /// Get the current confidence score for a specific corner + category + instruction.
    /// Returns value 0-100 based on historical success.
    /// </summary>
    public float GetConfidence(string cornerId, string category, string instruction)
    {
        lock (_lock)
        {
            string cacheKey = $"{cornerId}_{category}_{instruction}".ToLower();
            if (_cornerConfidenceCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            // Calculate from session memory
            var relevant = _sessionMemory.Where(m =>
                m.CornerId.Equals(cornerId, StringComparison.OrdinalIgnoreCase) &&
                m.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                m.InstructionText.Equals(instruction, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (relevant.Count == 0)
                return 70f; // Default confidence for new instructions

            // Calculate weighted average
            float improved = relevant.Count(m => m.Result == "improved");
            float total = relevant.Count;
            float successRate = improved / total;

            float confidence = 50f + (successRate * 40f); // Range 50-90
            _cornerConfidenceCache[cacheKey] = confidence;
            return confidence;
        }
    }

    /// <summary>
    /// Get the best (highest confidence) instruction for a specific corner/category.
    /// </summary>
    public string? GetBestInstruction(string cornerId, string category)
    {
        lock (_lock)
        {
            var relevant = _sessionMemory.Where(m =>
                m.CornerId.Equals(cornerId, StringComparison.OrdinalIgnoreCase) &&
                m.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                m.KeepRecommendation
            ).ToList();

            if (relevant.Count == 0)
                return null;

            // Return the most frequently successful instruction
            var bestGroup = relevant
                .GroupBy(m => m.InstructionText)
                .OrderByDescending(g => g.Count(m => m.Result == "improved"))
                .FirstOrDefault();

            return bestGroup?.Key;
        }
    }

    /// <summary>
    /// Get all memory entries from the current session.
    /// </summary>
    public List<MemoryEntry> GetSessionMemory()
    {
        lock (_lock)
        {
            return new List<MemoryEntry>(_sessionMemory);
        }
    }

    /// <summary>
    /// Get historical memory entries for a specific car/track combination.
    /// </summary>
    public List<MemoryEntry> GetCarTrackMemory(string car, string track)
    {
        lock (_lock)
        {
            return _sessionMemory.Where(m =>
                m.Car.Equals(car, StringComparison.OrdinalIgnoreCase) &&
                m.Track.Equals(track, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
    }

    /// <summary>
    /// Clear all session memory (start of new session).
    /// </summary>
    public void ClearSessionMemory()
    {
        lock (_lock)
        {
            _sessionMemory.Clear();
            _cornerConfidenceCache.Clear();
            _lastRecordedInstruction = null;
        }
    }

    /// <summary>
    /// Persist current session memory to database.
    /// Called at session end.
    /// </summary>
    public void PersistSessionMemory(string sessionId)
    {
        lock (_lock)
        {
            try
            {
                foreach (var entry in _sessionMemory)
                {
                    _db.InsertMemoryEntry(new MemoryEntryRecord
                    {
                        SessionId = sessionId,
                        InstructionId = entry.InstructionId,
                        CornerId = entry.CornerId,
                        Car = entry.Car,
                        Track = entry.Track,
                        Category = entry.Category,
                        InstructionText = entry.InstructionText,
                        LapGiven = entry.LapGiven,
                        DeltaBefore = entry.DeltaBefore,
                        DeltaAfter = entry.DeltaAfter,
                        Result = entry.Result,
                        ConfidenceAdjustment = entry.ConfidenceAdjustment,
                        KeepRecommendation = entry.KeepRecommendation,
                        CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    });
                }
            }
            catch
            {
                // Silent persistence error
            }
        }
    }

    /// <summary>
    /// Get the count of instructions in session memory.
    /// </summary>
    public int MemoryCount
    {
        get
        {
            lock (_lock)
            {
                return _sessionMemory.Count;
            }
        }
    }
}

/// <summary>
/// Database model for persisting memory entries.
/// </summary>
public class MemoryEntryRecord
{
    public string SessionId { get; set; } = "";
    public string InstructionId { get; set; } = "";
    public string CornerId { get; set; } = "";
    public string Car { get; set; } = "";
    public string Track { get; set; } = "";
    public string Category { get; set; } = "";
    public string InstructionText { get; set; } = "";
    public int LapGiven { get; set; }
    public float DeltaBefore { get; set; }
    public float DeltaAfter { get; set; }
    public string Result { get; set; } = "";
    public float ConfidenceAdjustment { get; set; }
    public bool KeepRecommendation { get; set; }
    public string CreatedAt { get; set; } = "";
}
