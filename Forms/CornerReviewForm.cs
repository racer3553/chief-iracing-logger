// ═══════════════════════════════════════════════════════════════
// CHIEF RACING — Corner Review Form
// WinForms dialog showing corner-by-corner performance review.
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ChiefLogger.Core;
using ChiefLogger.Data;

namespace ChiefLogger.Forms;

/// <summary>
/// Dialog form that displays corner-by-corner analysis for a session.
/// Shows lap-by-lap metrics and detects patterns.
/// </summary>
public class CornerReviewForm : Form
{
    private const int BG_DARK_R = 10, BG_DARK_G = 10, BG_DARK_B = 18;
    private const int BG_CARD_R = 28, BG_CARD_G = 28, BG_CARD_B = 40;
    private const int ACCENT_R = 163, ACCENT_G = 255, ACCENT_B = 0;

    private readonly ChiefDatabase _db;
    private readonly CornerPerformanceTracker _tracker;
    private readonly CoachingLearner _learner;
    private string _selectedSessionId = "";
    private string _selectedCornerId = "";

    // UI Controls
    private ComboBox cmbSessions = null!;
    private ListBox lstCorners = null!;
    private DataGridView dgvPerformance = null!;
    private Label lblSuggestion = null!;
    private TextBox txtVoiceHistory = null!;

    public CornerReviewForm(ChiefDatabase db, CornerPerformanceTracker tracker, CoachingLearner learner)
    {
        _db = db;
        _tracker = tracker;
        _learner = learner;
        InitializeForm();
    }

    private void InitializeForm()
    {
        // Form properties
        Text = "Corner Review";
        Width = 900;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(BG_DARK_R, BG_DARK_G, BG_DARK_B);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        // Main layout
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4,
            AutoSize = false
        };

        // Row 0: Session selector
        var lblSession = new Label
        {
            Text = "Session:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true
        };
        cmbSessions = new ComboBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(BG_CARD_R, BG_CARD_G, BG_CARD_B),
            ForeColor = Color.White,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbSessions.SelectedIndexChanged += (s, e) => OnSessionChanged();

        mainLayout.Controls.Add(lblSession, 0, 0);
        mainLayout.Controls.Add(cmbSessions, 1, 0);

        // Row 1: Corner list (left) and performance table (right)
        var lblCorners = new Label
        {
            Text = "Corners:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = true
        };
        lstCorners = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(BG_CARD_R, BG_CARD_G, BG_CARD_B),
            ForeColor = Color.White
        };
        lstCorners.SelectedIndexChanged += (s, e) => OnCornerSelected();

        var cornerPanel = new Panel { Dock = DockStyle.Fill };
        cornerPanel.Controls.Add(lstCorners);

        var lblPerf = new Label
        {
            Text = "Performance by Lap:",
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = true,
            Height = 24
        };
        dgvPerformance = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(BG_CARD_R, BG_CARD_G, BG_CARD_B),
            ForeColor = Color.White,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            BorderStyle = BorderStyle.None
        };
        dgvPerformance.EnableHeadersVisualStyles = false;
        dgvPerformance.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(BG_DARK_R, BG_DARK_G, BG_DARK_B),
            ForeColor = Color.FromArgb(ACCENT_R, ACCENT_G, ACCENT_B),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        var perfPanel = new Panel { Dock = DockStyle.Fill };
        perfPanel.Controls.Add(dgvPerformance);
        perfPanel.Controls.Add(lblPerf);

        // Row 2: Suggestions and voice history
        var lblSuggest = new Label
        {
            Text = "Suggested Next Callout:",
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = true,
            Height = 24,
            ForeColor = Color.FromArgb(ACCENT_R, ACCENT_G, ACCENT_B)
        };
        lblSuggestion = new Label
        {
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = true,
            Height = 50,
            ForeColor = Color.White,
            Padding = new Padding(0, 4, 0, 8)
        };

        var lblVoice = new Label
        {
            Text = "What Chief Said:",
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = true,
            Height = 24,
            ForeColor = Color.FromArgb(ACCENT_R, ACCENT_G, ACCENT_B)
        };
        txtVoiceHistory = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(BG_CARD_R, BG_CARD_G, BG_CARD_B),
            ForeColor = Color.White,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        var historyPanel = new Panel { Dock = DockStyle.Fill };
        historyPanel.Controls.Add(txtVoiceHistory);
        historyPanel.Controls.Add(lblVoice);
        historyPanel.Controls.Add(lblSuggestion);
        historyPanel.Controls.Add(lblSuggest);

        // Add row 1 controls
        mainLayout.Controls.Add(lblCorners, 0, 1);
        mainLayout.Controls.Add(cornerPanel, 0, 1);
        mainLayout.Controls.Add(lblPerf, 1, 1);
        mainLayout.Controls.Add(perfPanel, 1, 1);

        // Add row 2 (full width)
        mainLayout.Controls.Add(historyPanel, 0, 2);
        mainLayout.SetColumnSpan(historyPanel, 2);

        // Set row heights
        mainLayout.RowStyles[0] = new RowStyle(SizeType.Absolute, 40);
        mainLayout.RowStyles[1] = new RowStyle(SizeType.Percent, 50);
        mainLayout.RowStyles[2] = new RowStyle(SizeType.Percent, 50);

        mainLayout.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 25);
        mainLayout.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 75);

        Controls.Add(mainLayout);

        // Load initial data
        LoadSessions();
    }

    private void LoadSessions()
    {
        var sessions = _db.GetRecentSessions(20);
        cmbSessions.Items.Clear();
        foreach (var session in sessions)
        {
            cmbSessions.Items.Add($"{session.TrackName} - {session.SessionType} - {session.StartedAt}", session);
        }
        if (cmbSessions.Items.Count > 0)
            cmbSessions.SelectedIndex = 0;
    }

    private void OnSessionChanged()
    {
        if (cmbSessions.SelectedItem is LoggedSession session)
        {
            _selectedSessionId = session.SessionId;
            LoadCorners();
        }
    }

    private void LoadCorners()
    {
        lstCorners.Items.Clear();

        // Get unique corners from corner_performance table for this session
        var cornerPerfs = _db.GetCornerPerformanceForSession(_selectedSessionId);
        var uniqueCorners = cornerPerfs.GroupBy(c => c.CornerId)
            .Select(g => g.Key)
            .ToList();

        foreach (var cornerId in uniqueCorners)
        {
            lstCorners.Items.Add(cornerId);
        }

        if (lstCorners.Items.Count > 0)
            lstCorners.SelectedIndex = 0;
    }

    private void OnCornerSelected()
    {
        if (lstCorners.SelectedItem is string cornerId)
        {
            _selectedCornerId = cornerId;
            LoadPerformanceData();
            LoadSuggestedCallout();
            LoadVoiceHistory();
        }
    }

    private void LoadPerformanceData()
    {
        dgvPerformance.DataSource = null;
        dgvPerformance.Columns.Clear();

        var cornerPerfs = _db.GetCornerPerformanceForSession(_selectedSessionId)
            .Where(p => p.CornerId == _selectedCornerId)
            .OrderBy(p => p.LapNumber)
            .ToList();

        if (cornerPerfs.Count == 0) return;

        // Add columns
        dgvPerformance.Columns.Add("Lap", "Lap");
        dgvPerformance.Columns.Add("BrakePoint", "Brake Point");
        dgvPerformance.Columns.Add("EntrySpd", "Entry Spd");
        dgvPerformance.Columns.Add("MinSpd", "Min Spd");
        dgvPerformance.Columns.Add("ExitSpd", "Exit Spd");
        dgvPerformance.Columns.Add("Delta", "Delta");
        dgvPerformance.Columns.Add("Mistake", "Mistake");

        // Add rows
        foreach (var perf in cornerPerfs)
        {
            int rowIdx = dgvPerformance.Rows.Add(
                perf.LapNumber,
                $"{perf.BrakeStartDistPct:F2}",
                $"{perf.EntrySpeed:F1}",
                $"{perf.MinSpeed:F1}",
                $"{perf.ExitSpeed:F1}",
                $"{perf.DeltaGainedLost:F3}",
                perf.MistakeCategory
            );

            // Color code row
            if (!string.IsNullOrEmpty(perf.MistakeCategory) && perf.MistakeCategory != "none")
            {
                dgvPerformance.Rows[rowIdx].DefaultCellStyle.BackColor =
                    Color.FromArgb(139, 0, 0); // Dark red for mistakes
            }
            else if (perf.DeltaGainedLost > 0)
            {
                dgvPerformance.Rows[rowIdx].DefaultCellStyle.BackColor =
                    Color.FromArgb(0, 100, 0); // Dark green for improved
            }
        }

        // Auto-resize columns
        dgvPerformance.AutoResizeColumns();
    }

    private void LoadSuggestedCallout()
    {
        lblSuggestion.Text = "";

        // Get latest lap's corner performance
        var lastPerf = _db.GetCornerPerformanceForSession(_selectedSessionId)
            .Where(p => p.CornerId == _selectedCornerId)
            .OrderByDescending(p => p.LapNumber)
            .FirstOrDefault();

        if (lastPerf != null && !string.IsNullOrEmpty(lastPerf.MistakeCategory) &&
            lastPerf.MistakeCategory != "none")
        {
            // Get best instruction for this mistake
            var instruction = _learner.GetBestInstruction(_selectedCornerId, lastPerf.MistakeCategory);
            if (instruction != null)
            {
                lblSuggestion.Text = instruction;
            }
        }
    }

    private void LoadVoiceHistory()
    {
        var calls = _db.GetVoiceCallHistory(_selectedSessionId)
            .Where(v => v.CornerId == _selectedCornerId)
            .OrderByDescending(v => v.TimestampMs)
            .Take(20)
            .ToList();

        var text = "";
        foreach (var call in calls)
        {
            text += $"[Lap {call.LapNumber} - {call.CallType}] {call.CallText}\n";
        }

        txtVoiceHistory.Text = text;
    }
}
