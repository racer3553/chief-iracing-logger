// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Application Settings
// ═══════════════════════════════════════════════════════════════

using System.Text.Json;
using ChiefLogger.Data;

namespace ChiefLogger.Config;

public class ChiefSettings
{
    // ═══ PATHS ═══
    public string DataFolder { get; set; }
    public string SetupStorageFolder { get; set; }
    public string DatabasePath { get; set; }

    // ═══ iRACING ═══
    public string IRacingSetupFolder { get; set; }
    public int TelemetrySampleRateHz { get; set; } = 20;
    public bool AutoLogSessions { get; set; } = true;
    public int TelemetryBatchSize { get; set; } = 200;  // Flush every N samples

    // ═══ SYNC ═══
    public string ChiefApiUrl { get; set; } = "https://chiefracing.com/api";
    public string UserToken { get; set; } = "";
    public string SupabaseUrl { get; set; } = "";
    public string SupabaseAnonKey { get; set; } = "";
    public bool AutoSync { get; set; } = false;
    public int SyncIntervalSeconds { get; set; } = 60;

    // ═══ UI ═══
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool LaunchOnStartup { get; set; } = false;

    // ═══ COACHING ═══
    public bool CoachingEnabled { get; set; } = false;
    public string CoachingMode { get; set; } = "off";  // off, spotter, coach, both
    public string CoachingIntensity { get; set; } = "calm"; // calm, aggressive

    public ChiefSettings()
    {
        // Default paths
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChiefRacing", "Logger"
        );
        DataFolder = appData;
        SetupStorageFolder = Path.Combine(appData, "setups");
        DatabasePath = Path.Combine(appData, "chief-logger.db");

        // iRacing default setup folder
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        IRacingSetupFolder = Path.Combine(docs, "iRacing", "setups");
    }

    // ═══ LOAD / SAVE from database ═══

    public void LoadFromDatabase(ChiefDatabase db)
    {
        TelemetrySampleRateHz = int.TryParse(db.GetSetting("telemetry_hz", "20"), out int hz) ? hz : 20;
        AutoLogSessions = db.GetSetting("auto_log", "true") == "true";
        TelemetryBatchSize = int.TryParse(db.GetSetting("telemetry_batch", "200"), out int batch) ? batch : 200;

        ChiefApiUrl = db.GetSetting("api_url", ChiefApiUrl);
        UserToken = db.GetSetting("user_token", "");
        SupabaseUrl = db.GetSetting("supabase_url", "");
        SupabaseAnonKey = db.GetSetting("supabase_anon_key", "");
        AutoSync = db.GetSetting("auto_sync", "false") == "true";
        SyncIntervalSeconds = int.TryParse(db.GetSetting("sync_interval", "60"), out int si) ? si : 60;

        MinimizeToTray = db.GetSetting("minimize_to_tray", "true") == "true";
        StartMinimized = db.GetSetting("start_minimized", "false") == "true";
        LaunchOnStartup = db.GetSetting("launch_on_startup", "false") == "true";

        CoachingEnabled = db.GetSetting("coaching_enabled", "false") == "true";
        CoachingMode = db.GetSetting("coaching_mode", "off");
        CoachingIntensity = db.GetSetting("coaching_intensity", "calm");

        var customSetupFolder = db.GetSetting("iracing_setup_folder", "");
        if (!string.IsNullOrEmpty(customSetupFolder)) IRacingSetupFolder = customSetupFolder;
    }

    public void SaveToDatabase(ChiefDatabase db)
    {
        db.SetSetting("telemetry_hz", TelemetrySampleRateHz.ToString());
        db.SetSetting("auto_log", AutoLogSessions.ToString().ToLower());
        db.SetSetting("telemetry_batch", TelemetryBatchSize.ToString());
        db.SetSetting("api_url", ChiefApiUrl);
        db.SetSetting("user_token", UserToken);
        db.SetSetting("supabase_url", SupabaseUrl);
        db.SetSetting("supabase_anon_key", SupabaseAnonKey);
        db.SetSetting("auto_sync", AutoSync.ToString().ToLower());
        db.SetSetting("sync_interval", SyncIntervalSeconds.ToString());
        db.SetSetting("minimize_to_tray", MinimizeToTray.ToString().ToLower());
        db.SetSetting("start_minimized", StartMinimized.ToString().ToLower());
        db.SetSetting("launch_on_startup", LaunchOnStartup.ToString().ToLower());
        db.SetSetting("coaching_enabled", CoachingEnabled.ToString().ToLower());
        db.SetSetting("coaching_mode", CoachingMode);
        db.SetSetting("coaching_intensity", CoachingIntensity);
        db.SetSetting("iracing_setup_folder", IRacingSetupFolder);
    }
}
