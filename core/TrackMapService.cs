// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Track Map Service
// Manages track maps: load, save, CRUD operations via SQLite.
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

/// <summary>
/// Service for managing track maps. Handles persistence via SQLite database.
/// Provides load, save, import, and export functionality.
/// </summary>
public class TrackMapService
{
    private readonly ChiefDatabase _db;
    private readonly Dictionary<string, TrackMap> _cache = new();

    public TrackMapService(ChiefDatabase db)
    {
        _db = db;
        InitializeTables();
    }

    // ═══════════════════════════════════════
    // TABLE INITIALIZATION
    // ═══════════════════════════════════════

    private void InitializeTables()
    {
        // Create track_maps table
        _db.Execute(@"
            CREATE TABLE IF NOT EXISTS track_maps (
                id TEXT PRIMARY KEY,
                track_name TEXT NOT NULL,
                track_display_name TEXT DEFAULT '',
                track_id INTEGER DEFAULT 0,
                car_class TEXT NOT NULL,
                car_name TEXT DEFAULT '',
                track_length_km REAL DEFAULT 0,
                source TEXT DEFAULT 'manual',
                created_at TEXT DEFAULT '',
                updated_at TEXT DEFAULT '',
                UNIQUE(track_name, track_id, car_class, car_name)
            )");

        // Create track_corners table
        _db.Execute(@"
            CREATE TABLE IF NOT EXISTS track_corners (
                id TEXT PRIMARY KEY,
                track_map_id TEXT NOT NULL,
                track_name TEXT DEFAULT '',
                car_class TEXT DEFAULT '',
                car_name TEXT DEFAULT '',
                corner_name TEXT DEFAULT '',
                corner_number INTEGER DEFAULT 0,
                start_dist_pct REAL DEFAULT 0,
                brake_zone_dist_pct REAL DEFAULT 0,
                turn_in_dist_pct REAL DEFAULT 0,
                apex_dist_pct REAL DEFAULT 0,
                exit_dist_pct REAL DEFAULT 0,
                brake_marker TEXT DEFAULT '',
                target_brake_pressure REAL DEFAULT 0,
                target_entry_speed REAL DEFAULT 0,
                target_min_speed REAL DEFAULT 0,
                target_gear INTEGER DEFAULT 0,
                target_throttle_pickup REAL DEFAULT 0,
                target_exit_speed REAL DEFAULT 0,
                default_voice_call TEXT DEFAULT '',
                notes TEXT DEFAULT '',
                corner_type TEXT DEFAULT '',
                is_left_turn INTEGER DEFAULT 0,
                FOREIGN KEY (track_map_id) REFERENCES track_maps(id)
            )");
    }

    // ═══════════════════════════════════════
    // LOAD / SAVE
    // ═══════════════════════════════════════

    /// <summary>
    /// Load a track map for the given track and car class.
    /// Returns null if no map exists.
    /// </summary>
    public TrackMap? LoadTrackMap(string trackName, string carClass)
    {
        // Check cache first
        string cacheKey = $"{trackName}_{carClass}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            // Query track_maps table
            using var reader = _db.Query($@"
                SELECT id, track_name, track_display_name, track_id, car_class, car_name,
                       track_length_km, source, created_at, updated_at
                FROM track_maps
                WHERE track_name = ? AND car_class = ?
                LIMIT 1",
                new[] { trackName, carClass });

            if (!reader.Read())
            {
                return null; // No map found
            }

            var map = new TrackMap
            {
                Id = reader.GetString(0),
                TrackName = reader.GetString(1),
                TrackDisplayName = reader.GetString(2),
                TrackId = reader.GetInt32(3),
                CarClass = reader.GetString(4),
                CarName = reader.GetString(5),
                TrackLengthKm = reader.GetFloat(6),
                Source = reader.GetString(7),
                CreatedAt = reader.GetString(8),
                UpdatedAt = reader.GetString(9)
            };

            // Load corners for this map
            LoadCornersForMap(map);

            // Cache it
            _cache[cacheKey] = map;

            return map;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading track map: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Save a track map to the database.
    /// Overwrites existing map with same track/car_class.
    /// </summary>
    public void SaveTrackMap(TrackMap map)
    {
        try
        {
            map.UpdatedAt = DateTime.UtcNow.ToString("O");
            if (string.IsNullOrEmpty(map.CreatedAt))
            {
                map.CreatedAt = map.UpdatedAt;
            }

            // Upsert track_maps
            _db.Execute(@"
                INSERT OR REPLACE INTO track_maps
                (id, track_name, track_display_name, track_id, car_class, car_name,
                 track_length_km, source, created_at, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                new object?[]
                {
                    map.Id, map.TrackName, map.TrackDisplayName, map.TrackId,
                    map.CarClass, map.CarName, map.TrackLengthKm, map.Source,
                    map.CreatedAt, map.UpdatedAt
                });

            // Delete existing corners for this map
            _db.Execute("DELETE FROM track_corners WHERE track_map_id = ?", new[] { map.Id });

            // Insert all corners
            foreach (var corner in map.Corners)
            {
                if (string.IsNullOrEmpty(corner.Id))
                {
                    corner.Id = Guid.NewGuid().ToString();
                }

                _db.Execute(@"
                    INSERT INTO track_corners
                    (id, track_map_id, track_name, car_class, car_name, corner_name,
                     corner_number, start_dist_pct, brake_zone_dist_pct, turn_in_dist_pct,
                     apex_dist_pct, exit_dist_pct, brake_marker, target_brake_pressure,
                     target_entry_speed, target_min_speed, target_gear, target_throttle_pickup,
                     target_exit_speed, default_voice_call, notes, corner_type, is_left_turn)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    new object?[]
                    {
                        corner.Id, map.Id, corner.TrackName, corner.CarClass, corner.CarName,
                        corner.CornerName, corner.CornerNumber, corner.StartDistPct,
                        corner.BrakeZoneDistPct, corner.TurnInDistPct, corner.ApexDistPct,
                        corner.ExitDistPct, corner.BrakeMarker, corner.TargetBrakePressure,
                        corner.TargetEntrySpeed, corner.TargetMinSpeed, corner.TargetGear,
                        corner.TargetThrottlePickup, corner.TargetExitSpeed,
                        corner.DefaultVoiceCall, corner.Notes, corner.CornerType,
                        corner.IsLeftTurn ? 1 : 0
                    });
            }

            // Update cache
            string cacheKey = $"{map.TrackName}_{map.CarClass}";
            _cache[cacheKey] = map;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving track map: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Get all available track maps (summary info only, not full corner data).
    /// </summary>
    public List<TrackMap> GetAvailableMaps()
    {
        var maps = new List<TrackMap>();

        try
        {
            using var reader = _db.Query(@"
                SELECT id, track_name, track_display_name, track_id, car_class, car_name,
                       track_length_km, source, created_at, updated_at
                FROM track_maps
                ORDER BY track_name, car_class");

            while (reader.Read())
            {
                maps.Add(new TrackMap
                {
                    Id = reader.GetString(0),
                    TrackName = reader.GetString(1),
                    TrackDisplayName = reader.GetString(2),
                    TrackId = reader.GetInt32(3),
                    CarClass = reader.GetString(4),
                    CarName = reader.GetString(5),
                    TrackLengthKm = reader.GetFloat(6),
                    Source = reader.GetString(7),
                    CreatedAt = reader.GetString(8),
                    UpdatedAt = reader.GetString(9)
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting available maps: {ex.Message}");
        }

        return maps;
    }

    // ═══════════════════════════════════════
    // IMPORT / EXPORT
    // ═══════════════════════════════════════

    /// <summary>
    /// Import a track map from JSON string.
    /// Assumes JSON represents a TrackMap object with Corners array.
    /// </summary>
    public TrackMap? ImportFromJson(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var map = JsonSerializer.Deserialize<TrackMap>(json, options);

            if (map == null) return null;

            // Assign ID if missing
            if (string.IsNullOrEmpty(map.Id))
            {
                map.Id = Guid.NewGuid().ToString();
            }

            // Assign corner IDs if missing
            foreach (var corner in map.Corners)
            {
                if (string.IsNullOrEmpty(corner.Id))
                {
                    corner.Id = Guid.NewGuid().ToString();
                }
            }

            return map;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error importing track map from JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Export a track map to JSON string.
    /// </summary>
    public string ExportToJson(TrackMap map)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(map, options);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error exporting track map to JSON: {ex.Message}");
            return "";
        }
    }

    // ═══════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════

    private void LoadCornersForMap(TrackMap map)
    {
        try
        {
            using var reader = _db.Query(@"
                SELECT id, track_name, car_class, car_name, corner_name, corner_number,
                       start_dist_pct, brake_zone_dist_pct, turn_in_dist_pct, apex_dist_pct,
                       exit_dist_pct, brake_marker, target_brake_pressure, target_entry_speed,
                       target_min_speed, target_gear, target_throttle_pickup, target_exit_speed,
                       default_voice_call, notes, corner_type, is_left_turn
                FROM track_corners
                WHERE track_map_id = ?
                ORDER BY corner_number",
                new[] { map.Id });

            while (reader.Read())
            {
                map.Corners.Add(new TrackCorner
                {
                    Id = reader.GetString(0),
                    TrackName = reader.GetString(1),
                    CarClass = reader.GetString(2),
                    CarName = reader.GetString(3),
                    CornerName = reader.GetString(4),
                    CornerNumber = reader.GetInt32(5),
                    StartDistPct = reader.GetFloat(6),
                    BrakeZoneDistPct = reader.GetFloat(7),
                    TurnInDistPct = reader.GetFloat(8),
                    ApexDistPct = reader.GetFloat(9),
                    ExitDistPct = reader.GetFloat(10),
                    BrakeMarker = reader.GetString(11),
                    TargetBrakePressure = reader.GetFloat(12),
                    TargetEntrySpeed = reader.GetFloat(13),
                    TargetMinSpeed = reader.GetFloat(14),
                    TargetGear = reader.GetInt32(15),
                    TargetThrottlePickup = reader.GetFloat(16),
                    TargetExitSpeed = reader.GetFloat(17),
                    DefaultVoiceCall = reader.GetString(18),
                    Notes = reader.GetString(19),
                    CornerType = reader.GetString(20),
                    IsLeftTurn = reader.GetInt32(21) != 0
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading corners for map: {ex.Message}");
        }
    }
}
