// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Track Corner Data Model
// Represents a single corner on a track with targets and coaching info.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core;

/// <summary>
/// Data model for a single corner on a track.
/// Contains geometric positions, target values, and coaching information.
/// </summary>
public class TrackCorner
{
    /// <summary>Unique identifier for this corner</summary>
    public string Id { get; set; } = "";

    /// <summary>Name of the track this corner belongs to</summary>
    public string TrackName { get; set; } = "";

    /// <summary>Car class (GT3, LMP2, etc.)</summary>
    public string CarClass { get; set; } = "";

    /// <summary>Specific car name (optional, empty if applies to all cars in class)</summary>
    public string CarName { get; set; } = "";

    /// <summary>Corner name/identifier (e.g., "Turn 5", "Hairpin", "Eau Rouge")</summary>
    public string CornerName { get; set; } = "";

    /// <summary>Sequential corner number on the track</summary>
    public int CornerNumber { get; set; }

    // ═══════════════════════════════════════
    // TRACK POSITIONS (0.0 - 1.0 lap distance %)
    // ═══════════════════════════════════════

    /// <summary>Lap distance percentage where corner zone begins</summary>
    public float StartDistPct { get; set; }

    /// <summary>Lap distance percentage where braking should start</summary>
    public float BrakeZoneDistPct { get; set; }

    /// <summary>Lap distance percentage where turn-in occurs</summary>
    public float TurnInDistPct { get; set; }

    /// <summary>Lap distance percentage of apex</summary>
    public float ApexDistPct { get; set; }

    /// <summary>Lap distance percentage where corner zone ends</summary>
    public float ExitDistPct { get; set; }

    // ═══════════════════════════════════════
    // TARGET VALUES
    // ═══════════════════════════════════════

    /// <summary>Brake marker description (e.g., "3 board", "100m", "turn-in point")</summary>
    public string BrakeMarker { get; set; } = "";

    /// <summary>Target brake pressure (0-100%)</summary>
    public float TargetBrakePressure { get; set; }

    /// <summary>Target entry speed (mph)</summary>
    public float TargetEntrySpeed { get; set; }

    /// <summary>Target minimum speed at apex (mph)</summary>
    public float TargetMinSpeed { get; set; }

    /// <summary>Target gear at entry</summary>
    public int TargetGear { get; set; }

    /// <summary>Lap distance percentage where throttle application begins</summary>
    public float TargetThrottlePickup { get; set; }

    /// <summary>Target exit speed (mph)</summary>
    public float TargetExitSpeed { get; set; }

    // ═══════════════════════════════════════
    // COACHING
    // ═══════════════════════════════════════

    /// <summary>Default voice coaching text (used when no performance data available)</summary>
    public string DefaultVoiceCall { get; set; } = "";

    /// <summary>Additional notes about this corner</summary>
    public string Notes { get; set; } = "";

    // ═══════════════════════════════════════
    // CLASSIFICATION
    // ═══════════════════════════════════════

    /// <summary>
    /// Corner type classification.
    /// Values: hairpin, fast_sweeper, chicane, heavy_brake, light_brake, flat_out, kink
    /// </summary>
    public string CornerType { get; set; } = "";

    /// <summary>True if left turn, false if right turn</summary>
    public bool IsLeftTurn { get; set; }
}
