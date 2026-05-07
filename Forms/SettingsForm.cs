// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Settings Form
// Configure API sync, telemetry rate, paths, coaching options.
// ═══════════════════════════════════════════════════════════════

using ChiefLogger.Config;
using ChiefLogger.Data;

namespace ChiefLogger.Forms;

public class SettingsForm : Form
{
    private readonly ChiefDatabase _db;
    private readonly ChiefSettings _settings;

    private TextBox _txtApiUrl = null!;
    private TextBox _txtToken = null!;
    private TextBox _txtSupabaseUrl = null!;
    private TextBox _txtSupabaseKey = null!;
    private TextBox _txtSetupFolder = null!;
    private NumericUpDown _nudSampleRate = null!;
    private NumericUpDown _nudSyncInterval = null!;
    private CheckBox _chkAutoSync = null!;
    private CheckBox _chkMinToTray = null!;
    private CheckBox _chkStartMinimized = null!;
    private CheckBox _chkAutoLog = null!;
    private ComboBox _cmbCoachMode = null!;
    private ComboBox _cmbCoachIntensity = null!;

    private static readonly Color BG = Color.FromArgb(10, 10, 18);
    private static readonly Color BG_CARD = Color.FromArgb(28, 28, 40);
    private static readonly Color TEXT = Color.FromArgb(232, 236, 244);
    private static readonly Color DIM = Color.FromArgb(100, 110, 130);
    private static readonly Color ACCENT = Color.FromArgb(163, 255, 0);

    public SettingsForm(ChiefDatabase db, ChiefSettings settings)
    {
        _db = db;
        _settings = settings;
        InitializeUI();
    }

    private void InitializeUI()
    {
        Text = "Chief Logger — Settings";
        Size = new Size(480, 640);
        BackColor = BG;
        ForeColor = TEXT;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);

        var scroll = new Panel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
        };
        Controls.Add(scroll);

        int y = 15;

        // ═══ SYNC SETTINGS ═══
        AddSection(scroll, "API / SYNC", ref y);

        AddLabel(scroll, "Chief API URL:", ref y);
        _txtApiUrl = AddTextBox(scroll, _settings.ChiefApiUrl, ref y);

        AddLabel(scroll, "User Token:", ref y);
        _txtToken = AddTextBox(scroll, _settings.UserToken, ref y, true);

        AddLabel(scroll, "Supabase URL:", ref y);
        _txtSupabaseUrl = AddTextBox(scroll, _settings.SupabaseUrl, ref y);

        AddLabel(scroll, "Supabase Anon Key:", ref y);
        _txtSupabaseKey = AddTextBox(scroll, _settings.SupabaseAnonKey, ref y, true);

        _chkAutoSync = AddCheckBox(scroll, "Auto-sync when online", _settings.AutoSync, ref y);

        AddLabel(scroll, "Sync interval (seconds):", ref y);
        _nudSyncInterval = AddNumeric(scroll, _settings.SyncIntervalSeconds, 10, 600, ref y);

        y += 10;

        // ═══ TELEMETRY ═══
        AddSection(scroll, "TELEMETRY", ref y);

        AddLabel(scroll, "Sample rate (Hz):", ref y);
        _nudSampleRate = AddNumeric(scroll, _settings.TelemetrySampleRateHz, 5, 60, ref y);

        _chkAutoLog = AddCheckBox(scroll, "Auto-log when iRacing connects", _settings.AutoLogSessions, ref y);

        y += 10;

        // ═══ PATHS ═══
        AddSection(scroll, "PATHS", ref y);

        AddLabel(scroll, "iRacing setup folder:", ref y);
        _txtSetupFolder = AddTextBox(scroll, _settings.IRacingSetupFolder, ref y);

        var btnBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(340, y - 34),
            Size = new Size(80, 28),
            BackColor = BG_CARD,
            ForeColor = DIM,
            FlatStyle = FlatStyle.Flat,
        };
        btnBrowse.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _txtSetupFolder.Text };
            if (dlg.ShowDialog() == DialogResult.OK) _txtSetupFolder.Text = dlg.SelectedPath;
        };
        scroll.Controls.Add(btnBrowse);

        y += 10;

        // ═══ UI ═══
        AddSection(scroll, "INTERFACE", ref y);
        _chkMinToTray = AddCheckBox(scroll, "Minimize to system tray", _settings.MinimizeToTray, ref y);
        _chkStartMinimized = AddCheckBox(scroll, "Start minimized", _settings.StartMinimized, ref y);

        y += 10;

        // ═══ COACHING ═══
        AddSection(scroll, "COACHING (FUTURE)", ref y);

        AddLabel(scroll, "Mode:", ref y);
        _cmbCoachMode = AddComboBox(scroll, new[] { "off", "spotter", "coach", "both" }, _settings.CoachingMode, ref y);

        AddLabel(scroll, "Intensity:", ref y);
        _cmbCoachIntensity = AddComboBox(scroll, new[] { "calm", "aggressive" }, _settings.CoachingIntensity, ref y);

        y += 20;

        // ═══ SAVE / CANCEL ═══
        var btnSave = new Button
        {
            Text = "Save Settings",
            Location = new Point(20, y),
            Size = new Size(200, 40),
            BackColor = ACCENT,
            ForeColor = BG,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnSave.Click += (s, e) => SaveAndClose();
        scroll.Controls.Add(btnSave);

        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(230, y),
            Size = new Size(100, 40),
            BackColor = BG_CARD,
            ForeColor = DIM,
            FlatStyle = FlatStyle.Flat,
        };
        btnCancel.Click += (s, e) => Close();
        scroll.Controls.Add(btnCancel);
    }

    private void SaveAndClose()
    {
        _settings.ChiefApiUrl = _txtApiUrl.Text.Trim();
        _settings.UserToken = _txtToken.Text.Trim();
        _settings.SupabaseUrl = _txtSupabaseUrl.Text.Trim();
        _settings.SupabaseAnonKey = _txtSupabaseKey.Text.Trim();
        _settings.IRacingSetupFolder = _txtSetupFolder.Text.Trim();
        _settings.TelemetrySampleRateHz = (int)_nudSampleRate.Value;
        _settings.SyncIntervalSeconds = (int)_nudSyncInterval.Value;
        _settings.AutoSync = _chkAutoSync.Checked;
        _settings.AutoLogSessions = _chkAutoLog.Checked;
        _settings.MinimizeToTray = _chkMinToTray.Checked;
        _settings.StartMinimized = _chkStartMinimized.Checked;
        _settings.CoachingMode = _cmbCoachMode.SelectedItem?.ToString() ?? "off";
        _settings.CoachingIntensity = _cmbCoachIntensity.SelectedItem?.ToString() ?? "calm";

        _settings.SaveToDatabase(_db);
        Close();
    }

    // ═══ HELPERS ═══

    private void AddSection(Panel parent, string title, ref int y)
    {
        var lbl = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = ACCENT,
            Location = new Point(20, y),
            AutoSize = true,
        };
        parent.Controls.Add(lbl);
        y += 22;
    }

    private void AddLabel(Panel parent, string text, ref int y)
    {
        var lbl = new Label
        {
            Text = text,
            ForeColor = DIM,
            Location = new Point(20, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
        };
        parent.Controls.Add(lbl);
        y += 18;
    }

    private TextBox AddTextBox(Panel parent, string value, ref int y, bool password = false)
    {
        var txt = new TextBox
        {
            Text = value,
            Location = new Point(20, y),
            Size = new Size(310, 28),
            BackColor = BG_CARD,
            ForeColor = TEXT,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f),
        };
        if (password) txt.UseSystemPasswordChar = true;
        parent.Controls.Add(txt);
        y += 34;
        return txt;
    }

    private CheckBox AddCheckBox(Panel parent, string text, bool value, ref int y)
    {
        var chk = new CheckBox
        {
            Text = text,
            Checked = value,
            ForeColor = TEXT,
            Location = new Point(20, y),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
        };
        parent.Controls.Add(chk);
        y += 26;
        return chk;
    }

    private NumericUpDown AddNumeric(Panel parent, int value, int min, int max, ref int y)
    {
        var nud = new NumericUpDown
        {
            Value = Math.Clamp(value, min, max),
            Minimum = min,
            Maximum = max,
            Location = new Point(20, y),
            Size = new Size(100, 28),
            BackColor = BG_CARD,
            ForeColor = TEXT,
        };
        parent.Controls.Add(nud);
        y += 34;
        return nud;
    }

    private ComboBox AddComboBox(Panel parent, string[] items, string selected, ref int y)
    {
        var cmb = new ComboBox
        {
            Location = new Point(20, y),
            Size = new Size(200, 28),
            BackColor = BG_CARD,
            ForeColor = TEXT,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
        };
        cmb.Items.AddRange(items);
        cmb.SelectedItem = selected;
        parent.Controls.Add(cmb);
        y += 34;
        return cmb;
    }
}
