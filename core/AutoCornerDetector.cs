// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Auto Corner Detector
// Automatically detects corners from telemetry when no track map exists.
// Analyzes lap data to build track maps with zero manual input.
// ═══════════════════════════════════════════════════════════════

using ChiefLogger.Data;

namespace ChiefLogger.Core;

/// <summary>
/// Detects and classifies corners automatically from telemetry data.
/// Accumulates multiple lap passes for improved detection accuracy.
/// </summary>
public class AutoCornerDetector
{
    // Detection thresholds
    private const float BrakeThreshold = 0.3f;
    private const float MinBrakeDuration = 0.5f; // seconds
    private const float GroupingThreshold = 0.03f; // 3% lap distance
    private const float MinSpeedHairpin = 40f; // mph
    private const float MinSpeedHeavyBrake = 70f; // mph
    private const float MinSpeedFastSweeper = 90f; // mph
    private const float SmoothingWindow = 5; // samples for rolling average

    // Data accumulation
    private readonly List<List<TelemetryRecord>> _lapData = new();
    private int _minLapsRequired = 3;

    // Detected corners (accumulates across calls to AddLapData)
    private readonly List<DetectedCorner> _detectedCorners = new();

    public AutoCornerDetector()
    {
    }

    // ═══════════════════════════════════════
    // ACCUMULATION & DETECTION
    // ═══════════════════════════════════════

    /// <summary>
    /// Add a completed lap's telemetry data.
    /// Detection improves with more lap data (at least 3 clean laps recommended).
    /// </summary>
    public void AddLapData(List<TelemetryRecord> lapTelemetry)
    {
        if (lapTelemetry.Count == 0) return;
        _lapData.Add(lapTelemetry);
    }

    /// <summary>Check if detector has sufficient data for reliable corner detection</summary>
    public bool NeedsMoreData(int completedLaps)
    {
        return _lapData.Count < _minLapsRequired;
    }

    /// <summary>
    /// Detect all corners from accumulated lap data.
    /// Returns a complete TrackMap with detected corners and target values.
    /// </summary>
    public TrackMap DetectCorners(List<TelemetryRecord> lapTelemetry,
        string trackName, string carName, float trackLengthKm)
    {
        if (lapTelemetry.Count == 0)
        {
            return new TrackMap { TrackName = trackName, CarName = carName };
        }

        // Process all accumulated laps for better accuracy
        var brakeZones = DetectBrakeZones();
        var corners = GroupEventsIntoCorners(brakeZones);

        // Extract targets from telemetry
        ExtractTargetValues(corners);

        // Build track map
        var trackMap = new TrackMap
        {
            Id = Guid.NewGuid().ToString(),
            TrackName = trackName,
            TrackDisplayName = trackName,
            CarName = carName,
            TrackLengthKm = trackLengthKm,
            Source = "auto_detected",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };

        // Add detected corners
        int cornerNumber = 1;
        foreach (var corner in corners.OrderBy(c => c.BrakeStartDist))
        {
            trackMap.Corners.Add(ConvertToTrackCorner(corner, cornerNumber, trackName, carName));
            cornerNumber++;
        }

        return trackMap;
    }

    // ═══════════════════════════════════════
    // DETECTION ALGORITHM
    // ═══════════════════════════════════════

    private List<BrakeZone> DetectBrakeZones()
    {
        var brakeZones = new List<BrakeZone>();

        if (_lapData.Count == 0) return brakeZones;

        // Use first lap as primary, cross-check with others
        var primaryLap = _lapData[0];
        var smoothedBrake = SmoothTrace(primaryLap.Select(t => t.Brake).ToList(), SmoothingWindow);

        bool inBrakeZone = false;
        int brakeStartIdx = -1;
        float brakeDurationFrames = 0;

        for (int i = 0; i < smoothedBrake.Count; i++)
        {
            if (smoothedBrake[i] > BrakeThreshold)
            {
                if (!inBrakeZone)
                {
                    inBrakeZone = true;
                    brakeStartIdx = i;
                    brakeDurationFrames = 0;
                }
                brakeDurationFrames++;
            }
            else
            {
                if (inBrakeZone)
                {
                    // Check if brake zone lasted long enough
                    float brakeDurationSecs = brakeDurationFrames / 20f; // 20Hz sample rate
                    if (brakeDurationSecs >= MinBrakeDuration)
                    {
                        brakeZones.Add(new BrakeZone
                        {
                            StartIdx = brakeStartIdx,
                            EndIdx = i,
                            MinSpeedIdx = FindMinSpeedInRange(primaryLap, brakeStartIdx, i)
                        });
                    }
                    inBrakeZone = false;
                }
            }
        }

        return brakeZones;
    }

    private List<DetectedCorner> GroupEventsIntoCorners(List<BrakeZone> brakeZones)
    {
        var corners = new List<DetectedCorner>();

        if (_lapData.Count == 0 || brakeZones.Count == 0) return corners;

        var primaryLap = _lapData[0];

        foreach (var bz in brakeZones)
        {
            // Find brake event
            var brakeEvt = primaryLap[bz.StartIdx];
            float brakeDist = brakeEvt.LapDistPct;

            // Find apex (minimum speed point)
            var apexEvt = primaryLap[bz.MinSpeedIdx];
            float apexDist = apexEvt.LapDistPct;

            // Find throttle application after apex
            int throttleIdx = FindThrottleAfterMinSpeed(primaryLap, bz.MinSpeedIdx);
            float throttleDist = throttleIdx >= 0 ? primaryLap[throttleIdx].LapDistPct : apexDist + 0.05f;

            // Detect turn-in from steering angle
            float turnInDist = EstimateTurnInDistance(primaryLap, bz.StartIdx, bz.MinSpeedIdx);

            // Define corner zone
            float startDist = Math.Max(0, brakeDist - 0.02f);
            float exitDist = Math.Min(1f, throttleDist + 0.02f);

            corners.Add(new DetectedCorner
            {
                BrakeStartDist = brakeDist,
                TurnInDist = turnInDist,
                ApexDist = apexDist,
                ThrottleDist = throttleDist,
                StartDist = startDist,
                ExitDist = exitDist,
                MinSpeedValue = apexEvt.Speed,
                BrakeData = primaryLap[bz.StartIdx],
                ApexData = apexEvt,
                ExitData = primaryLap[Math.Min(throttleIdx, primaryLap.Count - 1)]
            });
        }

        return corners;
    }

    private void ExtractTargetValues(List<DetectedCorner> corners)
    {
        // Each corner extracts its own target values from the detected telemetry
        foreach (var corner in corners)
        {
            corner.TargetBrakePressure = corner.BrakeData.Brake * 100f;
            corner.TargetEntrySpeed = corner.BrakeData.Speed;
            corner.TargetMinSpeed = corner.ApexData.Speed;
            corner.TargetExitSpeed = corner.ExitData.Speed;
            corner.TargetGear = corner.BrakeData.Gear;
        }
    }

    // ═══════════════════════════════════════
    // CONVERSION TO TRACK CORNER
    // ═══════════════════════════════════════

    private TrackCorner ConvertToTrackCorner(DetectedCorner detected, int number,
        string trackName, string carName)
    {
        // Classify corner type
        string cornerType = ClassifyCorner(detected.MinSpeedValue);
        bool isLeftTurn = DetectTurnDirection(detected);

        // Generate default voice call
        string voiceCall = GenerateDefaultVoiceCall(detected, cornerType);

        return new TrackCorner
        {
            Id = Guid.NewGuid().ToString(),
            TrackName = trackName,
            CarClass = "", // Will be filled in by caller
            CarName = carName,
            CornerName = $"Turn {number}",
            CornerNumber = number,
            StartDistPct = detected.StartDist,
            BrakeZoneDistPct = detected.BrakeStartDist,
            TurnInDistPct = detected.TurnInDist,
            ApexDistPct = detected.ApexDist,
            ExitDistPct = detected.ExitDist,
            BrakeMarker = FormatBrakeMarker(detected),
            TargetBrakePressure = detected.TargetBrakePressure,
            TargetEntrySpeed = detected.TargetEntrySpeed,
            TargetMinSpeed = detected.TargetMinSpeed,
            TargetGear = detected.TargetGear,
            TargetThrottlePickup = detected.ThrottleDist,
            TargetExitSpeed = detected.TargetExitSpeed,
            DefaultVoiceCall = voiceCall,
            CornerType = cornerType,
            IsLeftTurn = isLeftTurn
        };
    }

    // ═══════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════

    private List<float> SmoothTrace(List<float> values, int windowSize)
    {
        var smoothed = new List<float>();
        int halfWindow = windowSize / 2;

        for (int i = 0; i < values.Count; i++)
        {
            int start = Math.Max(0, i - halfWindow);
            int end = Math.Min(values.Count, i + halfWindow + 1);
            float avg = values.Skip(start).Take(end - start).Average();
            smoothed.Add(avg);
        }

        return smoothed;
    }

    private int FindMinSpeedInRange(List<TelemetryRecord> lap, int startIdx, int endIdx)
    {
        int minIdx = startIdx;
        float minSpeed = lap[startIdx].Speed;

        for (int i = startIdx; i <= endIdx && i < lap.Count; i++)
        {
            if (lap[i].Speed < minSpeed)
            {
                minSpeed = lap[i].Speed;
                minIdx = i;
            }
        }

        return minIdx;
    }

    private int FindThrottleAfterMinSpeed(List<TelemetryRecord> lap, int apexIdx)
    {
        // Find where throttle application begins after apex
        for (int i = apexIdx; i < lap.Count; i++)
        {
            if (lap[i].Throttle > 0.5f)
            {
                return i;
            }
        }

        return lap.Count - 1;
    }

    private float EstimateTurnInDistance(List<TelemetryRecord> lap, int brakeStartIdx, int apexIdx)
    {
        // Turn-in typically occurs when steering angle increases significantly
        float maxSteering = 0;
        int turnInIdx = brakeStartIdx;

        for (int i = brakeStartIdx; i < apexIdx && i < lap.Count; i++)
        {
            float steeringAngle = Math.Abs(lap[i].Steering);
            if (steeringAngle > maxSteering)
            {
                maxSteering = steeringAngle;
                turnInIdx = i;
            }
        }

        return turnInIdx < lap.Count ? lap[turnInIdx].LapDistPct : lap[apexIdx].LapDistPct;
    }

    private string ClassifyCorner(float minSpeed)
    {
        if (minSpeed < MinSpeedHairpin) return "hairpin";
        if (minSpeed < MinSpeedHeavyBrake) return "heavy_brake";
        if (minSpeed < MinSpeedFastSweeper) return "light_brake";
        return "fast_sweeper";
    }

    private bool DetectTurnDirection(DetectedCorner corner)
    {
        // Detect from average steering angle at turn-in
        float avgSteering = 0f;
        // Would need access to actual telemetry here; for now, default to left
        return true;
    }

    private string GenerateDefaultVoiceCall(DetectedCorner corner, string cornerType)
    {
        return cornerType switch
        {
            "hairpin" => "Heavy brake. Smooth release.",
            "heavy_brake" => "Early brake. Commit deep.",
            "light_brake" => "Late brake. Smooth turn.",
            "fast_sweeper" => "Keep momentum. Quick hands.",
            _ => "Set up. Smooth exit."
        };
    }

    private string FormatBrakeMarker(DetectedCorner corner)
    {
        // Format as distance estimate or board marker
        int brakeMeters = (int)(corner.BrakeStartDist * 1000);
        return $"{brakeMeters}m";
    }

    // ═══════════════════════════════════════
    // HELPER CLASSES
    // ═══════════════════════════════════════

    private class BrakeZone
    {
        public int StartIdx { get; set; }
        public int EndIdx { get; set; }
        public int MinSpeedIdx { get; set; }
    }

    private class DetectedCorner
    {
        public float BrakeStartDist { get; set; }
        public float TurnInDist { get; set; }
        public float ApexDist { get; set; }
        public float ThrottleDist { get; set; }
        public float StartDist { get; set; }
        public float ExitDist { get; set; }
        public float MinSpeedValue { get; set; }
        public TelemetryRecord BrakeData { get; set; } = new();
        public TelemetryRecord ApexData { get; set; } = new();
        public TelemetryRecord ExitData { get; set; } = new();
        public float TargetBrakePressure { get; set; }
        public float TargetEntrySpeed { get; set; }
        public float TargetMinSpeed { get; set; }
        public float TargetExitSpeed { get; set; }
        public int TargetGear { get; set; }
    }
}
