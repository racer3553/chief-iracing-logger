// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — iRacing Logger Entry Point
// Initializes all services and launches the main form.
// ════════════���═══════════════════════════════��══════════════════

using ChiefLogger.Config;
using ChiefLogger.Core;
using ChiefLogger.Data;
using ChiefLogger.Forms;

namespace ChiefLogger;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // ═══ SETTINGS ��══
        var settings = new ChiefSettings();
        Directory.CreateDirectory(settings.DataFolder);
        Directory.CreateDirectory(settings.SetupStorageFolder);

        // ═══ DATABASE ═══
        var db = new ChiefDatabase(settings.DatabasePath);
        db.Initialize();
        settings.LoadFromDatabase(db);

        var coachingSettings = new CoachingSettings();
        coachingSettings.LoadFromDatabase(db);

        // ═══ CORE SERVICES ═══
        var connection = new IRacingConnection();
        var events = new EventHooks(db);
        var recorder = new TelemetryRecorder(connection.Sdk, db)
        {
            SampleRateHz = settings.TelemetrySampleRateHz,
            BatchSize = settings.TelemetryBatchSize,
        };
        var sessionManager = new SessionManager(connection.Sdk, db, recorder, events);
        var setupWatcher = new SetupWatcher(db, settings.IRacingSetupFolder, settings.SetupStorageFolder);
        var syncManager = new SyncManager(db, settings);

        // ═══ COACHING SERVICES ═══
        var voiceEngine = new VoiceEngine();
        voiceEngine.Enabled = coachingSettings.VoiceEnabled;

        var talkTiming = new TalkTimingSystem(connection.Sdk);

        var coachingConfig = new CoachingConfig(
            coachingSettings.ReactiveCoachEnabled,
            settings.CoachingMode,
            settings.CoachingIntensity,
            true, true, true, true, true, true, true, true, true
        );

        var coachingEngine = new CoachingEngine(events, db, coachingConfig);
        var trackMapService = new TrackMapService(db);
        var cornerTracker = new CornerPerformanceTracker(db, trackMapService);
        var coachingLearner = new CoachingLearner(db);
        var predictiveCoach = new PredictiveCoach(
            connection.Sdk, trackMapService, cornerTracker,
            voiceEngine, talkTiming, coachingConfig
        );

        // Wire coaching → voice
        coachingEngine.OnCoachingMessage += msg =>
        {
            if (talkTiming.CanSpeakPriority(msg.Priority))
                voiceEngine.Enqueue(msg.Text, msg.Priority);
        };

        // ═══ LAUNCH ═══
        var mainForm = new MainForm(
            db, settings, connection, recorder,
            sessionManager, setupWatcher, events, syncManager,
            coachingEngine, voiceEngine, predictiveCoach, coachingSettings,
            cornerTracker, coachingLearner, talkTiming
        );

        if (settings.StartMinimized)
        {
            mainForm.WindowState = FormWindowState.Minimized;
            mainForm.ShowInTaskbar = false;
        }

        Application.Run(mainForm);

        // ═══ CLEANUP ═══
        voiceEngine.Dispose();
        syncManager.Dispose();
        setupWatcher.Dispose();
        sessionManager.Dispose();
        recorder.Dispose();
        connection.Dispose();
        db.Dispose();
    }
}
