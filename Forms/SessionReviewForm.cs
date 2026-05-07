// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Session Review Form
// Displays last session: laps, telemetry traces, setup, notes.
// ═══════════════════════════════════════════════════════════════

using ChiefLogger.Data;

namespace ChiefLogger.Forms;

public class SessionReviewForm : Form
{
    private readonly ChiefDatabase _db;

    private ListBox _lstSessions = null!;
    private ListBox _lstLaps = null!;
    private Panel _pnlTraces = null!;
    private Label _lblSessionInfo = null!;
    private Label _lblBestLap = null!;
    private Label _lblSetup = null!;
    private TextBox _txtNotes = null!;
    private Button _btnSaveNotes = null!;

    // Trace data for selected lap
    private List<TelemetryRecord> _traceData = new();
    private string _selectedSessionId = "";

    private static readonly Color BG = Color.FromArgb(10, 10, 18);
    private static readonly Color BG_CARD = Color.FromArgb(28, 28, 40);
    private static readonly Color TEXT = Color.FromArgb(232, 236, 244);
    private static readonly Color DIM = Color.FromArgb(100, 110, 130);
    private static readonly Color ACCENT = Color.FromArgb(163, 255, 0);
    private static readonly Color RED = Color.FromArgb(239, 68, 68);
    private static readonly Color BLUE = Color.FromArgb(96, 165, 250);
    private static readonly Color GREEN = Color.FromArgb(34, 197, 94);
    private static readonly Color YELLOW = Color.FromArgb(250, 204, 21);

    public SessionReviewForm(ChiefDatabase db)
    {
        _db = db;
        InitializeUI();
        LoadSessions();
    }

    private void InitializeUI()
    {
        Text = "Chief Logger — Session Review";
        Size = new Size(800, 620);
        BackColor = BG;
        ForeColor = TEXT;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(700, 500);

        // ═══ LEFT: Session List ═══
        var lblSessions = new Label
        {
            Text = "SESSIONS",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = ACCENT,
            Location = new Point(10, 10),
            AutoSize = true,
        };
        Controls.Add(lblSessions);

        _lstSessions = new ListBox
        {
            Location = new Point(10, 32),
            Size = new Size(220, 200),
            BackColor = BG_CARD,
            ForeColor = TEXT,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
        };
        _lstSessions.SelectedIndexChanged += OnSessionSelected;
        Controls.Add(_lstSessions);

        // ═══ Session Info ═══
        _lblSessionInfo = new Label
        {
            Location = new Point(240, 10),
            Size = new Size(540, 50),
            ForeColor = TEXT,
            Font = new Font("Segoe UI", 10f),
        };
        Controls.Add(_lblSessionInfo);

        _lblBestLap = new Label
        {
            Location = new Point(240, 55),
            Size = new Size(200, 22),
            ForeColor = ACCENT,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        };
        Controls.Add(_lblBestLap);

        _lblSetup = new Label
        {
            Location = new Point(450, 55),
            Size = new Size(320, 22),
            ForeColor = DIM,
        };
        Controls.Add(_lblSetup);

        // ═══ Lap List ═══
        var lblLaps = new Label
        {
            Text = "LAPS",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = ACCENT,
            Location = new Point(10, 240),
            AutoSize = true,
        };
        Controls.Add(lblLaps);

        _lstLaps = new ListBox
        {
            Location = new Point(10, 262),
            Size = new Size(220, 200),
            BackColor = BG_CARD,
            ForeColor = TEXT,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
        };
        _lstLaps.SelectedIndexChanged += OnLapSelected;
        Controls.Add(_lstLaps);

        // ═══ Telemetry Trace Panel ═══
        _pnlTraces = new DoubleBufferedPanel
        {
            Location = new Point(240, 85),
            Size = new Size(540, 350),
            BackColor = BG_CARD,
        };
        _pnlTraces.Paint += PaintTraces;
        Controls.Add(_pnlTraces);

        // ═══ Notes ═══
        var lblNotes = new Label
        {
            Text = "NOTES",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = ACCENT,
            Location = new Point(10, 470),
            AutoSize = true,
        };
        Controls.Add(lblNotes);

        _txtNotes = new TextBox
        {
            Location = new Point(10, 492),
            Size = new Size(540, 70),
            BackColor = BG_CARD,
            ForeColor = TEXT,
            BorderStyle = BorderStyle.FixedSingle,
            Multiline = true,
            Font = new Font("Segoe UI", 9f),
        };
        Controls.Add(_txtNotes);

        _btnSaveNotes = new Button
        {
            Text = "Save Notes",
            Location = new Point(560, 492),
            Size = new Size(110, 35),
            BackColor = ACCENT,
            ForeColor = BG,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _btnSaveNotes.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_selectedSessionId))
            {
                _db.UpdateSessionNotes(_selectedSessionId, _txtNotes.Text);
            }
        };
        Controls.Add(_btnSaveNotes);

        // ═══ LEGEND ═══
        var legendY = 445;
        AddLegendItem(240, legendY, "Throttle", GREEN);
        AddLegendItem(330, legendY, "Brake", RED);
        AddLegendItem(400, legendY, "Steering", YELLOW);
        AddLegendItem(490, legendY, "Speed", BLUE);
    }

    // ═══════════════════════════════════════
    // DATA LOADING
    // ═══════════════════════════════════════

    private void LoadSessions()
    {
        _lstSessions.Items.Clear();
        var sessions = _db.GetRecentSessions(30);
        foreach (var s in sessions)
        {
            var dateStr = DateTime.TryParse(s.StartedAt, out var dt) ? dt.ToString("MM/dd HH:mm") : "?";
            _lstSessions.Items.Add(new SessionListItem
            {
                Session = s,
                Display = $"{dateStr} {s.CarScreenName[..Math.Min(12, s.CarScreenName.Length)]} {s.BestLapTime:F2}s"
            });
        }
    }

    private void OnSessionSelected(object? sender, EventArgs e)
    {
        if (_lstSessions.SelectedItem is not SessionListItem item) return;

        var s = item.Session;
        _selectedSessionId = s.SessionId;

        _lblSessionInfo.Text = $"{s.CarScreenName}\n{s.TrackDisplayName} ({s.SessionType})";
        _lblBestLap.Text = s.BestLapTime > 0 ? $"Best: {s.BestLapTime:F3}s (Lap {s.BestLapNumber})" : "No valid laps";
        _lblSetup.Text = !string.IsNullOrEmpty(s.SetupFileId) ? $"Setup: {s.SetupFileId}" : "No setup linked";
        _txtNotes.Text = s.Notes;

        // Load laps
        _lstLaps.Items.Clear();
        var laps = _db.GetLapsForSession(s.SessionId);
        foreach (var l in laps)
        {
            var delta = l.DeltaToBest >= 0 ? $"+{l.DeltaToBest:F3}" : $"{l.DeltaToBest:F3}";
            var valid = l.IsValid ? "" : " ✘";
            _lstLaps.Items.Add(new LapListItem
            {
                Lap = l,
                Display = $"L{l.LapNumber:D2} {l.LapTime:F3}s ({delta}){valid}"
            });
        }

        _traceData.Clear();
        _pnlTraces.Invalidate();
    }

    private void OnLapSelected(object? sender, EventArgs e)
    {
        if (_lstLaps.SelectedItem is not LapListItem item) return;

        _traceData = _db.GetTelemetryForLap(_selectedSessionId, item.Lap.LapNumber);
        _pnlTraces.Invalidate();
    }

    // ═══════════════════════════════════════
    // TRACE PAINTING
    // ═══════════════════════════════════════

    private void PaintTraces(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var w = _pnlTraces.Width;
        var h = _pnlTraces.Height;

        // Background grid
        using var gridPen = new Pen(Color.FromArgb(20, 255, 255, 255));
        for (int i = 0; i <= 10; i++)
        {
            var y = i * h / 10;
            g.DrawLine(gridPen, 0, y, w, y);
        }
        for (int i = 0; i <= 10; i++)
        {
            var x = i * w / 10;
            g.DrawLine(gridPen, x, 0, x, h);
        }

        if (_traceData.Count < 2)
        {
            using var font = new Font("Segoe UI", 12f);
            g.DrawString("Select a lap to view traces", font, new SolidBrush(DIM), w / 2 - 100, h / 2 - 10);
            return;
        }

        // Normalize: X = LapDistPct (0-1), Y = value (0-1 for each channel)
        var maxSpeed = _traceData.Max(t => t.Speed);
        if (maxSpeed < 1) maxSpeed = 1;

        DrawTrace(g, w, h, t => t.LapDistPct, t => t.Throttle, GREEN, 2f);
        DrawTrace(g, w, h, t => t.LapDistPct, t => t.Brake, RED, 2f);
        DrawTrace(g, w, h, t => t.LapDistPct, t => Math.Abs(t.Steering) / 3.14f, YELLOW, 1.5f); // Normalize ~pi radians
        DrawTrace(g, w, h, t => t.LapDistPct, t => t.Speed / maxSpeed, BLUE, 1.5f);
    }

    private void DrawTrace(Graphics g, int w, int h,
        Func<TelemetryRecord, float> getX, Func<TelemetryRecord, float> getY,
        Color color, float width)
    {
        if (_traceData.Count < 2) return;

        using var pen = new Pen(color, width);
        var points = new PointF[_traceData.Count];

        for (int i = 0; i < _traceData.Count; i++)
        {
            var x = getX(_traceData[i]) * w;
            var y = h - (Math.Clamp(getY(_traceData[i]), 0f, 1f) * h);
            points[i] = new PointF(x, y);
        }

        try { g.DrawLines(pen, points); } catch { }
    }

    private void AddLegendItem(int x, int y, string label, Color color)
    {
        var box = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(12, 12),
            BackColor = color,
        };
        Controls.Add(box);

        var lbl = new Label
        {
            Text = label,
            Location = new Point(x + 16, y - 2),
            AutoSize = true,
            ForeColor = DIM,
            Font = new Font("Segoe UI", 8f),
        };
        Controls.Add(lbl);
    }

    // ═══ HELPER CLASSES ═══

    private class SessionListItem
    {
        public LoggedSession Session { get; set; } = null!;
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    private class LapListItem
    {
        public LoggedLap Lap { get; set; } = null!;
        public string Display { get; set; } = "";
        public override string ToString() => Display;
    }

    private class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; }
    }
}
