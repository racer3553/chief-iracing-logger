// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Coaching Decision Model
// Represents every coaching intervention Chief makes, with full
// context, priority, suppression tracking, and outcome measurement.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core.Elite;

public class CoachingDecision
{
    /// <summary>
    /// Unique identifier for this decision (first 12 chars of GUID).
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// When this decision was made (UTC milliseconds since epoch).
    /// </summary>
    public long TimestampMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Vehicle class (e.g., "Ford GTE", "McLaren GT3").
    /// </summary>
    public string Car { get; set; } = "";

    /// <summary>
    /// Track name (e.g., "Monza", "Nurburgring GP").
    /// </summary>
    public string Track { get; set; } = "";

    /// <summary>
    /// Corner name where coaching applies (e.g., "Eau Rouge", "Apex 1").
    /// </summary>
    public string CornerName { get; set; } = "";

    /// <summary>
    /// Corner number in track map.
    /// </summary>
    public int CornerNumber { get; set; }

    /// <summary>
    /// Lap number when decision was made.
    /// </summary>
    public int LapNumber { get; set; }

    /// <summary>
    /// Track distance percentage when decision was made (0-100).
    /// </summary>
    public float LapDistPct { get; set; }

    /// <summary>
    /// Session type: practice, qualifying, race, testing.
    /// </summary>
    public string SessionType { get; set; } = "practice";

    /// <summary>
    /// Coaching category: braking, throttle, steering, line, setup,
    /// hardware, tire, racecraft, mental.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Full coaching message for detailed analysis.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Short voice text for actual delivery (max 10 words in corners, 15 on straights).
    /// </summary>
    public string VoiceText { get; set; } = "";

    /// <summary>
    /// Priority level for decision ordering.
    /// </summary>
    public CoachingPriority Priority { get; set; } = CoachingPriority.Low;

    /// <summary>
    /// Confidence in this advice (0-100).
    /// </summary>
    public int ConfidenceScore { get; set; }

    /// <summary>
    /// How many seconds before the next corner this should be spoken.
    /// 0 = immediately.
    /// </summary>
    public float SpeakBeforeCornerSeconds { get; set; }

    /// <summary>
    /// Reason if this decision was suppressed (empty = not suppressed).
    /// </summary>
    public string SuppressReason { get; set; } = "";

    /// <summary>
    /// True if this decision was actually spoken to driver.
    /// </summary>
    public bool WasSpoken { get; set; }

    /// <summary>
    /// True if this decision was suppressed before attempt to speak.
    /// </summary>
    public bool WasSuppressed { get; set; }

    /// <summary>
    /// Expected time gain in seconds if driver acts on this advice.
    /// </summary>
    public float ExpectedDeltaGain { get; set; }

    /// <summary>
    /// Type of action required: driver, setup, hardware, strategy.
    /// </summary>
    public string RecommendedActionType { get; set; } = "driver";

    /// <summary>
    /// Actual delta change in the lap following coaching (measured from
    /// actual lap vs best lap). Set during post-lap analysis.
    /// </summary>
    public float ActualDeltaChange { get; set; }

    /// <summary>
    /// Outcome after coaching: improved, worsened, no_change, unknown.
    /// </summary>
    public string Outcome { get; set; } = "";

    /// <summary>
    /// True if driver made measurable improvement on this specific point
    /// in the lap immediately following coaching.
    /// </summary>
    public bool DriverResponded { get; set; }

    /// <summary>
    /// Session ID this decision belongs to.
    /// </summary>
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Reference to the lap in the database (if saved).
    /// </summary>
    public string LapId { get; set; } = "";
}

/// <summary>
/// Coaching priority levels. Used for deciding what gets spoken and
/// in what order. Higher priority suppresses lower priority messages.
/// </summary>
public enum CoachingPriority
{
    /// <summary>
    /// Safety-critical: wreck, car nearby, dangerous situation.
    /// Expiration: 10s. Always speaks unless talking.
    /// </summary>
    Critical = 0,

    /// <summary>
    /// Racecraft: defend position, pass opportunity, strategic decision.
    /// Expiration: 8s. Suppresses Medium/Low/Minimal.
    /// </summary>
    High = 1,

    /// <summary>
    /// Predictive corner instruction: braking point, turn-in, apex.
    /// Expiration: 6s. Can be suppressed by Critical/High.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Tire/fuel strategy, one-lap-fix suggestions.
    /// Expiration: 5s. Easily suppressed.
    /// </summary>
    Low = 3,

    /// <summary>
    /// Hardware/setup recommendation, praise, motivation.
    /// Expiration: 4s. Most easily suppressed.
    /// </summary>
    Minimal = 4
}
