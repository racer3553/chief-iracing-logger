// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Sync Manager
// Handles offline-first sync to Chief web app API.
// Phase 1: Structure + mock. Phase 2: Live API connection.
// ═══════════════════════════════════════════════════════════════

using System.Net.Http.Json;
using System.Text.Json;
using ChiefLogger.Config;
using ChiefLogger.Data;

namespace ChiefLogger.Core;

public class SyncManager : IDisposable
{
    private readonly ChiefDatabase _db;
    private readonly ChiefSettings _settings;
    private readonly HttpClient _http;
    private Timer? _syncTimer;
    private bool _syncing;

    // Status
    public bool IsOnline { get; private set; }
    public int PendingCount { get; private set; }
    public int SyncedCount { get; private set; }
    public int FailedCount { get; private set; }
    public string LastSyncTime { get; private set; } = "Never";
    public string LastError { get; private set; } = "";

    // Events
    public event Action<string>? OnSyncStatus;
    public event Action<int>? OnSyncProgress;

    public SyncManager(ChiefDatabase db, ChiefSettings settings)
    {
        _db = db;
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    // ═══ START / STOP ═══

    public void Start()
    {
        if (!_settings.AutoSync) return;

        _syncTimer = new Timer(
            _ => _ = TrySyncAsync(),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(_settings.SyncIntervalSeconds)
        );
    }

    public void Stop()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
    }

    // ═══ MANUAL SYNC ═══

    public async Task TrySyncAsync()
    {
        if (_syncing) return;
        if (string.IsNullOrEmpty(_settings.UserToken) && string.IsNullOrEmpty(_settings.SupabaseAnonKey))
        {
            OnSyncStatus?.Invoke("No API credentials configured");
            return;
        }

        _syncing = true;
        OnSyncStatus?.Invoke("Syncing...");

        try
        {
            // Check connectivity
            IsOnline = await CheckConnectivityAsync();
            if (!IsOnline)
            {
                OnSyncStatus?.Invoke("Offline — will sync later");
                return;
            }

            // Get pending items
            var pending = _db.GetPendingSyncItems();
            PendingCount = pending.Count;

            if (pending.Count == 0)
            {
                OnSyncStatus?.Invoke("Up to date");
                return;
            }

            int synced = 0;
            foreach (var item in pending)
            {
                try
                {
                    _db.UpdateSyncItem(item.Id, "syncing");

                    bool success = item.ItemType switch
                    {
                        "session" => await SyncSessionAsync(item.ItemId),
                        "setup" => await SyncSetupAsync(item.ItemId),
                        _ => false
                    };

                    if (success)
                    {
                        _db.UpdateSyncItem(item.Id, "synced");
                        if (item.ItemType == "session")
                            _db.UpdateSessionSyncStatus(item.ItemId, "synced");
                        synced++;
                    }
                    else
                    {
                        _db.UpdateSyncItem(item.Id, "failed", "Unknown error");
                    }
                }
                catch (Exception ex)
                {
                    _db.UpdateSyncItem(item.Id, "failed", ex.Message);
                    LastError = ex.Message;
                }

                OnSyncProgress?.Invoke(synced);
            }

            SyncedCount += synced;
            FailedCount = pending.Count - synced;
            LastSyncTime = DateTime.Now.ToString("HH:mm:ss");
            OnSyncStatus?.Invoke($"Synced {synced}/{pending.Count} items");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            OnSyncStatus?.Invoke($"Sync error: {ex.Message}");
        }
        finally
        {
            _syncing = false;
        }
    }

    // ═══ SYNC INDIVIDUAL ITEMS ═══

    private async Task<bool> SyncSessionAsync(string sessionId)
    {
        var session = _db.GetSession(sessionId);
        if (session == null) return false;

        var laps = _db.GetLapsForSession(sessionId);

        var payload = new
        {
            session = new
            {
                session_id = session.SessionId,
                iracing_sub_session_id = session.IRacingSubSessionId,
                driver_name = session.DriverName,
                driver_id = session.DriverId,
                car_name = session.CarName,
                car_screen_name = session.CarScreenName,
                car_id = session.CarId,
                track_name = session.TrackName,
                track_display_name = session.TrackDisplayName,
                track_id = session.TrackId,
                track_config = session.TrackConfig,
                session_type = session.SessionType,
                air_temp = session.AirTemp,
                track_temp = session.TrackTemp,
                skies = session.Skies,
                wind_speed = session.WindSpeed,
                humidity = session.Humidity,
                started_at = session.StartedAt,
                ended_at = session.EndedAt,
                total_laps = session.TotalLaps,
                best_lap_time = session.BestLapTime,
                best_lap_number = session.BestLapNumber,
                final_position = session.FinalPosition,
                incident_count = session.IncidentCount,
                fuel_used_total = session.FuelUsedTotal,
                fuel_per_lap = session.FuelPerLap,
                setup_file_id = session.SetupFileId,
                notes = session.Notes,
            },
            laps = laps.Select(l => new
            {
                lap_number = l.LapNumber,
                lap_time = l.LapTime,
                is_valid = l.IsValid,
                incidents = l.IncidentsThisLap,
                fuel_used = l.FuelUsed,
                fuel_remaining = l.FuelRemaining,
                max_speed = l.MaxSpeed,
                min_speed = l.MinSpeed,
                avg_throttle = l.AvgThrottle,
                avg_brake = l.AvgBrake,
                max_lat_g = l.MaxLatG,
                max_long_g = l.MaxLongG,
                position = l.Position,
                delta_to_best = l.DeltaToBest,
                gear_changes = l.GearChanges,
            }),
        };

        return await PostToApiAsync("/api/sim/sync-session", payload);
    }

    private async Task<bool> SyncSetupAsync(string fileId)
    {
        var setups = _db.GetSetupFiles();
        var setup = setups.FirstOrDefault(s => s.FileId == fileId);
        if (setup == null) return false;

        var payload = new
        {
            file_id = setup.FileId,
            file_name = setup.FileName,
            car_name = setup.CarName,
            car_id = setup.CarId,
            track_name = setup.TrackName,
            track_id = setup.TrackId,
            detected_at = setup.DetectedAt,
            modified_at = setup.ModifiedAt,
            raw_content = setup.RawContent,
            parsed_values = setup.ParsedValues,
            notes = setup.Notes,
        };

        return await PostToApiAsync("/api/sim/sync-setup", payload);
    }

    // ═══ API CALLS ═══

    private async Task<bool> PostToApiAsync(string endpoint, object payload)
    {
        // Phase 1: Mock mode — log what would be sent
        if (string.IsNullOrEmpty(_settings.ChiefApiUrl) ||
            _settings.ChiefApiUrl == "https://chiefracing.com/api")
        {
            // In mock mode, just pretend it worked
            System.Diagnostics.Debug.WriteLine($"[SYNC MOCK] POST {endpoint}: {JsonSerializer.Serialize(payload)[..Math.Min(200, JsonSerializer.Serialize(payload).Length)]}...");
            await Task.Delay(100); // Simulate network
            return true;
        }

        // Real mode
        var url = _settings.ChiefApiUrl.TrimEnd('/') + endpoint;
        var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (!string.IsNullOrEmpty(_settings.UserToken))
            request.Headers.Add("Authorization", $"Bearer {_settings.UserToken}");
        if (!string.IsNullOrEmpty(_settings.SupabaseAnonKey))
            request.Headers.Add("apikey", _settings.SupabaseAnonKey);

        request.Content = JsonContent.Create(payload);

        var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> CheckConnectivityAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ChiefApiUrl)) return false;
            var response = await _http.GetAsync(_settings.ChiefApiUrl.TrimEnd('/') + "/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
