using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Adze.Host.Infrastructure;

namespace Adze.Host.UI;

[ComVisible(true)]
[Guid("F4068202-600A-4D6F-973B-DA2048A949CF")]
[ProgId(ProgIdValue)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class TaskPaneControl : UserControl
{
    public const string ProgIdValue = "Adze.Host.TaskPaneControl";
    private const string InitialAnswerText = "Ask about the active part, assembly, or drawing, then press 'Run assistant'. If the session is blocked or no document is open, Adze will explain the issue and the next recovery step.";
    private const string InitialPlanText = "The execution plan, blockers, and recovery guidance will appear here after a run.";
    private readonly TextBox _requestBox;
    private readonly TextBox _answerBox;
    private readonly TextBox _planBox;
    private readonly TextBox _statusBox;
    private readonly Button _runButton;
    private readonly Label _runStateLabel;
    private readonly Timer _refreshTimer;
    private bool _isRunning;

    public TaskPaneControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(244, 246, 248);

        var title = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(14, 8, 14, 0),
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 48, 76),
            Text = "Adze for SOLIDWORKS"
        };

        var subtitle = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(14, 0, 14, 8),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(86, 96, 108),
            Text = "Ground the current CAD session, inspect the plan, and recover cleanly from blocked launcher or no-document states."
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White
        };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        headerLayout.Controls.Add(title, 0, 0);
        headerLayout.Controls.Add(subtitle, 0, 1);

        var requestLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(14, 4, 14, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 41, 47),
            Text = "Ask Adze"
        };

        var requestBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 0, 12, 6),
            Multiline = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            BackColor = Color.White,
            Text = Host.Services.GroundingExecutionService.DefaultRequest
        };

        var runButton = new Button
        {
            Dock = DockStyle.Left,
            Width = 126,
            Height = 30,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.System,
            Text = "Run assistant"
        };
        runButton.Click += (_, _) => RunAssistant();

        var runStateLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(10, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
            ForeColor = Color.FromArgb(86, 96, 108),
            Text = "Ready. Open a document or ask a grounded question."
        };

        var runRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 0, 12, 8),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
        runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        runRow.Controls.Add(runButton, 0, 0);
        runRow.Controls.Add(runStateLabel, 1, 0);

        var answerLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 22,
            Padding = new Padding(12, 10, 12, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = "Assistant response"
        };

        var answerBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            ScrollBars = ScrollBars.Vertical,
            Text = InitialAnswerText
        };

        var answerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        answerPanel.Controls.Add(answerBox);
        answerPanel.Controls.Add(answerLabel);

        var planBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Consolas", 9F, FontStyle.Regular),
            ScrollBars = ScrollBars.Vertical,
            Text = InitialPlanText
        };

        var planTab = new TabPage("Plan")
        {
            BackColor = Color.White
        };
        planTab.Controls.Add(planBox);

        var body = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            Font = new Font("Consolas", 9F, FontStyle.Regular),
            ScrollBars = ScrollBars.Vertical
        };

        var statusTab = new TabPage("Status")
        {
            BackColor = Color.White
        };
        statusTab.Controls.Add(body);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Point(14, 6)
        };
        tabs.TabPages.Add(planTab);
        tabs.TabPages.Add(statusTab);

        var lowerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        lowerPanel.Controls.Add(tabs);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BorderStyle = BorderStyle.None,
            Panel1MinSize = 180,
            Panel2MinSize = 140,
            SplitterDistance = 234
        };
        split.Panel1.Padding = new Padding(12, 0, 12, 8);
        split.Panel2.Padding = new Padding(12, 0, 12, 12);
        split.Panel1.Controls.Add(answerPanel);
        split.Panel2.Controls.Add(lowerPanel);

        var refreshButton = new Button
        {
            Dock = DockStyle.Right,
            Width = 120,
            Height = 30,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.System,
            Text = "Refresh status"
        };
        refreshButton.Click += (_, _) => RefreshStatus();

        var footerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 0, 12, 10),
            ColumnCount = 2,
            RowCount = 1
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        footerLayout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 0);
        footerLayout.Controls.Add(refreshButton, 1, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.FromArgb(244, 246, 248)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        root.Controls.Add(headerLayout, 0, 0);
        root.Controls.Add(requestLabel, 0, 1);
        root.Controls.Add(requestBox, 0, 2);
        root.Controls.Add(runRow, 0, 3);
        root.Controls.Add(split, 0, 4);
        root.Controls.Add(footerLayout, 0, 5);

        _requestBox = requestBox;
        _answerBox = answerBox;
        _planBox = planBox;
        _statusBox = body;
        _runButton = runButton;
        _runStateLabel = runStateLabel;
        _refreshTimer = new Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshStatus();
        };

        Controls.Add(root);

        RefreshStatus();
        _refreshTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshStatus()
    {
        if (_isRunning)
        {
            return;
        }

        _statusBox.Text = HostState.BuildStatusText();
    }

    private void RunAssistant()
    {
        if (_isRunning)
        {
            return;
        }

        try
        {
            _isRunning = true;
            _refreshTimer.Stop();
            _runButton.Enabled = false;
            _requestBox.Enabled = false;
            _runStateLabel.Text = "Running grounded assistant against the current SOLIDWORKS session...";
            UseWaitCursor = true;
            Application.DoEvents();

            AssistantRunSnapshot snapshot = HostState.RunAssistant(_requestBox.Text);
            _answerBox.Text = snapshot.AnswerText;
            _planBox.Text = snapshot.PlanText;
            _runStateLabel.Text = BuildRunStateText(snapshot);
        }
        catch (Exception ex)
        {
            _answerBox.Text =
                "The assistant run failed before a grounded answer could be produced." +
                Environment.NewLine +
                Environment.NewLine +
                ex.Message;
            _runStateLabel.Text = "Run failed. Review the answer panel and host status for recovery guidance.";
        }
        finally
        {
            UseWaitCursor = false;
            _requestBox.Enabled = true;
            _runButton.Enabled = true;
            _isRunning = false;
            RefreshStatus();
            _refreshTimer.Start();
        }
    }

    private static string BuildRunStateText(AssistantRunSnapshot snapshot)
    {
        string source = snapshot.AnswerSource;
        if (!string.IsNullOrWhiteSpace(snapshot.AnswerModelId))
        {
            source += " (" + snapshot.AnswerModelId + ")";
        }

        return "Last run complete. Answer source: " + source + ".";
    }
}
