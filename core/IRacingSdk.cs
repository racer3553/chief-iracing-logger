// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — iRacing SDK Shared Memory Reader
// Reads telemetry directly from iRacing's memory-mapped file.
// Based on the official iRacing SDK header specification.
// ═══════════════════════════════════════════════════════════════

using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace ChiefLogger.Core;

// ═══════════════════════════════════════
// ENUMS & CONSTANTS
// ═══════════════════════════════════════

public enum IRacingVarType
{
    Char = 0,
    Bool = 1,
    Int = 2,
    BitField = 3,
    Float = 4,
    Double = 5
}

public static class IRacingConstants
{
    public const string MemMapFileName = "Local\\IRSDKMemMapFileName";
    public const string DataValidEventName = "Local\\IRSDKDataValidEvent";
    public const string BroadcastMsgName = "IRSDK_BROADCASTMSG";

    public const int MaxBufs = 4;
    public const int MaxString = 32;
    public const int MaxDesc = 64;

    // Header offsets (bytes)
    public const int HeaderSize = 144;       // Total header before var headers
    public const int VarHeaderSize = 144;    // Size of each VarHeader struct
}

// ═══════════════════════════════════════
// HEADER STRUCTURES
// ═══════════════════════════════════════

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct IRacingHeader
{
    public int Ver;                  // API version
    public int Status;               // 1 = connected
    public int TickRate;             // Ticks per second (usually 60)

    // Session info
    public int SessionInfoUpdate;    // Increments when session YAML changes
    public int SessionInfoLen;       // Length of session info string
    public int SessionInfoOffset;    // Offset to session info string

    // Telemetry variables
    public int NumVars;              // Number of variables in header
    public int VarHeaderOffset;      // Offset to variable headers

    // Data buffers
    public int NumBuf;               // Number of buffers (usually 3-4)
    public int BufLen;               // Length of each data buffer

    // Padding to 48 bytes, then buffer info
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public int[] Pad;
}

[StructLayout(LayoutKind.Sequential)]
public struct IRacingBufInfo
{
    public int TickCount;            // Tick when buffer was written
    public int BufOffset;            // Offset to start of buffer
    public int Pad1;
    public int Pad2;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct IRacingVarHeader
{
    public int Type;                 // IRacingVarType
    public int Offset;               // Offset within data buffer
    public int Count;                // 1 for scalar, N for array

    [MarshalAs(UnmanagedType.I1)]
    public bool CountAsTime;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] Pad;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string Name;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Desc;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string Unit;
}

// ═══════════════════════════════════════
// TELEMETRY DATA MODEL
// ═══════════════════════════════════════

public class TelemetrySample
{
    public long TimestampMs { get; set; }
    public int SessionNum { get; set; }
    public int Lap { get; set; }
    public float LapDistPct { get; set; }
    public float Speed { get; set; }          // m/s
    public float Throttle { get; set; }       // 0-1
    public float Brake { get; set; }          // 0-1
    public float SteeringAngle { get; set; }  // radians
    public int Gear { get; set; }
    public float RPM { get; set; }
    public float FuelLevel { get; set; }      // liters
    public float LapCurrentLapTime { get; set; }
    public float LapDeltaToSessionBestLap { get; set; }
    public int TrackPosition { get; set; }    // overall position
    public int IncidentCount { get; set; }
    public float BrakesBias { get; set; }
    public float LatAccel { get; set; }
    public float LongAccel { get; set; }
    public float Yaw { get; set; }
    public float YawRate { get; set; }

    // Tire temps (LF, RF, LR, RR — each has L/M/R)
    public float[]? LFTireTemp { get; set; }
    public float[]? RFTireTemp { get; set; }
    public float[]? LRTireTemp { get; set; }
    public float[]? RRTireTemp { get; set; }

    // Tire wear
    public float[]? LFWear { get; set; }
    public float[]? RFWear { get; set; }
    public float[]? LRWear { get; set; }
    public float[]? RRWear { get; set; }
}

// ═══════════════════════════════════════
// SESSION INFO FROM YAML
// ═══════════════════════════════════════

public class SessionInfo
{
    public string DriverName { get; set; } = "";
    public int DriverId { get; set; }
    public string CarName { get; set; } = "";
    public string CarScreenName { get; set; } = "";
    public int CarId { get; set; }
    public string TrackName { get; set; } = "";
    public string TrackDisplayName { get; set; } = "";
    public int TrackId { get; set; }
    public string TrackConfig { get; set; } = "";
    public float TrackLength { get; set; }   // km
    public string SessionType { get; set; } = "";
    public int SessionNum { get; set; }
    public int SubSessionId { get; set; }
    public int SeriesId { get; set; }
    public string SeriesName { get; set; } = "";
    public float AirTemp { get; set; }       // C
    public float TrackTemp { get; set; }     // C
    public string Skies { get; set; } = "";
    public float WindSpeed { get; set; }
    public string WindDir { get; set; } = "";
    public int Humidity { get; set; }
    public string RawYaml { get; set; } = "";
}

// ═══════════════════════════════════════
// SDK READER
// ═══════════════════════════════════════

public class IRacingSdk : IDisposable
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private EventWaitHandle? _dataEvent;
    private IRacingHeader _header;
    private Dictionary<string, IRacingVarHeader> _varHeaders = new();
    private bool _connected;
    private int _lastSessionInfoUpdate = -1;
    private SessionInfo _sessionInfo = new();

    public bool IsConnected => _connected;
    public SessionInfo CurrentSession => _sessionInfo;

    // ═══ CONNECT ═══
    public bool TryConnect()
    {
        if (_connected) return true;

        try
        {
            _mmf = MemoryMappedFile.OpenExisting(IRacingConstants.MemMapFileName);
            _accessor = _mmf.CreateViewAccessor();
            _dataEvent = EventWaitHandle.OpenExisting(IRacingConstants.DataValidEventName);

            // Read header
            _accessor.Read(0, out _header);

            if (_header.Ver < 2 || _header.Status == 0)
            {
                Disconnect();
                return false;
            }

            // Read variable headers
            _varHeaders.Clear();
            for (int i = 0; i < _header.NumVars; i++)
            {
                int offset = _header.VarHeaderOffset + (i * Marshal.SizeOf<IRacingVarHeader>());
                _accessor.Read(offset, out IRacingVarHeader vh);
                if (!string.IsNullOrEmpty(vh.Name))
                {
                    _varHeaders[vh.Name] = vh;
                }
            }

            _connected = true;
            return true;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _connected = false;
        _accessor?.Dispose();
        _accessor = null;
        _mmf?.Dispose();
        _mmf = null;
        _dataEvent?.Dispose();
        _dataEvent = null;
        _varHeaders.Clear();
    }

    // ═══ WAIT FOR DATA ═══
    public bool WaitForData(int timeoutMs = 16)
    {
        if (!_connected || _dataEvent == null) return false;
        return _dataEvent.WaitOne(timeoutMs);
    }

    // ═══ GET LATEST DATA BUFFER OFFSET ═══
    private int GetLatestBufOffset()
    {
        if (_accessor == null) return -1;

        // Re-read header for latest tick counts
        _accessor.Read(0, out _header);

        int latestTick = -1;
        int latestOffset = -1;

        // Buffer info starts at offset 48 in the header area
        int bufInfoStart = 48; // after the 12 ints (48 bytes)
        for (int i = 0; i < Math.Min(_header.NumBuf, IRacingConstants.MaxBufs); i++)
        {
            int pos = bufInfoStart + (i * Marshal.SizeOf<IRacingBufInfo>());
            _accessor.Read(pos, out IRacingBufInfo bufInfo);
            if (bufInfo.TickCount > latestTick)
            {
                latestTick = bufInfo.TickCount;
                latestOffset = bufInfo.BufOffset;
            }
        }

        return latestOffset;
    }

    // ═══ READ VARIABLE VALUES ═══
    public float GetFloat(string name)
    {
        if (_accessor == null || !_varHeaders.TryGetValue(name, out var vh)) return 0f;
        int bufOffset = GetLatestBufOffset();
        if (bufOffset < 0) return 0f;
        return _accessor.ReadSingle(bufOffset + vh.Offset);
    }

    public int GetInt(string name)
    {
        if (_accessor == null || !_varHeaders.TryGetValue(name, out var vh)) return 0;
        int bufOffset = GetLatestBufOffset();
        if (bufOffset < 0) return 0;
        return _accessor.ReadInt32(bufOffset + vh.Offset);
    }

    public double GetDouble(string name)
    {
        if (_accessor == null || !_varHeaders.TryGetValue(name, out var vh)) return 0.0;
        int bufOffset = GetLatestBufOffset();
        if (bufOffset < 0) return 0.0;
        return _accessor.ReadDouble(bufOffset + vh.Offset);
    }

    public bool GetBool(string name)
    {
        if (_accessor == null || !_varHeaders.TryGetValue(name, out var vh)) return false;
        int bufOffset = GetLatestBufOffset();
        if (bufOffset < 0) return false;
        return _accessor.ReadBoolean(bufOffset + vh.Offset);
    }

    public float[] GetFloatArray(string name, int count)
    {
        if (_accessor == null || !_varHeaders.TryGetValue(name, out var vh))
            return new float[count];

        int bufOffset = GetLatestBufOffset();
        if (bufOffset < 0) return new float[count];

        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = _accessor.ReadSingle(bufOffset + vh.Offset + (i * 4));
        }
        return result;
    }

    public bool HasVariable(string name) => _varHeaders.ContainsKey(name);

    public IReadOnlyDictionary<string, IRacingVarHeader> Variables => _varHeaders;

    // ═══ READ FULL TELEMETRY SAMPLE ═══
    public TelemetrySample ReadSample()
    {
        var s = new TelemetrySample
        {
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SessionNum = GetInt("SessionNum"),
            Lap = GetInt("Lap"),
            LapDistPct = GetFloat("LapDistPct"),
            Speed = GetFloat("Speed"),
            Throttle = GetFloat("Throttle"),
            Brake = GetFloat("Brake"),
            SteeringAngle = GetFloat("SteeringWheelAngle"),
            Gear = GetInt("Gear"),
            RPM = GetFloat("RPM"),
            FuelLevel = GetFloat("FuelLevel"),
            LapCurrentLapTime = GetFloat("LapCurrentLapTime"),
            LapDeltaToSessionBestLap = HasVariable("LapDeltaToSessionBestLap")
                ? GetFloat("LapDeltaToSessionBestLap") : 0f,
            TrackPosition = GetInt("PlayerCarPosition"),
            IncidentCount = GetInt("PlayerCarMyIncidentCount"),
            BrakesBias = HasVariable("dcBrakeBias") ? GetFloat("dcBrakeBias") : 0f,
            LatAccel = GetFloat("LatAccel"),
            LongAccel = GetFloat("LongAccel"),
            Yaw = GetFloat("Yaw"),
            YawRate = GetFloat("YawRate"),
        };

        // Tire temps (Left/Mid/Right for each corner)
        if (HasVariable("LFtempCL"))
        {
            s.LFTireTemp = new[] { GetFloat("LFtempCL"), GetFloat("LFtempCM"), GetFloat("LFtempCR") };
            s.RFTireTemp = new[] { GetFloat("RFtempCL"), GetFloat("RFtempCM"), GetFloat("RFtempCR") };
            s.LRTireTemp = new[] { GetFloat("LRtempCL"), GetFloat("LRtempCM"), GetFloat("LRtempCR") };
            s.RRTireTemp = new[] { GetFloat("RRtempCL"), GetFloat("RRtempCM"), GetFloat("RRtempCR") };
        }

        // Tire wear
        if (HasVariable("LFwearL"))
        {
            s.LFWear = new[] { GetFloat("LFwearL"), GetFloat("LFwearM"), GetFloat("LFwearR") };
            s.RFWear = new[] { GetFloat("RFwearL"), GetFloat("RFwearM"), GetFloat("RFwearR") };
            s.LRWear = new[] { GetFloat("LRwearL"), GetFloat("LRwearM"), GetFloat("LRwearR") };
            s.RRWear = new[] { GetFloat("RRwearL"), GetFloat("RRwearM"), GetFloat("RRwearR") };
        }

        return s;
    }

    // ═══ SESSION INFO (YAML) ═══
    public SessionInfo? UpdateSessionInfo()
    {
        if (_accessor == null || !_connected) return null;

        // Re-read header
        _accessor.Read(0, out _header);

        if (_header.SessionInfoUpdate == _lastSessionInfoUpdate)
            return null; // No change

        _lastSessionInfoUpdate = _header.SessionInfoUpdate;

        // Read YAML string
        var bytes = new byte[_header.SessionInfoLen];
        _accessor.ReadArray(_header.SessionInfoOffset, bytes, 0, bytes.Length);
        var yaml = Encoding.Latin1.GetString(bytes).TrimEnd('\0');

        _sessionInfo = ParseSessionYaml(yaml);
        _sessionInfo.RawYaml = yaml;

        return _sessionInfo;
    }

    // Simple YAML parser for iRacing session info
    // iRacing YAML is simple key: value format, not full YAML
    private static SessionInfo ParseSessionYaml(string yaml)
    {
        var info = new SessionInfo();

        info.DriverName = ExtractYamlValue(yaml, "UserName") ?? "";
        info.DriverId = int.TryParse(ExtractYamlValue(yaml, "UserID"), out int did) ? did : 0;

        // Car info — look in DriverInfo.Drivers section for UserName's entry
        info.CarName = ExtractYamlValue(yaml, "CarPath") ?? "";
        info.CarScreenName = ExtractYamlValue(yaml, "CarScreenName") ?? ExtractYamlValue(yaml, "CarScreenNameShort") ?? "";
        info.CarId = int.TryParse(ExtractYamlValue(yaml, "CarIdx"), out int cid) ? cid : 0;

        // Track info
        info.TrackName = ExtractYamlValue(yaml, "TrackName") ?? "";
        info.TrackDisplayName = ExtractYamlValue(yaml, "TrackDisplayName") ?? "";
        info.TrackId = int.TryParse(ExtractYamlValue(yaml, "TrackID"), out int tid) ? tid : 0;
        info.TrackConfig = ExtractYamlValue(yaml, "TrackConfigName") ?? "";
        info.TrackLength = float.TryParse(ExtractYamlValue(yaml, "TrackLength")?.Replace(" km", ""), out float tl) ? tl : 0f;

        // Weather
        info.AirTemp = float.TryParse(ExtractYamlValue(yaml, "TrackAirTemp")?.Replace(" C", ""), out float at) ? at : 0f;
        info.TrackTemp = float.TryParse(ExtractYamlValue(yaml, "TrackSurfaceTemp")?.Replace(" C", ""), out float tt) ? tt : 0f;
        info.Skies = ExtractYamlValue(yaml, "TrackSkies") ?? "";
        info.WindSpeed = float.TryParse(ExtractYamlValue(yaml, "TrackWindVel")?.Split(' ')[0], out float ws) ? ws : 0f;
        info.WindDir = ExtractYamlValue(yaml, "TrackWindDir") ?? "";
        info.Humidity = int.TryParse(ExtractYamlValue(yaml, "TrackRelativeHumidity")?.Replace(" %", ""), out int hum) ? hum : 0;

        // Session
        info.SubSessionId = int.TryParse(ExtractYamlValue(yaml, "SubSessionID"), out int ssid) ? ssid : 0;
        info.SeriesId = int.TryParse(ExtractYamlValue(yaml, "SeriesID"), out int sid) ? sid : 0;
        info.SeriesName = ExtractYamlValue(yaml, "SeriesShortName") ?? "";

        // Find current session type from Sessions section
        var sessionSection = ExtractSessionTypes(yaml);
        if (sessionSection.Count > 0)
        {
            info.SessionType = sessionSection.LastOrDefault() ?? "Unknown";
        }

        return info;
    }

    private static string? ExtractYamlValue(string yaml, string key)
    {
        // Find "key: value" pattern in YAML
        var search = key + ":";
        int idx = yaml.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;

        int valueStart = idx + search.Length;
        int lineEnd = yaml.IndexOf('\n', valueStart);
        if (lineEnd < 0) lineEnd = yaml.Length;

        return yaml[valueStart..lineEnd].Trim().Trim('"');
    }

    private static List<string> ExtractSessionTypes(string yaml)
    {
        var types = new List<string>();
        int searchFrom = 0;
        while (true)
        {
            int idx = yaml.IndexOf("SessionType:", searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;
            int valueStart = idx + "SessionType:".Length;
            int lineEnd = yaml.IndexOf('\n', valueStart);
            if (lineEnd < 0) lineEnd = yaml.Length;
            types.Add(yaml[valueStart..lineEnd].Trim());
            searchFrom = lineEnd;
        }
        return types;
    }

    // ═══ DISPOSE ═══
    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
