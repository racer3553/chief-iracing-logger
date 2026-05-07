// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Track Map
// Collection of corners for a specific track and car combination.
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Core;

/// <summary>
/// Represents a complete corner map for a track and car combination.
/// Contains all corners and provides lookup/navigation methods.
/// </summary>
public class TrackMap
{
    /// <summary>Unique identifier for this track map</summary>
    public string Id { get; set; } = "";

    /// <summary>Track name (internal identifier)</summary>
    public string TrackName { get; set; } = "";

    /// <summary>Track display name (user-facing)</summary>
    public string TrackDisplayName { get; set; } = "";

    /// <summary>iRacing track ID</summary>
    public int TrackId { get; set; }

    /// <summary>Car class this map applies to (GT3, LMP2, etc.)</summary>
    public string CarClass { get; set; } = "";

    /// <summary>Specific car name (optional, empty if applies to all cars in class)</summary>
    public string CarName { get; set; } = "";

    /// <summary>Track length in kilometers</summary>
    public float TrackLengthKm { get; set; }

    /// <summary>Collection of all corners on this track</summary>
    public List<TrackCorner> Corners { get; set; } = new();

    /// <summary>
    /// Source of this map: manual (user-created), auto_detected (from telemetry),
    /// or imported (from file)
    /// </summary>
    public string Source { get; set; } = "manual";

    /// <summary>ISO 8601 creation timestamp</summary>
    public string CreatedAt { get; set; } = "";

    /// <summary>ISO 8601 last update timestamp</summary>
    public string UpdatedAt { get; set; } = "";

    // ═══════════════════════════════════════
    // LOOKUP METHODS
    // ═══════════════════════════════════════

    /// <summary>
    /// Find the next corner after the given lap distance percentage.
    /// Wraps around at the start/finish line (100% to 0%).
    /// </summary>
    /// <param name="currentDistPct">Current position as lap distance percentage (0.0-1.0)</param>
    /// <returns>The next corner, or null if no corners exist</returns>
    public TrackCorner? GetNextCorner(float currentDistPct)
    {
        if (Corners.Count == 0) return null;

        // Sort by start distance for reliable iteration
        var sortedCorners = Corners.OrderBy(c => c.StartDistPct).ToList();

        // Find first corner that starts after current position
        var nextCorner = sortedCorners.FirstOrDefault(c => c.StartDistPct > currentDistPct);

        // If none found, wrap around to first corner
        return nextCorner ?? sortedCorners.FirstOrDefault();
    }

    /// <summary>
    /// Find the corner whose zone contains the given lap distance percentage.
    /// </summary>
    /// <param name="currentDistPct">Current position as lap distance percentage (0.0-1.0)</param>
    /// <returns>The corner containing this position, or null if between corners</returns>
    public TrackCorner? GetCurrentCorner(float currentDistPct)
    {
        if (Corners.Count == 0) return null;

        return Corners.FirstOrDefault(c =>
            currentDistPct >= c.StartDistPct && currentDistPct <= c.ExitDistPct);
    }

    /// <summary>
    /// Get corner by sequential number.
    /// </summary>
    /// <param name="number">Sequential corner number (1-based)</param>
    /// <returns>The corner, or null if not found</returns>
    public TrackCorner? GetCorner(int number)
    {
        return Corners.FirstOrDefault(c => c.CornerNumber == number);
    }

    /// <summary>
    /// Calculate distance to the next brake zone from the current position,
    /// accounting for wrap-around at the start/finish line.
    /// </summary>
    /// <param name="currentDistPct">Current position as lap distance percentage (0.0-1.0)</param>
    /// <returns>Distance as lap percentage (0.0-1.0)</returns>
    public float DistanceToNextBrakeZone(float currentDistPct)
    {
        if (Corners.Count == 0) return 0f;

        // Normalize input to 0.0-1.0 range
        currentDistPct = currentDistPct % 1f;

        // Sort by brake zone distance for reliable iteration
        var sortedCorners = Corners.OrderBy(c => c.BrakeZoneDistPct).ToList();

        // Find first brake zone that occurs after current position
        var nextBrakeZone = sortedCorners.FirstOrDefault(c => c.BrakeZoneDistPct > currentDistPct);

        if (nextBrakeZone != null)
        {
            // Brake zone is ahead in this lap
            return nextBrakeZone.BrakeZoneDistPct - currentDistPct;
        }

        // Wrap to next lap: distance to first brake zone + distance to end of lap
        var firstBrakeZone = sortedCorners.FirstOrDefault();
        if (firstBrakeZone != null)
        {
            return (1f - currentDistPct) + firstBrakeZone.BrakeZoneDistPct;
        }

        return 0f;
    }

    /// <summary>
    /// Calculate seconds to the next brake zone at the given speed.
    /// </summary>
    /// <param name="currentDistPct">Current position as lap distance percentage (0.0-1.0)</param>
    /// <param name="speedMs">Current speed in meters per second</param>
    /// <param name="trackLengthM">Track length in meters</param>
    /// <returns>Seconds until next brake zone, or float.MaxValue if speed is 0</returns>
    public float SecondsToNextBrakeZone(float currentDistPct, float speedMs, float trackLengthM)
    {
        if (Math.Abs(speedMs) < 0.01f) return float.MaxValue; // Avoid division by zero

        // Get distance in lap percentage
        float distPct = DistanceToNextBrakeZone(currentDistPct);

        // Convert to meters
        float distMeters = distPct * trackLengthM;

        // Calculate seconds
        return distMeters / speedMs;
    }
}
