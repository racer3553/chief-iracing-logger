// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — SQLite Database Layer
// Local-first storage for sessions, laps, telemetry, and setups.
// ═══════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace ChiefLogger.Data;

public class ChiefDatabase : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;

    public ChiefDatabase(string dbPath)
    {
        _dbPath = dbPath;
    }

    // ═══ INIT ═══
    public void Initialize()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();

        // WAL mode for better concurrent read/write
        Execute("PRAGMA journal_mode=WAL");
        Execute("PRAGMA synchronous=NORMAL");
        Execute("PRAGMA cache_size=-8000"); // 8MB cache

        CreateTables();
    }

    private void CreateTables()
    {
        Execute(@"
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT UNIQUE NOT NULL,
                iracing_sub_session_id INTEGER DEFAULT 0,
                driver_name TEXT DEFAULT '',
                driver_id INTEGER DEFAULT 0,
                car_name TEXT DEFAULT '',
                car_screen_name TEXT DEFAULT '',
                car_id INTEGER DEFAULT 0,
                track_name TEXT DEFAULT '',
                track_display_name TEXT DEFAULT '',
                track_id INTEGER DEFAULT 0,
                track_config TEXT DEFAULT '',
                session_type TEXT DEFAULT '',
                air_temp REAL DEFAULT 0,
                track_temp REAL DEFAULT 0,
                skies TEXT DEFAULT '',
                wind_speed REAL DEFAULT 0,
                humidity INTEGER DEFAULT 0,
                started_at TEXT DEFAULT '',
                ended_at TEXT DEFAULT '',
                total_laps INTEGER DEFAULT 0,
                best_lap_time REAL DEFAULT 0,
                best_lap_number INTEGER DEFAULT 0,
                final_position INTEGER DEFAULT 0,
                incident_count INTEGER DEFAULT 0,
                fuel_used_total REAL DEFAULT 0,
                fuel_per_lap REAL DEFAULT 0,
                setup_file_id TEXT DEFAULT '',
                notes TEXT DEFAULT '',
                sync_status TEXT DEFAULT 'not_synced',
                session_info_yaml TEXT DEFAULT ''
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS laps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                lap_number INTEGER NOT NULL,
                lap_time REAL DEFAULT 0,
                is_valid INTEGER DEFAULT 1,
                incidents_this_lap INTEGER DEFAULT 0,
                fuel_used REAL DEFAULT 0,
                fuel_remaining REAL DEFAULT 0,
                max_speed REAL DEFAULT 0,
                min_speed REAL DEFAULT 0,
                avg_throttle REAL DEFAULT 0,
                avg_brake REAL DEFAULT 0,
                max_lat_g REAL DEFAULT 0,
                max_long_g REAL DEFAULT 0,
                position INTEGER DEFAULT 0,
                delta_to_best REAL DEFAULT 0,
                gear_changes INTEGER DEFAULT 0,
                flags TEXT DEFAULT '',
                FOREIGN KEY (session_id) REFERENCES sessions(session_id)
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS telemetry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                lap_number INTEGER DEFAULT 0,
                timestamp_ms INTEGER DEFAULT 0,
                lap_dist_pct REAL DEFAULT 0,
                speed REAL DEFAULT 0,
                throttle REAL DEFAULT 0,
                brake REAL DEFAULT 0,
                steering REAL DEFAULT 0,
                gear INTEGER DEFAULT 0,
                rpm REAL DEFAULT 0,
                fuel_level REAL DEFAULT 0,
                lap_time REAL DEFAULT 0,
                delta REAL DEFAULT 0,
                lat_accel REAL DEFAULT 0,
                long_accel REAL DEFAULT 0,
                yaw REAL DEFAULT 0,
                yaw_rate REAL DEFAULT 0,
                brake_bias REAL DEFAULT 0,
                position INTEGER DEFAULT 0,
                incidents INTEGER DEFAULT 0,
                tire_temps TEXT,
                tire_wear TEXT,
                FOREIGN KEY (session_id) REFERENCES sessions(session_id)
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS setup_files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id TEXT UNIQUE NOT NULL,
                file_name TEXT DEFAULT '',
                file_path TEXT DEFAULT '',
                stored_path TEXT DEFAULT '',
                car_name TEXT DEFAULT '',
                car_id INTEGER DEFAULT 0,
                track_name TEXT DEFAULT '',
                track_id INTEGER DEFAULT 0,
                detected_at TEXT DEFAULT '',
                modified_at TEXT DEFAULT '',
                raw_content TEXT DEFAULT '',
                parsed_values TEXT DEFAULT '',
                notes TEXT DEFAULT '',
                sync_status TEXT DEFAULT 'not_synced'
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS coaching_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                lap_number INTEGER DEFAULT 0,
                lap_dist_pct REAL DEFAULT 0,
                timestamp_ms INTEGER DEFAULT 0,
                event_type TEXT DEFAULT '',
                severity TEXT DEFAULT 'info',
                message TEXT DEFAULT '',
                data TEXT DEFAULT '',
                FOREIGN KEY (session_id) REFERENCES sessions(session_id)
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS sync_queue (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                item_type TEXT DEFAULT '',
                item_id TEXT DEFAULT '',
                status TEXT DEFAULT 'pending',
                retry_count INTEGER DEFAULT 0,
                last_error TEXT DEFAULT '',
                created_at TEXT DEFAULT '',
                last_attempt_at TEXT DEFAULT ''
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT DEFAULT ''
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS track_maps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                track_name TEXT NOT NULL,
                track_id INTEGER DEFAULT 0,
                car_class TEXT NOT NULL,
                car_name TEXT DEFAULT '',
                track_length_km REAL DEFAULT 0,
                source TEXT DEFAULT 'manual',
                corners_json TEXT DEFAULT '',
                created_at TEXT DEFAULT '',
                updated_at TEXT DEFAULT '',
                UNIQUE(track_name, car_class)
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS corner_performance (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                corner_id TEXT NOT NULL,
                lap_number INTEGER NOT NULL,
                brake_start_dist_pct REAL DEFAULT 0,
                peak_brake_pressure REAL DEFAULT 0,
                brake_release_rate REAL DEFAULT 0,
                entry_speed REAL DEFAULT 0,
                apex_speed REAL DEFAULT 0,
                min_speed REAL DEFAULT 0,
                throttle_pickup_dist_pct REAL DEFAULT 0,
                exit_speed REAL DEFAULT 0,
                gear_at_entry INTEGER DEFAULT 0,
                gear_at_apex INTEGER DEFAULT 0,
                steering_corrections INTEGER DEFAULT 0,
                delta_gained_lost REAL DEFAULT 0,
                mistake_category TEXT DEFAULT '',
                FOREIGN KEY (session_id) REFERENCES sessions(session_id)
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS coaching_instructions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                corner_id TEXT NOT NULL,
                mistake_category TEXT NOT NULL,
                instruction_text TEXT NOT NULL,
                confidence_score REAL DEFAULT 0.5,
                times_given INTEGER DEFAULT 0,
                times_improved INTEGER DEFAULT 0,
                times_worsened INTEGER DEFAULT 0,
                times_no_change INTEGER DEFAULT 0,
                UNIQUE(corner_id, mistake_category, instruction_text)
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS coaching_outcomes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                instruction_id INTEGER NOT NULL,
                corner_id TEXT NOT NULL,
                lap_given INTEGER NOT NULL,
                lap_result INTEGER NOT NULL,
                delta_before REAL DEFAULT 0,
                delta_after REAL DEFAULT 0,
                result TEXT DEFAULT '',
                FOREIGN KEY (instruction_id) REFERENCES coaching_instructions(id)
            )");

        Execute(@"
            CREATE TABLE IF NOT EXISTS voice_call_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                corner_id TEXT NOT NULL,
                lap_number INTEGER NOT NULL,
                call_text TEXT DEFAULT '',
                call_type TEXT DEFAULT '',
                timestamp_ms INTEGER DEFAULT 0,
                FOREIGN KEY (session_id) REFERENCES sessions(session_id)
            )");

        // Indexes for performance
        Execute("CREATE INDEX IF NOT EXISTS idx_telemetry_session ON telemetry(session_id, lap_number)");
        Execute("CREATE INDEX IF NOT EXISTS idx_laps_session ON laps(session_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_sessions_track ON sessions(track_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_sessions_car ON sessions(car_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_setup_files_car ON setup_files(car_id, track_id)");
        Execute("CREATE INDEX IF NOT EXISTS idx_coaching_session ON coaching_events(session_id, lap_number)");
        Execute("CREATE INDEX IF NOT EXISTS idx_sync_queue_status ON sync_queue(status)");
        Execute("CREATE INDEX IF NOT EXISTS idx_track_maps_name_class ON track_maps(track_name, car_class)");
        Execute("CREATE INDEX IF NOT EXISTS idx_corner_perf_session_corner ON corner_performance(session_id, corner_id, lap_number)");
        Execute("CREATE INDEX IF NOT EXISTS idx_coaching_instruct_mistake ON coaching_instructions(corner_id, mistake_category)");
        Execute("CREATE INDEX IF NOT EXISTS idx_voice_calls_session ON voice_call_history(session_id)");
    }

    // ═══════════════════════════════════════
    // SESSIONS
    // ═══════════════════════════════════════

    public void InsertSession(LoggedSession s)
    {
        Execute(@"
            INSERT OR REPLACE INTO sessions (
                session_id, iracing_sub_session_id, driver_name, driver_id,
                car_name, car_screen_name, car_id, track_name, track_display_name,
                track_id, track_config, session_type, air_temp, track_temp,
                skies, wind_speed, humidity, started_at, ended_at,
                total_laps, best_lap_time, best_lap_number, final_position,
                incident_count, fuel_used_total, fuel_per_lap, setup_file_id,
                notes, sync_status, session_info_yaml
            ) VALUES (
                @sid, @irsid, @dn, @did, @cn, @csn, @cid, @tn, @tdn,
                @tid, @tc, @st, @at, @tt, @sk, @ws, @hum, @sa, @ea,
                @tl, @blt, @bln, @fp, @ic, @fut, @fpl, @sfi,
                @notes, @ss, @yaml
            )",
            ("@sid", s.SessionId), ("@irsid", s.IRacingSubSessionId),
            ("@dn", s.DriverName), ("@did", s.DriverId),
            ("@cn", s.CarName), ("@csn", s.CarScreenName), ("@cid", s.CarId),
            ("@tn", s.TrackName), ("@tdn", s.TrackDisplayName),
            ("@tid", s.TrackId), ("@tc", s.TrackConfig),
            ("@st", s.SessionType), ("@at", s.AirTemp), ("@tt", s.TrackTemp),
            ("@sk", s.Skies), ("@ws", s.WindSpeed), ("@hum", s.Humidity),
            ("@sa", s.StartedAt), ("@ea", s.EndedAt),
            ("@tl", s.TotalLaps), ("@blt", s.BestLapTime), ("@bln", s.BestLapNumber),
            ("@fp", s.FinalPosition), ("@ic", s.IncidentCount),
            ("@fut", s.FuelUsedTotal), ("@fpl", s.FuelPerLap),
            ("@sfi", s.SetupFileId), ("@notes", s.Notes),
            ("@ss", s.SyncStatus), ("@yaml", s.SessionInfoYaml));
    }

    public LoggedSession? GetSession(string sessionId)
    {
        using var cmd = CreateCommand("SELECT * FROM sessions WHERE session_id = @sid", ("@sid", sessionId));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadSession(reader);
    }

    public List<LoggedSession> GetRecentSessions(int limit = 20)
    {
        var list = new List<LoggedSession>();
        using var cmd = CreateCommand($"SELECT * FROM sessions ORDER BY started_at DESC LIMIT {limit}");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadSession(reader));
        return list;
    }

    public void UpdateSessionEnd(string sessionId, string endedAt, int totalLaps, float bestLap, int bestLapNum, int position, int incidents, float fuelUsed, float fuelPerLap)
    {
        Execute(@"UPDATE sessions SET ended_at=@ea, total_laps=@tl, best_lap_time=@blt,
            best_lap_number=@bln, final_position=@fp, incident_count=@ic,
            fuel_used_total=@fut, fuel_per_lap=@fpl WHERE session_id=@sid",
            ("@ea", endedAt), ("@tl", totalLaps), ("@blt", bestLap),
            ("@bln", bestLapNum), ("@fp", position), ("@ic", incidents),
            ("@fut", fuelUsed), ("@fpl", fuelPerLap), ("@sid", sessionId));
    }

    public void UpdateSessionNotes(string sessionId, string notes)
    {
        Execute("UPDATE sessions SET notes=@n WHERE session_id=@sid", ("@n", notes), ("@sid", sessionId));
    }

    public void UpdateSessionSyncStatus(string sessionId, string status)
    {
        Execute("UPDATE sessions SET sync_status=@ss WHERE session_id=@sid", ("@ss", status), ("@sid", sessionId));
    }

    private LoggedSession ReadSession(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), SessionId = r.GetString(1),
        IRacingSubSessionId = r.GetInt32(2), DriverName = r.GetString(3),
        DriverId = r.GetInt32(4), CarName = r.GetString(5),
        CarScreenName = r.GetString(6), CarId = r.GetInt32(7),
        TrackName = r.GetString(8), TrackDisplayName = r.GetString(9),
        TrackId = r.GetInt32(10), TrackConfig = r.GetString(11),
        SessionType = r.GetString(12), AirTemp = r.GetFloat(13),
        TrackTemp = r.GetFloat(14), Skies = r.GetString(15),
        WindSpeed = r.GetFloat(16), Humidity = r.GetInt32(17),
        StartedAt = r.GetString(18), EndedAt = r.GetString(19),
        TotalLaps = r.GetInt32(20), BestLapTime = r.GetFloat(21),
        BestLapNumber = r.GetInt32(22), FinalPosition = r.GetInt32(23),
        IncidentCount = r.GetInt32(24), FuelUsedTotal = r.GetFloat(25),
        FuelPerLap = r.GetFloat(26), SetupFileId = r.GetString(27),
        Notes = r.GetString(28), SyncStatus = r.GetString(29),
        SessionInfoYaml = r.GetString(30),
    };

    // ═══════════════════════════════════════
    // LAPS
    // ═══════════════════════════════════════

    public void InsertLap(LoggedLap lap)
    {
        Execute(@"
            INSERT INTO laps (session_id, lap_number, lap_time, is_valid, incidents_this_lap,
                fuel_used, fuel_remaining, max_speed, min_speed, avg_throttle, avg_brake,
                max_lat_g, max_long_g, position, delta_to_best, gear_changes, flags)
            VALUES (@sid, @ln, @lt, @iv, @inc, @fu, @fr, @maxs, @mins, @at, @ab,
                @mlg, @mlong, @pos, @dtb, @gc, @flags)",
            ("@sid", lap.SessionId), ("@ln", lap.LapNumber), ("@lt", lap.LapTime),
            ("@iv", lap.IsValid ? 1 : 0), ("@inc", lap.IncidentsThisLap),
            ("@fu", lap.FuelUsed), ("@fr", lap.FuelRemaining),
            ("@maxs", lap.MaxSpeed), ("@mins", lap.MinSpeed),
            ("@at", lap.AvgThrottle), ("@ab", lap.AvgBrake),
            ("@mlg", lap.MaxLatG), ("@mlong", lap.MaxLongG),
            ("@pos", lap.Position), ("@dtb", lap.DeltaToBest),
            ("@gc", lap.GearChanges), ("@flags", lap.Flags));
    }

    public List<LoggedLap> GetLapsForSession(string sessionId)
    {
        var list = new List<LoggedLap>();
        using var cmd = CreateCommand("SELECT * FROM laps WHERE session_id=@sid ORDER BY lap_number", ("@sid", sessionId));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new LoggedLap
            {
                Id = reader.GetInt64(0), SessionId = reader.GetString(1),
                LapNumber = reader.GetInt32(2), LapTime = reader.GetFloat(3),
                IsValid = reader.GetInt32(4) == 1, IncidentsThisLap = reader.GetInt32(5),
                FuelUsed = reader.GetFloat(6), FuelRemaining = reader.GetFloat(7),
                MaxSpeed = reader.GetFloat(8), MinSpeed = reader.GetFloat(9),
                AvgThrottle = reader.GetFloat(10), AvgBrake = reader.GetFloat(11),
                MaxLatG = reader.GetFloat(12), MaxLongG = reader.GetFloat(13),
                Position = reader.GetInt32(14), DeltaToBest = reader.GetFloat(15),
                GearChanges = reader.GetInt32(16), Flags = reader.GetString(17),
            });
        }
        return list;
    }

    // ═══════════════════════════════════════
    // TELEMETRY (bulk insert for performance)
    // ═══════════════════════════════════════

    public void InsertTelemetryBatch(IEnumerable<TelemetryRecord> records)
    {
        if (_conn == null) return;

        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO telemetry (session_id, lap_number, timestamp_ms, lap_dist_pct,
                speed, throttle, brake, steering, gear, rpm, fuel_level, lap_time,
                delta, lat_accel, long_accel, yaw, yaw_rate, brake_bias,
                position, incidents, tire_temps, tire_wear)
            VALUES (@sid, @ln, @ts, @ldp, @sp, @th, @br, @st, @gr, @rpm, @fl, @lt,
                @d, @la, @lo, @y, @yr, @bb, @pos, @inc, @tt, @tw)";

        var pSid = cmd.Parameters.Add("@sid", SqliteType.Text);
        var pLn = cmd.Parameters.Add("@ln", SqliteType.Integer);
        var pTs = cmd.Parameters.Add("@ts", SqliteType.Integer);
        var pLdp = cmd.Parameters.Add("@ldp", SqliteType.Real);
        var pSp = cmd.Parameters.Add("@sp", SqliteType.Real);
        var pTh = cmd.Parameters.Add("@th", SqliteType.Real);
        var pBr = cmd.Parameters.Add("@br", SqliteType.Real);
        var pSt = cmd.Parameters.Add("@st", SqliteType.Real);
        var pGr = cmd.Parameters.Add("@gr", SqliteType.Integer);
        var pRpm = cmd.Parameters.Add("@rpm", SqliteType.Real);
        var pFl = cmd.Parameters.Add("@fl", SqliteType.Real);
        var pLt = cmd.Parameters.Add("@lt", SqliteType.Real);
        var pD = cmd.Parameters.Add("@d", SqliteType.Real);
        var pLa = cmd.Parameters.Add("@la", SqliteType.Real);
        var pLo = cmd.Parameters.Add("@lo", SqliteType.Real);
        var pY = cmd.Parameters.Add("@y", SqliteType.Real);
        var pYr = cmd.Parameters.Add("@yr", SqliteType.Real);
        var pBb = cmd.Parameters.Add("@bb", SqliteType.Real);
        var pPos = cmd.Parameters.Add("@pos", SqliteType.Integer);
        var pInc = cmd.Parameters.Add("@inc", SqliteType.Integer);
        var pTt = cmd.Parameters.Add("@tt", SqliteType.Text);
        var pTw = cmd.Parameters.Add("@tw", SqliteType.Text);

        foreach (var r in records)
        {
            pSid.Value = r.SessionId; pLn.Value = r.LapNumber;
            pTs.Value = r.TimestampMs; pLdp.Value = r.LapDistPct;
            pSp.Value = r.Speed; pTh.Value = r.Throttle;
            pBr.Value = r.Brake; pSt.Value = r.Steering;
            pGr.Value = r.Gear; pRpm.Value = r.RPM;
            pFl.Value = r.FuelLevel; pLt.Value = r.LapTime;
            pD.Value = r.Delta; pLa.Value = r.LatAccel;
            pLo.Value = r.LongAccel; pY.Value = r.Yaw;
            pYr.Value = r.YawRate; pBb.Value = r.BrakeBias;
            pPos.Value = r.Position; pInc.Value = r.Incidents;
            pTt.Value = (object?)r.TireTemps ?? DBNull.Value;
            pTw.Value = (object?)r.TireWear ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<TelemetryRecord> GetTelemetryForLap(string sessionId, int lapNumber)
    {
        var list = new List<TelemetryRecord>();
        using var cmd = CreateCommand(
            "SELECT * FROM telemetry WHERE session_id=@sid AND lap_number=@ln ORDER BY timestamp_ms",
            ("@sid", sessionId), ("@ln", lapNumber));
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadTelemetry(reader));
        return list;
    }

    private TelemetryRecord ReadTelemetry(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), SessionId = r.GetString(1), LapNumber = r.GetInt32(2),
        TimestampMs = r.GetInt64(3), LapDistPct = r.GetFloat(4), Speed = r.GetFloat(5),
        Throttle = r.GetFloat(6), Brake = r.GetFloat(7), Steering = r.GetFloat(8),
        Gear = r.GetInt32(9), RPM = r.GetFloat(10), FuelLevel = r.GetFloat(11),
        LapTime = r.GetFloat(12), Delta = r.GetFloat(13), LatAccel = r.GetFloat(14),
        LongAccel = r.GetFloat(15), Yaw = r.GetFloat(16), YawRate = r.GetFloat(17),
        BrakeBias = r.GetFloat(18), Position = r.GetInt32(19), Incidents = r.GetInt32(20),
        TireTemps = r.IsDBNull(21) ? null : r.GetString(21),
        TireWear = r.IsDBNull(22) ? null : r.GetString(22),
    };

    // ═══════════════════════════════════════
    // SETUP FILES
    // ═══════════════════════════════════════

    public void InsertSetupFile(SetupFile sf)
    {
        Execute(@"
            INSERT OR REPLACE INTO setup_files (file_id, file_name, file_path, stored_path,
                car_name, car_id, track_name, track_id, detected_at, modified_at,
                raw_content, parsed_values, notes, sync_status)
            VALUES (@fid, @fn, @fp, @sp, @cn, @cid, @tn, @tid, @da, @ma, @rc, @pv, @n, @ss)",
            ("@fid", sf.FileId), ("@fn", sf.FileName), ("@fp", sf.FilePath),
            ("@sp", sf.StoredPath), ("@cn", sf.CarName), ("@cid", sf.CarId),
            ("@tn", sf.TrackName), ("@tid", sf.TrackId), ("@da", sf.DetectedAt),
            ("@ma", sf.ModifiedAt), ("@rc", sf.RawContent), ("@pv", sf.ParsedValues),
            ("@n", sf.Notes), ("@ss", sf.SyncStatus));
    }

    public List<SetupFile> GetSetupFiles(string? carName = null, string? trackName = null)
    {
        var where = new List<string>();
        var parms = new List<(string, object)>();
        if (carName != null) { where.Add("car_name=@cn"); parms.Add(("@cn", carName)); }
        if (trackName != null) { where.Add("track_name=@tn"); parms.Add(("@tn", trackName)); }
        var sql = "SELECT * FROM setup_files" + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + " ORDER BY detected_at DESC";
        using var cmd = CreateCommand(sql, parms.ToArray());
        using var reader = cmd.ExecuteReader();
        var list = new List<SetupFile>();
        while (reader.Read()) list.Add(ReadSetupFile(reader));
        return list;
    }

    private SetupFile ReadSetupFile(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), FileId = r.GetString(1), FileName = r.GetString(2),
        FilePath = r.GetString(3), StoredPath = r.GetString(4),
        CarName = r.GetString(5), CarId = r.GetInt32(6),
        TrackName = r.GetString(7), TrackId = r.GetInt32(8),
        DetectedAt = r.GetString(9), ModifiedAt = r.GetString(10),
        RawContent = r.GetString(11), ParsedValues = r.GetString(12),
        Notes = r.GetString(13), SyncStatus = r.GetString(14),
    };

    // ═══════════════════════════════════════
    // COACHING EVENTS
    // ═══════════════════════════════════════

    public void InsertCoachingEvent(CoachingEvent e)
    {
        Execute(@"
            INSERT INTO coaching_events (session_id, lap_number, lap_dist_pct, timestamp_ms,
                event_type, severity, message, data)
            VALUES (@sid, @ln, @ldp, @ts, @et, @sev, @msg, @data)",
            ("@sid", e.SessionId), ("@ln", e.LapNumber), ("@ldp", e.LapDistPct),
            ("@ts", e.TimestampMs), ("@et", e.EventType), ("@sev", e.Severity),
            ("@msg", e.Message), ("@data", e.Data));
    }

    // ═══════════════════════════════════════
    // SYNC QUEUE
    // ═══════════════════════════════════════

    public void EnqueueSync(string itemType, string itemId)
    {
        Execute(@"INSERT INTO sync_queue (item_type, item_id, status, created_at)
            VALUES (@t, @id, 'pending', @ca)",
            ("@t", itemType), ("@id", itemId), ("@ca", DateTime.UtcNow.ToString("o")));
    }

    public List<SyncQueueItem> GetPendingSyncItems(int limit = 50)
    {
        var list = new List<SyncQueueItem>();
        using var cmd = CreateCommand($"SELECT * FROM sync_queue WHERE status IN ('pending', 'failed') AND retry_count < 5 ORDER BY id LIMIT {limit}");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SyncQueueItem
            {
                Id = reader.GetInt64(0), ItemType = reader.GetString(1),
                ItemId = reader.GetString(2), Status = reader.GetString(3),
                RetryCount = reader.GetInt32(4), LastError = reader.GetString(5),
                CreatedAt = reader.GetString(6), LastAttemptAt = reader.GetString(7),
            });
        }
        return list;
    }

    public void UpdateSyncItem(long id, string status, string? error = null)
    {
        if (error != null)
            Execute("UPDATE sync_queue SET status=@s, last_error=@e, retry_count=retry_count+1, last_attempt_at=@la WHERE id=@id",
                ("@s", status), ("@e", error), ("@la", DateTime.UtcNow.ToString("o")), ("@id", id));
        else
            Execute("UPDATE sync_queue SET status=@s, last_attempt_at=@la WHERE id=@id",
                ("@s", status), ("@la", DateTime.UtcNow.ToString("o")), ("@id", id));
    }

    // ═══════════════════════════════════════
    // SETTINGS
    // ═══════════════════════════════════════

    public string GetSetting(string key, string defaultValue = "")
    {
        using var cmd = CreateCommand("SELECT value FROM app_settings WHERE key=@k", ("@k", key));
        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        Execute("INSERT OR REPLACE INTO app_settings (key, value) VALUES (@k, @v)", ("@k", key), ("@v", value));
    }

    // ═══════════════════════════════════════
    // STATS
    // ═══════════════════════════════════════

    public int GetSessionCount() => QueryInt("SELECT COUNT(*) FROM sessions");
    public int GetUnsyncedCount() => QueryInt("SELECT COUNT(*) FROM sessions WHERE sync_status != 'synced'");
    public int GetSetupFileCount() => QueryInt("SELECT COUNT(*) FROM setup_files");
    public long GetTelemetrySampleCount() => QueryLong("SELECT COUNT(*) FROM telemetry");

    // ═══════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════

    private void Execute(string sql, params (string name, object value)[] parms)
    {
        if (_conn == null) return;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parms)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand CreateCommand(string sql, params (string name, object value)[] parms)
    {
        var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parms)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    private int QueryInt(string sql)
    {
        using var cmd = CreateCommand(sql);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private long QueryLong(string sql)
    {
        using var cmd = CreateCommand(sql);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    // ═══════════════════════════════════════
    // TRACK MAPS
    // ═══════════════════════════════════════

    public void InsertTrackMap(string trackName, int trackId, string carClass, string carName,
        float trackLengthKm, string source, string cornersJson)
    {
        Execute(@"
            INSERT OR REPLACE INTO track_maps
                (track_name, track_id, car_class, car_name, track_length_km, source,
                 corners_json, created_at, updated_at)
            VALUES (@tn, @tid, @cc, @cn, @tlk, @src, @cj, @ca, @ua)",
            ("@tn", trackName), ("@tid", trackId), ("@cc", carClass), ("@cn", carName),
            ("@tlk", trackLengthKm), ("@src", source), ("@cj", cornersJson),
            ("@ca", DateTime.UtcNow.ToString("o")), ("@ua", DateTime.UtcNow.ToString("o")));
    }

    public TrackMapRecord? GetTrackMap(string trackName, string carClass)
    {
        using var cmd = CreateCommand(
            "SELECT * FROM track_maps WHERE track_name=@tn AND car_class=@cc",
            ("@tn", trackName), ("@cc", carClass));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new TrackMapRecord
        {
            Id = reader.GetInt64(0), TrackName = reader.GetString(1),
            TrackId = reader.GetInt32(2), CarClass = reader.GetString(3),
            CarName = reader.GetString(4), TrackLengthKm = reader.GetFloat(5),
            Source = reader.GetString(6), CornersJson = reader.GetString(7),
            CreatedAt = reader.GetString(8), UpdatedAt = reader.GetString(9)
        };
    }

    public List<TrackMapRecord> GetAllTrackMaps()
    {
        var list = new List<TrackMapRecord>();
        using var cmd = CreateCommand("SELECT * FROM track_maps ORDER BY track_name");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TrackMapRecord
            {
                Id = reader.GetInt64(0), TrackName = reader.GetString(1),
                TrackId = reader.GetInt32(2), CarClass = reader.GetString(3),
                CarName = reader.GetString(4), TrackLengthKm = reader.GetFloat(5),
                Source = reader.GetString(6), CornersJson = reader.GetString(7),
                CreatedAt = reader.GetString(8), UpdatedAt = reader.GetString(9)
            });
        }
        return list;
    }

    public void DeleteTrackMap(string trackName, string carClass)
    {
        Execute("DELETE FROM track_maps WHERE track_name=@tn AND car_class=@cc",
            ("@tn", trackName), ("@cc", carClass));
    }

    // ═══════════════════════════════════════
    // CORNER PERFORMANCE
    // ═══════════════════════════════════════

    public void InsertCornerPerformance(Core.CornerPerformance perf)
    {
        Execute(@"
            INSERT INTO corner_performance
                (session_id, corner_id, lap_number, brake_start_dist_pct, peak_brake_pressure,
                 brake_release_rate, entry_speed, apex_speed, min_speed, throttle_pickup_dist_pct,
                 exit_speed, gear_at_entry, gear_at_apex, steering_corrections, delta_gained_lost,
                 mistake_category)
            VALUES (@sid, @cid, @ln, @bsdp, @pbp, @brr, @es, @as, @ms, @tpd, @exs, @gae, @gaa,
                @sc, @dgl, @mc)",
            ("@sid", perf.SessionId), ("@cid", perf.CornerId), ("@ln", perf.LapNumber),
            ("@bsdp", perf.BrakeStartDistPct), ("@pbp", perf.PeakBrakePressure),
            ("@brr", perf.BrakeReleaseRate), ("@es", perf.EntrySpeed), ("@as", perf.ApexSpeed),
            ("@ms", perf.MinSpeed), ("@tpd", perf.ThrottlePickupDistPct), ("@exs", perf.ExitSpeed),
            ("@gae", perf.GearAtEntry), ("@gaa", perf.GearAtApex), ("@sc", perf.SteeringCorrectionCount),
            ("@dgl", perf.DeltaGainedLost), ("@mc", perf.MistakeCategory));
    }

    public CornerPerformanceRecord? GetCornerPerformance(string sessionId, string cornerId, int lapNumber)
    {
        using var cmd = CreateCommand(@"
            SELECT * FROM corner_performance
            WHERE session_id=@sid AND corner_id=@cid AND lap_number=@ln",
            ("@sid", sessionId), ("@cid", cornerId), ("@ln", lapNumber));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new CornerPerformanceRecord
        {
            Id = reader.GetInt64(0), SessionId = reader.GetString(1), CornerId = reader.GetString(2),
            LapNumber = reader.GetInt32(3), BrakeStartDistPct = reader.GetFloat(4),
            PeakBrakePressure = reader.GetFloat(5), BrakeReleaseRate = reader.GetFloat(6),
            EntrySpeed = reader.GetFloat(7), ApexSpeed = reader.GetFloat(8), MinSpeed = reader.GetFloat(9),
            ThrottlePickupDistPct = reader.GetFloat(10), ExitSpeed = reader.GetFloat(11),
            GearAtEntry = reader.GetInt32(12), GearAtApex = reader.GetInt32(13),
            SteeringCorrections = reader.GetInt32(14), DeltaGainedLost = reader.GetFloat(15),
            MistakeCategory = reader.GetString(16)
        };
    }

    public List<CornerPerformanceRecord> GetCornerPerformanceRange(string sessionId, string cornerId,
        int fromLap, int toLap)
    {
        var list = new List<CornerPerformanceRecord>();
        using var cmd = CreateCommand(@"
            SELECT * FROM corner_performance
            WHERE session_id=@sid AND corner_id=@cid AND lap_number BETWEEN @fl AND @tl
            ORDER BY lap_number",
            ("@sid", sessionId), ("@cid", cornerId), ("@fl", fromLap), ("@tl", toLap));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new CornerPerformanceRecord
            {
                Id = reader.GetInt64(0), SessionId = reader.GetString(1), CornerId = reader.GetString(2),
                LapNumber = reader.GetInt32(3), BrakeStartDistPct = reader.GetFloat(4),
                PeakBrakePressure = reader.GetFloat(5), BrakeReleaseRate = reader.GetFloat(6),
                EntrySpeed = reader.GetFloat(7), ApexSpeed = reader.GetFloat(8), MinSpeed = reader.GetFloat(9),
                ThrottlePickupDistPct = reader.GetFloat(10), ExitSpeed = reader.GetFloat(11),
                GearAtEntry = reader.GetInt32(12), GearAtApex = reader.GetInt32(13),
                SteeringCorrections = reader.GetInt32(14), DeltaGainedLost = reader.GetFloat(15),
                MistakeCategory = reader.GetString(16)
            });
        }
        return list;
    }

    public List<CornerPerformanceRecord> GetCornerPerformanceForSession(string sessionId)
    {
        var list = new List<CornerPerformanceRecord>();
        using var cmd = CreateCommand(@"
            SELECT * FROM corner_performance
            WHERE session_id=@sid
            ORDER BY lap_number, corner_id",
            ("@sid", sessionId));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new CornerPerformanceRecord
            {
                Id = reader.GetInt64(0), SessionId = reader.GetString(1), CornerId = reader.GetString(2),
                LapNumber = reader.GetInt32(3), BrakeStartDistPct = reader.GetFloat(4),
                PeakBrakePressure = reader.GetFloat(5), BrakeReleaseRate = reader.GetFloat(6),
                EntrySpeed = reader.GetFloat(7), ApexSpeed = reader.GetFloat(8), MinSpeed = reader.GetFloat(9),
                ThrottlePickupDistPct = reader.GetFloat(10), ExitSpeed = reader.GetFloat(11),
                GearAtEntry = reader.GetInt32(12), GearAtApex = reader.GetInt32(13),
                SteeringCorrections = reader.GetInt32(14), DeltaGainedLost = reader.GetFloat(15),
                MistakeCategory = reader.GetString(16)
            });
        }
        return list;
    }

    // ═══════════════════════════════════════
    // COACHING INSTRUCTIONS
    // ═══════════════════════════════════════

    public void InsertCoachingInstruction(Core.CoachingInstruction instr)
    {
        Execute(@"
            INSERT OR IGNORE INTO coaching_instructions
                (corner_id, mistake_category, instruction_text, confidence_score,
                 times_given, times_improved, times_worsened, times_no_change)
            VALUES (@cid, @mc, @it, @cs, @tg, @ti, @tw, @tnc)",
            ("@cid", instr.CornerId), ("@mc", instr.MistakeCategory),
            ("@it", instr.InstructionText), ("@cs", instr.ConfidenceScore),
            ("@tg", instr.TimesGiven), ("@ti", instr.TimesImproved),
            ("@tw", instr.TimesWorsened), ("@tnc", instr.TimesNoChange));
    }

    public List<Core.CoachingInstruction> GetCoachingInstructions(string cornerId, string mistakeCategory)
    {
        var list = new List<Core.CoachingInstruction>();
        using var cmd = CreateCommand(@"
            SELECT id, corner_id, mistake_category, instruction_text, confidence_score,
                   times_given, times_improved, times_worsened, times_no_change
            FROM coaching_instructions
            WHERE corner_id=@cid AND mistake_category=@mc
            ORDER BY confidence_score DESC",
            ("@cid", cornerId), ("@mc", mistakeCategory));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Core.CoachingInstruction
            {
                Id = reader.GetInt32(0).ToString(), CornerId = reader.GetString(1),
                MistakeCategory = reader.GetString(2), InstructionText = reader.GetString(3),
                ConfidenceScore = reader.GetFloat(4), TimesGiven = reader.GetInt32(5),
                TimesImproved = reader.GetInt32(6), TimesWorsened = reader.GetInt32(7),
                TimesNoChange = reader.GetInt32(8)
            });
        }
        return list;
    }

    public List<Core.CoachingInstruction> GetAllCoachingInstructions()
    {
        var list = new List<Core.CoachingInstruction>();
        using var cmd = CreateCommand(@"
            SELECT id, corner_id, mistake_category, instruction_text, confidence_score,
                   times_given, times_improved, times_worsened, times_no_change
            FROM coaching_instructions
            ORDER BY confidence_score DESC");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Core.CoachingInstruction
            {
                Id = reader.GetInt32(0).ToString(), CornerId = reader.GetString(1),
                MistakeCategory = reader.GetString(2), InstructionText = reader.GetString(3),
                ConfidenceScore = reader.GetFloat(4), TimesGiven = reader.GetInt32(5),
                TimesImproved = reader.GetInt32(6), TimesWorsened = reader.GetInt32(7),
                TimesNoChange = reader.GetInt32(8)
            });
        }
        return list;
    }

    public void UpdateCoachingInstructionGiven(string id)
    {
        Execute("UPDATE coaching_instructions SET times_given = times_given + 1 WHERE id=@id", ("@id", id));
    }

    public void UpdateCoachingInstructionConfidence(string id, float confidence, string result)
    {
        if (result == "improved")
            Execute("UPDATE coaching_instructions SET confidence_score=@cs, times_improved=times_improved+1 WHERE id=@id",
                ("@cs", confidence), ("@id", id));
        else if (result == "worsened")
            Execute("UPDATE coaching_instructions SET confidence_score=@cs, times_worsened=times_worsened+1 WHERE id=@id",
                ("@cs", confidence), ("@id", id));
        else
            Execute("UPDATE coaching_instructions SET confidence_score=@cs, times_no_change=times_no_change+1 WHERE id=@id",
                ("@cs", confidence), ("@id", id));
    }

    // ═══════════════════════════════════════
    // COACHING OUTCOMES
    // ═══════════════════════════════════════

    public void InsertCoachingOutcome(Core.CoachingOutcome outcome)
    {
        Execute(@"
            INSERT INTO coaching_outcomes
                (instruction_id, corner_id, lap_given, lap_result, delta_before, delta_after, result)
            VALUES (@iid, @cid, @lg, @lr, @db, @da, @res)",
            ("@iid", int.Parse(outcome.InstructionId)), ("@cid", outcome.CornerId),
            ("@lg", outcome.LapGiven), ("@lr", outcome.LapResult),
            ("@db", outcome.DeltaBefore), ("@da", outcome.DeltaAfter), ("@res", outcome.Result));
    }

    // ═══════════════════════════════════════
    // VOICE CALL HISTORY
    // ═══════════════════════════════════════

    public void InsertVoiceCallHistory(string sessionId, string cornerId, int lapNumber,
        string callText, string callType, long timestampMs)
    {
        Execute(@"
            INSERT INTO voice_call_history
                (session_id, corner_id, lap_number, call_text, call_type, timestamp_ms)
            VALUES (@sid, @cid, @ln, @ct, @ctype, @ts)",
            ("@sid", sessionId), ("@cid", cornerId), ("@ln", lapNumber),
            ("@ct", callText), ("@ctype", callType), ("@ts", timestampMs));
    }

    public List<VoiceCallHistoryRecord> GetVoiceCallHistory(string sessionId)
    {
        var list = new List<VoiceCallHistoryRecord>();
        using var cmd = CreateCommand(@"
            SELECT * FROM voice_call_history
            WHERE session_id=@sid
            ORDER BY timestamp_ms",
            ("@sid", sessionId));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new VoiceCallHistoryRecord
            {
                Id = reader.GetInt64(0), SessionId = reader.GetString(1),
                CornerId = reader.GetString(2), LapNumber = reader.GetInt32(3),
                CallText = reader.GetString(4), CallType = reader.GetString(5),
                TimestampMs = reader.GetInt64(6)
            });
        }
        return list;
    }

    public void Dispose()
    {
        _conn?.Close();
        _conn?.Dispose();
        GC.SuppressFinalize(this);
    }
}
