// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Main Form (Windows Desktop + System Tray)
// Shows connection status, current session, live data, controls.
// ═══════════════════════════════════════════════════════════════

using ChiefLogger.Config;
using ChiefLogger.Core;
using ChiefLogger.Data;

namespace ChiefLogger.Forms;

public class MainForm : Form
{
    // ═══ SERVICES ═══
    private readonly ChiefDatabase _db;
    private readonly ChiefSettings _settings;
    private readonly IRacingConnection _connection;
    private readonly TelemetryRecorder _recorder;
    private readonly SessionManager _sessionManager;
    private readonly SetupWatcher _setupWatcher;
    private readonly EventHooks _events;
    private readonly SyncManager _syncManager;
    private readonly CoachingEngine _coachingEngine;
    private readonly VoiceEngine _voiceEngine;
    private readonly PredictiveCoach _predictiveCoach;
    private readonly CoachingSettings _coachingSettings;
    private readonly CornerPerformanceTracker _cornerTracker;
    private readonly CoachingLearner _coachingLearner;
    private readonly TalkTimingSystem _talkTiming;

    // ═══ UI CONTROLS ═══
    private NotifyIcon _trayIcon = null!;
    private Label _lblStatus = null!;
    private Label _lblCar = null!;
    private Label _lblTrack = null!;
    private Label _lblSession = null!;
    private Label _lblLap = null!;
    private Label _lblBestLap = null!;
    private Label _lblSpeed = null!;
    private Label _lblThrottle = null!;
    private Label _lblBrake = null!;
    private Label _lblGear = null!;
    private Label _lblRPM = null!;
    private Label _lblFuel = null!;
    private Label _lblIncidents = null!;
    private Label _lblPosition = null!;
    private Label _lblSyncStatus = null!;
    private Label _lblVoiceStatus = null!;
    private Label _lblSamples = null!;
    private Label _lblSetups = null!;
    private Button _btnStartLog = null!;
    private Button _btnStopLog = null!;
    private Button _btnOpenLogs = null!;
    private Button _btnSettings = null!;
    private Button _btnReview = null!;
    private Button _btnSync = null!;
    private CheckBox _chkAutoLog = null!;
    private ListBox _lstEvents = null!;
    private System.Windows.Forms.Timer _uiTimer = null!;

    // ═══ COLORS ═══
    private static readonly Color BG_DARK = Color.FromArgb(10, 10, 18);
    private static readonly Color BG_CARD = Color.FromArgb(28, 28, 40);
    private static readonly Color ACCENT = Color.FromArgb(163, 255, 0);
    private static readonly Color TEXT = Color.FromArgb(232, 236, 244);
    private static readonly Color TEXT_DIM = Color.FromArgb(100, 110, 130);
    private static readonly Color RED = Color.FromArgb(239, 68, 68);
    private static readonly Color GREEN = Color.FromArgb(34, 197, 94);
    private static readonly Color CYAN = Color.FromArgb(6, 182, 212);

    public MainForm(ChiefDatabase db, ChiefSettings settings, IRacingConnection connection,
        TelemetryRecorder recorder, SessionManager sessionManager, SetupWatcher setupWatcher,
        EventHooks events, SyncManager syncManager, CoachingEngine coachingEngine,
        VoiceEngine voiceEngine, PredictiveCoach predictiveCoach, CoachingSettings coachingSettings,
        CornerPerformanceTracker cornerTracker, CoachingLearner coachingLearner, TalkTimingSystem talkTiming)
    {
        _db = db;
        _settings = settings;
        _connection = connection;
        _recorder = recorder;
        _sessionManager = sessionManager;
        _setupWatcher = setupWatcher;
        _events = events;
        _syncManager = syncManager;
        _coachingEngine = coachingEngine;
        _voiceEngine = voiceEngine;
        _predictiveCoach = predictiveCoach;
        _coachingSettings = coachingSettings;
        _cornerTracker = cornerTracker;
        _coachingLearner = coachingLearner;
        _talkTiming = talkTiming;

        InitializeUI();
        WireEvents();
        StartServices();
    }

    // ═══════════════════════════════════════
    // UI INITIALIZATION
    // ═══════════════════════════════════════

    private void InitializeUI()
    {
        Text = "Chief Racing — iRacing Logger";
        Size = new Size(520, 780);
        MinimumSize = new Size(480, 700);
        BackColor = BG_DARK;
        ForeColor = TEXT;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        // System tray
        _trayIcon = new NotifyIcon
        {
            Text = "Chief Racing Logger",
            Visible = true,
        };
        // Use a simple generated icon since we don't have the .ico file
        _trayIcon.Icon = CreateSimpleIcon();
        Icon = _trayIcon.Icon;
        _trayIcon.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; };

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; });
        trayMenu.Items.Add("Exit", null, (s, e) => { _trayIcon.Visible = false; Application.Exit(); });
        _trayIcon.ContextMenuStrip = trayMenu;

        var y = 12;

        // ═══ HEADER ═══
        var lblTitle = CreateLabel("CHIEF RACING", 16, true, ACCENT);
        lblTitle.Location = new Point(20, y);
        lblTitle.AutoSize = true;
        Controls.Add(lblTitle);

        var lblSub = CreateLabel("iRacing Logger v1.0", 9, false, TEXT_DIM);
        lblSub.Location = new Point(20, y + 28);
        lblSub.AutoSize = true;
        Controls.Add(lblSub);

        y += 55;

        // ═══ CONNECTION STATUS ═══
        _lblStatus = CreateLabel("● DISCONNECTED", 12, true, RED);
        _lblStatus.Location = new Point(20, y);
        _lblStatus.Size = new Size(460, 26);
        Controls.Add(_lblStatus);
        y += 35;

        // ═══ SESSION INFO CARD ═══
        var sessionPanel = CreateCard(20, y, 460, 130);
        Controls.Add(sessionPanel);

        _lblCar = CreateLabel("Car: —", 10, false, TEXT);
        _lblCar.Location = new Point(12, 10);
        _lblCar.Size = new Size(430, 22);
        sessionPanel.Controls.Add(_lblCar);

        _lblTrack = CreateLabel("Track: —", 10, false, TEXT);
        _lblTrack.Location = new Point(12, 34);
        _lblTrack.Size = new Size(430, 22);
        sessionPanel.Controls.Add(_lblTrack);

        _lblSession = CreateLabel("Session: —", 10, false, TEXT_DIM);
        _lblSession.Location = new Point(12, 58);
        _lblSession.Size = new Size(210, 22);
        sessionPanel.Controls.Add(_lblSession);

        _lblLap = CreateLabel("Lap: —", 10, false, TEXT);
        _lblLap.Location = new Point(230, 58);
        _lblLap.Size = new Size(210, 22);
        sessionPanel.Controls.Add(_lblLap);

        _lblBestLap = CreateLabel("Best: —", 11, true, ACCENT);
        _lblBestLap.Location = new Point(12, 84);
        _lblBestLap.Size = new Size(210, 24);
        sessionPanel.Controls.Add(_lblBestLap);

        _lblPosition = CreateLabel("P: —", 11, true, CYAN);
        _lblPosition.Location = new Point(230, 84);
        _lblPosition.Size = new Size(210, 24);
        sessionPanel.Controls.Add(_lblPosition);

        y += 142;

        // ═══ LIVE DATA GRID ═══
        var dataPanel = CreateCard(20, y, 460, 95);
        Controls.Add(dataPanel);

        int col1 = 12, col2 = 120, col3 = 228, col4 = 345;
        int row1 = 8, row2 = 35, row3 = 62;

        _lblSpeed = CreateDataLabel("SPD: —", col1, row1, dataPanel);
        _lblThrottle = CreateDataLabel("THR: —", col2, row1, dataPanel);
        _lblBrake = CreateDataLabel("BRK: —", col3, row1, dataPanel);
        _lblGear = CreateDataLabel("GEAR: —", col4, row1, dataPanel);
        _lblRPM = CreateDataLabel("RPM: —", col1, row2, dataPanel);
        _lblFuel = CreateDataLabel("FUEL: —", col2, row2, dataPanel);
        _lblIncidents = CreateDataLabel("INC: —", col3, row2, dataPanel);
        _lblSamples = CreateDataLabel("REC: 0", col4, row2, dataPanel);
        _lblSetups = CreateDataLabel("SETUPS: 0", col1, row3, dataPanel);
        _lblSyncStatus = CreateDataLabel("SYNC: —", col2, row3, dataPanel);
        _lblVoiceStatus = CreateDataLabel("VOICE: 🔊", col3, row3, dataPanel);

        y += 107;

        // ═══ CONTROLS ═══
        var controlPanel = CreateCard(20, y, 460, 55);
        Controls.Add(controlPanel);

        _btnStartLog = CreateButton("▶ START LOG", ACCENT, BG_DARK, 10, 10, 120, 35);
        controlPanel.Controls.Add(_btnStartLog);
        _btnStartLog.Click += (s, e) => _sessionManager.StartLogging();

        _btnStopLog = CreateButton("■ STOP LOG", RED, Color.White, 138, 10, 110, 35);
        _btnStopLog.Enabled = false;
        controlPanel.Controls.Add(_btnStopLog);
        _btnStopLog.Click += (s, e) => _sessionManager.StopLogging();

        _chkAutoLog = new CheckBox
        {
            Text = "Auto-Log",
            Checked = _settings.AutoLogSessions,
            ForeColor = TEXT_DIM,
            Location = new Point(260, 16),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
        };
        controlPanel.Controls.Add(_chkAutoLog);
        _chkAutoLog.CheckedChanged += (s, e) => {
            _settings.AutoLogSessions = _chkAutoLog.Checked;
            _settings.SaveToDatabase(_db);
        };

        y += 67;

        // ═══ ACTION BUTTONS ═══
        var actPanel = CreateCard(20, y, 460, 55);
        Controls.Add(actPanel);

        _btnOpenLogs = CreateButton("📁 Logs", BG_CARD, TEXT, 8, 10, 80, 35);
        _btnOpenLogs.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
        actPanel.Controls.Add(_btnOpenLogs);
        _btnOpenLogs.Click += (s, e) => OpenLogsFolder();

        _btnReview = CreateButton("📊 Review", BG_CARD, TEXT, 96, 10, 80, 35);
        _btnReview.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
        actPanel.Controls.Add(_btnReview);
        _btnReview.Click += (s, e) => OpenReview();

        var btnCoach = CreateButton("🧠 Coach", BG_CARD, Color.FromArgb(163, 255, 0), 184, 10, 80, 35);
        btnCoach.FlatAppearance.BorderColor = Color.FromArgb(163, 255, 0, 60);
        actPanel.Controls.Add(btnCoach);
        btnCoach.Click += (s, e) => { using var f = new CornerReviewForm(_db); f.ShowDialog(this); };

        _btnSync = CreateButton("🔄 Sync", BG_CARD, CYAN, 272, 10, 88, 35);
        _btnSync.FlatAppearance.BorderColor = Color.FromArgb(6, 182, 212, 60);
        actPanel.Controls.Add(_btnSync);
        _btnSync.Click += async (s, e) => await _syncManager.TrySyncAsync();

        _btnSettings = CreateButton("⚙ Settings", BG_CARD, TEXT, 368, 10, 80, 35);
        _btnSettings.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
        actPanel.Controls.Add(_btnSettings);
        _btnSettings.Click += (s, e) => OpenSettings();

        y += 67;

        // ═══ EVENT LOG ═══
        var lblEvt = CreateLabel("EVENTS", 9, true, TEXT_DIM);
        lblEvt.Location = new Point(20, y);
        lblEvt.AutoSize = true;
        Controls.Add(lblEvt);
        y += 20;

        _lstEvents = new ListBox
        {
            Location = new Point(20, y),
            Size = new Size(460, 160),
            BackColor = BG_CARD,
            ForeColor = TEXT,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
        };
        Controls.Add(_lstEvents);

        // ═══ UI TIMER (update live data at ~10 Hz) ═══
        _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _uiTimer.Tick += UpdateUI;
        _uiTimer.Start();
    }

    // ═══════════════════════════════════════
    // WIRE EVENTS
    // ═══════════════════════════════════════

    private void WireEvents()
    {
        _connection.OnConnected += () => SafeInvoke(() =>
        {
            _lblStatus.Text = "● CONNECTED";
            _lblStatus.ForeColor = GREEN;
            _trayIcon.Text = "Chief Logger — Connected";

            if (_settings.AutoLogSessions && !_sessionManager.IsLogging)
                _sessionManager.StartLogging();
        });

        _connection.OnDisconnected += () => SafeInvoke(() =>
        {
            _lblStatus.Text = "● DISCONNECTED";
            _lblStatus.ForeColor = RED;
            _trayIcon.Text = "Chief Logger — Disconnected";

            if (_sessionManager.IsLogging)
                _sessionManager.StopLogging();
        });

        _connection.OnSessionInfoChanged += info => SafeInvoke(() =>
        {
            _lblCar.Text = $"Car: {info.CarScreenName}";
            _lblTrack.Text = $"Track: {info.TrackDisplayName}" +
                (string.IsNullOrEmpty(info.TrackConfig) ? "" : $" ({info.TrackConfig})");
            _lblSession.Text = $"Session: {info.SessionType}";

            // Update setup watcher context
            _setupWatcher.CurrentCarName = info.CarName;
            _setupWatcher.CurrentCarId = info.CarId;
            _setupWatcher.CurrentTrackName = info.TrackName;
            _setupWatcher.CurrentTrackId = info.TrackId;
        });

        _sessionManager.OnSessionStarted += session => SafeInvoke(() =>
        {
            _btnStartLog.Enabled = false;
            _btnStopLog.Enabled = true;
            AddEvent($"[SESSION] Started: {session.CarScreenName} @ {session.TrackDisplayName}");
        });

        _sessionManager.OnSessionEnded += session => SafeInvoke(() =>
        {
            _btnStartLog.Enabled = true;
            _btnStopLog.Enabled = false;
            AddEvent($"[SESSION] Ended: {session.TotalLaps} laps, best {session.BestLapTime:F3}s");
        });

        _sessionManager.OnLapCompleted += lap => SafeInvoke(() =>
        {
            AddEvent($"[LAP {lap.LapNumber}] {lap.LapTime:F3}s | P{lap.Position} | Fuel: {lap.FuelRemaining:F1}L");
        });

        _setupWatcher.OnSetupDetected += setup => SafeInvoke(() =>
        {
            AddEvent($"[SETUP] Detected: {setup.FileName} ({setup.CarName})");
        });

        _events.Subscribe(evt =>
        {
            if (evt.Severity == "warning" || evt.Severity == "critical")
            {
                SafeInvoke(() => AddEvent($"[{evt.Type.ToUpper()}] {evt.Message}"));
            }
        });

        _syncManager.OnSyncStatus += status => SafeInvoke(() =>
        {
            _lblSyncStatus.Text = $"SYNC: {status}";
        });

        // Wire coaching events
        _coachingEngine.OnCoachingMessage += msg => SafeInvoke(() =>
        {
            AddEvent($"[COACH] {msg.Text}");
        });

        _predictiveCoach.OnPredictiveCall += call => SafeInvoke(() =>
        {
            AddEvent($"[PREDICT] {call}");
        });
    }

    // ═══════════════════════════════════════
    // START SERVICES
    // ═══════════════════════════════════════

    private void StartServices()
    {
        _connection.StartPolling();
        _sessionManager.StartMonitoring();
        _setupWatcher.Start();
        _syncManager.Start();

        // Wire telemetry sample analysis for coaching events
        _recorder.OnSample += sample =>
        {
            _events.AnalyzeSample(sample, _sessionManager.CurrentSessionId);
            // Wire telemetry to coaching systems
            _talkTiming.UpdateState(sample);
            _predictiveCoach.Update(sample);
        };

        // Wire session events to coaching
        _sessionManager.OnSessionStarted += session =>
        {
            _predictiveCoach.OnSessionStarted(session.TrackName, session.CarScreenName);
            _predictiveCoach.SetSessionMode(_coachingSettings.SessionMode);
        };

        _sessionManager.OnLapCompleted += lap =>
        {
            _predictiveCoach.OnLapCompleted(lap.LapNumber);
            _cornerTracker.OnLapCompleted(lap.LapNumber, _sessionManager.CurrentSessionId);
            _coachingLearner.EvaluateOutcome("", lap.LapNumber);
        };

        AddEvent("[CHIEF] Logger started. Waiting for iRacing...");
        AddEvent($"[CHIEF] Setup folder: {_settings.IRacingSetupFolder}");
        AddEvent($"[CHIEF] Data folder: {_settings.DataFolder}");
    }

    // ═══════════════════════════════════════
    // UI UPDATE (10 Hz)
    // ═══════════════════════════════════════

    private void UpdateUI(object? sender, EventArgs e)
    {
        if (!_connection.IsConnected) return;

        var sdk = _connection.Sdk;
        var speedMph = sdk.GetFloat("Speed") * 2.23694f; // m/s to mph

        _lblLap.Text = $"Lap: {sdk.GetInt("Lap")}";
        _lblSpeed.Text = $"SPD: {speedMph:F0}";
        _lblThrottle.Text = $"THR: {sdk.GetFloat("Throttle") * 100:F0}%";
        _lblBrake.Text = $"BRK: {sdk.GetFloat("Brake") * 100:F0}%";
        _lblGear.Text = $"GEAR: {sdk.GetInt("Gear")}";
        _lblRPM.Text = $"RPM: {sdk.GetFloat("RPM"):F0}";
        _lblFuel.Text = $"FUEL: {sdk.GetFloat("FuelLevel"):F1}L";
        _lblIncidents.Text = $"INC: {sdk.GetInt("PlayerCarMyIncidentCount")}x";
        _lblPosition.Text = $"P{sdk.GetInt("PlayerCarPosition")}";

        if (_sessionManager.BestLapTime > 0)
            _lblBestLap.Text = $"Best: {_sessionManager.BestLapTime:F3}s";

        _lblSamples.Text = $"REC: {_recorder.TotalSamplesRecorded:N0}";
        _lblSetups.Text = $"SETUPS: {_db.GetSetupFileCount()}";

        // Update voice status indicator
        _lblVoiceStatus.Text = _voiceEngine.Enabled ? "VOICE: 🔊" : "VOICE: 🔇";
        _lblVoiceStatus.ForeColor = _voiceEngine.Enabled ? ACCENT : TEXT_DIM;
    }

    // ═══════════════════════════════════════
    // ACTIONS
    // ═══════════════════════════════════════

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(_settings.DataFolder);
        System.Diagnostics.Process.Start("explorer.exe", _settings.DataFolder);
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_db, _settings);
        form.ShowDialog(this);
    }

    private void OpenReview()
    {
        using var form = new SessionReviewForm(_db);
        form.ShowDialog(this);
    }

    // ═══════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════

    private void AddEvent(string msg)
    {
        var time = DateTime.Now.ToString("HH:mm:ss");
        _lstEvents.Items.Insert(0, $"[{time}] {msg}");
        while (_lstEvents.Items.Count > 200)
            _lstEvents.Items.RemoveAt(_lstEvents.Items.Count - 1);
    }

    private void SafeInvoke(Action action)
    {
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    private static Label CreateLabel(string text, float size, bool bold, Color color)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color,
            BackColor = Color.Transparent,
        };
    }

    private Label CreateDataLabel(string text, int x, int y, Panel parent)
    {
        var lbl = new Label
        {
            Text = text,
            Font = new Font("Consolas", 9.5f, FontStyle.Bold),
            ForeColor = TEXT,
            Location = new Point(x, y),
            Size = new Size(110, 22),
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    private static Panel CreateCard(int x, int y, int w, int h)
    {
        return new Panel
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = BG_CARD,
            BorderStyle = BorderStyle.None,
        };
    }

    private static Button CreateButton(string text, Color bg, Color fg, int x, int y, int w, int h)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
    }

    private static Icon CreateSimpleIcon()
    {
        // Generate a simple 16x16 icon (green square with C)
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(163, 255, 0));
        using var font = new Font("Arial", 9f, FontStyle.Bold);
        g.DrawString("C", font, Brushes.Black, 2, 0);
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ═══ FORM LIFECYCLE ═══

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _uiTimer.Stop();
        _syncManager.Stop();
        _setupWatcher.Stop();
        _sessionManager.StopMonitoring();
        _connection.StopPolling();
        _trayIcon.Visible = false;

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiTimer?.Dispose();
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
