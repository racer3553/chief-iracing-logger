// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Data Models for SQLite Storage
// ═══════════════════════════════════════════════════════════════

namespace ChiefLogger.Data;

// ═══════════════════════════════════════
// SESSION
// ═══════════════════════════════════════

public class LoggedSession
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";       // Unique GUID
    public int IRacingSubSessionId { get; set; }
    public string DriverName { get; set; } = "";
    public int DriverId { get; set; }
    public string CarName { get; set; } = "";
    public string CarScreenName { get; set; } = "";
    public int CarId { get; set; }
    public string TrackName { get; set; } = "";
    public string TrackDisplayName { get; set; } = "";
    public int TrackId { get; set; }
    public string TrackConfig { get; set; } = "";
    public string SessionType { get; set; } = "";      // Practice, Qualify, Race
    public float AirTemp { get; set; }
    public float TrackTemp { get; set; }
    public string Skies { get; set; } = "";
    public float WindSpeed { get; set; }
    public int Humidity { get; set; }
    public string StartedAt { get; set; } = "";        // ISO 8601
    public string EndedAt { get; set; } = "";
    public int TotalLaps { get; set; }
    public float BestLapTime { get; set; }             // seconds
    public int BestLapNumber { get; set; }
    public int FinalPosition { get; set; }
    public int IncidentCount { get; set; }
    public float FuelUsedTotal { get; set; }
    public float FuelPerLap { get; set; }
    public string SetupFileId { get; set; } = "";      // Links to SetupFile
    public string Notes { get; set; } = "";
    public string SyncStatus { get; set; } = "not_synced"; // not_synced, syncing, synced, sync_failed
    public string SessionInfoYaml { get; set; } = "";
}

// ═══════════════════════════════════════
// LAP
// ═══════════════════════════════════════

public class LoggedLap
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public int LapNumber { get; set; }
    public float LapTime { get; set; }                 // seconds
    public bool IsValid { get; set; } = true;
    public int IncidentsThisLap { get; set; }
    public float FuelUsed { get; set; }
    public float FuelRemaining { get; set; }
    public float MaxSpeed { get; set; }
    public float MinSpeed { get; set; }
    public float AvgThrottle { get; set; }
    public float AvgBrake { get; set; }
    public float MaxLatG { get; set; }
    public float MaxLongG { get; set; }
    public int Position { get; set; }
    public float DeltaToBest { get; set; }
    public int GearChanges { get; set; }
    public string Flags { get; set; } = "";            // JSON: off_track, tow, etc.
}

// ═══════════════════════════════════════
// TELEMETRY SAMPLE (stored in bulk)
// ═══════════════════════════════════════

public class TelemetryRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public int LapNumber { get; set; }
    public long TimestampMs { get; set; }
    public float LapDistPct { get; set; }
    public float Speed { get; set; }
    public float Throttle { get; set; }
    public float Brake { get; set; }
    public float Steering { get; set; }
    public int Gear { get; set; }
    public float RPM { get; set; }
    public float FuelLevel { get; set; }
    public float LapTime { get; set; }
    public float Delta { get; set; }
    public float LatAccel { get; set; }
    public float LongAccel { get; set; }
    public float Yaw { get; set; }
    public float YawRate { get; set; }
    public float BrakeBias { get; set; }
    public int Position { get; set; }
    public int Incidents { get; set; }

    // Tire data as JSON strings for compact storage
    public string? TireTemps { get; set; }             // JSON: {LF:[l,m,r], RF:...}
    public string? TireWear { get; set; }              // JSON: {LF:[l,m,r], RF:...}
}

// ═══════════════════════════════════════
// SETUP FILE
// ═══════════════════════════════════════

public class SetupFile
{
    public long Id { get; set; }
    public string FileId { get; set; } = "";           // Unique GUID
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";         // Original path
    public string StoredPath { get; set; } = "";       // Path in Chief storage
    public string CarName { get; set; } = "";
    public int CarId { get; set; }
    public string TrackName { get; set; } = "";
    public int TrackId { get; set; }
    public string DetectedAt { get; set; } = "";       // ISO 8601
    public string ModifiedAt { get; set; } = "";       // File modification time
    public string RawContent { get; set; } = "";       // Full .sto file text
    public string ParsedValues { get; set; } = "";     // JSON of parsed setup values
    public string Notes { get; set; } = "";
    public string SyncStatus { get; set; } = "not_synced";
}

// ═══════════════════════════════════════
// PARSED SETUP VALUES
// ═══════════════════════════════════════

public class ParsedSetupValues
{
    // Tires
    public float? LFPressure { get; set; }
    public float? RFPressure { get; set; }
    public float? LRPressure { get; set; }
    public float? RRPressure { get; set; }

    // Springs
    public string? LFSpring { get; set; }
    public string? RFSpring { get; set; }
    public string? LRSpring { get; set; }
    public string? RRSpring { get; set; }

    // Ride heights
    public string? FrontRideHeight { get; set; }
    public string? RearRideHeight { get; set; }

    // ARB
    public string? FrontARB { get; set; }
    public string? RearARB { get; set; }

    // Brakes
    public float? BrakeBias { get; set; }

    // Steering
    public string? SteeringRatio { get; set; }

    // Aero
    public string? FrontWing { get; set; }
    public string? RearWing { get; set; }
    public string? RearSpoiler { get; set; }

    // Dampers
    public string? LFRebound { get; set; }
    public string? RFRebound { get; set; }
    public string? LRRebound { get; set; }
    public string? RRRebound { get; set; }
    public string? LFCompression { get; set; }
    public string? RFCompression { get; set; }
    public string? LRCompression { get; set; }
    public string? RRCompression { get; set; }

    // Differential
    public string? DiffPreload { get; set; }
    public string? DiffEntry { get; set; }
    public string? DiffMiddle { get; set; }
    public string? DiffExit { get; set; }

    // Gearing
    public string? FinalDrive { get; set; }
    public List<string>? GearRatios { get; set; }

    // Cross weight
    public string? CrossWeight { get; set; }

    // Camber / Toe
    public string? LFCamber { get; set; }
    public string? RFCamber { get; set; }
    public string? LRCamber { get; set; }
    public string? RRCamber { get; set; }
    public string? FrontToe { get; set; }
    public string? RearToe { get; set; }

    // Raw sections that couldn't be parsed
    public Dictionary<string, string>? RawSections { get; set; }
}

// ═══════════════════════════════════════
// COACHING EVENTS
// ═══════════════════════════════════════

public class CoachingEvent
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public int LapNumber { get; set; }
    public float LapDistPct { get; set; }
    public long TimestampMs { get; set; }
    public string EventType { get; set; } = "";        // lap_started, lap_completed, off_track, etc.
    public string Severity { get; set; } = "info";     // info, warning, critical
    public string Message { get; set; } = "";
    public string Data { get; set; } = "";             // JSON with event-specific data
}

// ═══════════════════════════════════════
// SYNC QUEUE
// ═══════════════════════════════════════

public class SyncQueueItem
{
    public long Id { get; set; }
    public string ItemType { get; set; } = "";         // session, lap, telemetry, setup
    public string ItemId { get; set; } = "";
    public string Status { get; set; } = "pending";    // pending, syncing, synced, failed
    public int RetryCount { get; set; }
    public string LastError { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string LastAttemptAt { get; set; } = "";
}

// ═══════════════════════════════════════
// APP SETTINGS (stored in DB)
// ═══════════════════════════════════════

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

// ═══════════════════════════════════════
// TRACK MAPS (stored in SQLite)
// ═══════════════════════════════════════

public class TrackMapRecord
{
    public long Id { get; set; }
    public string TrackName { get; set; } = "";
    public int TrackId { get; set; }
    public string CarClass { get; set; } = "";
    public string CarName { get; set; } = "";
    public float TrackLengthKm { get; set; }
    public string Source { get; set; } = "manual";    // manual, auto_detected, imported
    public string CornersJson { get; set; } = "";     // JSON array of corners
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

// ═══════════════════════════════════════
// CORNER PERFORMANCE (per-corner metrics)
// ═══════════════════════════════════════

public class CornerPerformanceRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string CornerId { get; set; } = "";
    public int LapNumber { get; set; }
    public float BrakeStartDistPct { get; set; }
    public float PeakBrakePressure { get; set; }
    public float BrakeReleaseRate { get; set; }
    public float EntrySpeed { get; set; }
    public float ApexSpeed { get; set; }
    public float MinSpeed { get; set; }
    public float ThrottlePickupDistPct { get; set; }
    public float ExitSpeed { get; set; }
    public int GearAtEntry { get; set; }
    public int GearAtApex { get; set; }
    public int SteeringCorrections { get; set; }
    public float DeltaGainedLost { get; set; }
    public string MistakeCategory { get; set; } = "";
}

// ═══════════════════════════════════════
// COACHING INSTRUCTIONS
// ═══════════════════════════════════════

public class CoachingInstructionRecord
{
    public long Id { get; set; }
    public string CornerId { get; set; } = "";
    public string MistakeCategory { get; set; } = "";
    public string InstructionText { get; set; } = "";
    public float ConfidenceScore { get; set; } = 0.5f;
    public int TimesGiven { get; set; }
    public int TimesImproved { get; set; }
    public int TimesWorsened { get; set; }
    public int TimesNoChange { get; set; }
}

// ═══════════════════════════════════════
// COACHING OUTCOMES
// ═══════════════════════════════════════

public class CoachingOutcomeRecord
{
    public long Id { get; set; }
    public string InstructionId { get; set; } = "";
    public string CornerId { get; set; } = "";
    public int LapGiven { get; set; }
    public int LapResult { get; set; }
    public float DeltaBefore { get; set; }
    public float DeltaAfter { get; set; }
    public string Result { get; set; } = "";          // improved, worsened, no_change
}

// ═══════════════════════════════════════
// VOICE CALL HISTORY
// ═══════════════════════════════════════

public class VoiceCallHistoryRecord
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string CornerId { get; set; } = "";
    public int LapNumber { get; set; }
    public string CallText { get; set; } = "";
    public string CallType { get; set; } = "";        // predictive, reactive, callout
    public long TimestampMs { get; set; }
}
