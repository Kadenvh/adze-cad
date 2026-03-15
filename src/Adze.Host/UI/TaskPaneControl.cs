using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
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
    private const string RequestPlaceholderText = "Ask about the active document...";
    private const string InitialAnswerText = "Open a document and ask a question about it. Adze will inspect the live CAD session and ground an answer.";
    private const string InitialAnswerFooterText = "Answer source and trace details appear here after a run.";
    private const string InitialPlanText = "The broker plan will appear here after a run.";
    private const string InitialStatusText = "Open the Status tab to inspect the live host dashboard.";
    private const string InitialToolsText = "Run the assistant to inspect the last grounded tool execution results.";
    private const int StatusRefreshIntervalMilliseconds = 5000;
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int EmLineScroll = 0x00B6;

    private readonly TextBox _requestBox;
    private readonly TextBox _answerBox;
    private readonly Label _answerFooterLabel;
    private readonly TextBox _planBox;
    private readonly TextBox _statusBox;
    private readonly TextBox _toolsBox;
    private readonly Button _runButton;
    private readonly Label _runStateLabel;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly SplitContainer _mainSplitContainer;
    private readonly TabControl _detailsTabs;
    private readonly TabPage _statusTab;
    private readonly CheckBox _autoRefreshStatusCheckBox;
    private readonly Label _statusRefreshStateLabel;
    private bool _isRunning;
    private bool _requestPlaceholderActive;
    private bool _statusRefreshScheduled;
    private bool _splitterInitialized;
    private bool _splitterAdjustedByUser;

    public TaskPaneControl()
    {
        SuspendLayout();

        try
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            UpdateStyles();

            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            BackColor = Color.FromArgb(244, 246, 248);

            var headerLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(14, 8, 14, 4),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 48, 76),
                Text = "ADZE"
            };

            var requestLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 0, 4),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 41, 47),
                Text = "Request"
            };

            var requestBox = new TextBox
            {
                Dock = DockStyle.Top,
                Multiline = true,
                Height = 78,
                Margin = new Padding(0),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                BackColor = Color.White,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                AcceptsTab = false,
                WordWrap = true
            };
            requestBox.Enter += (_, _) => RemoveRequestPlaceholder();
            requestBox.Leave += (_, _) => ApplyRequestPlaceholderIfNeeded();

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
                Margin = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(86, 96, 108),
                Text = "Ready."
            };

            var runRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                Margin = new Padding(0, 10, 0, 0),
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.White
            };
            runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
            runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            runRow.Controls.Add(runButton, 0, 0);
            runRow.Controls.Add(runStateLabel, 1, 0);

            var composerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(12, 0, 12, 10),
                Padding = new Padding(12, 12, 12, 12),
                BackColor = Color.White
            };
            composerPanel.Controls.Add(runRow);
            composerPanel.Controls.Add(requestBox);
            composerPanel.Controls.Add(requestLabel);

            var answerTitleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 0, 6),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 41, 47),
                Text = "Assistant Answer"
            };

            var answerBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Text = InitialAnswerText
            };

            var answerFooterLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                Margin = new Padding(0),
                Padding = new Padding(0, 6, 0, 0),
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(120, 128, 138),
                Text = InitialAnswerFooterText
            };

            var answerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(14, 12, 14, 10),
                BackColor = Color.White
            };
            answerPanel.Controls.Add(answerBox);
            answerPanel.Controls.Add(answerFooterLabel);
            answerPanel.Controls.Add(answerTitleLabel);

            var planBox = CreateReadOnlyTextBox(new Font("Consolas", 9F), InitialPlanText, Color.White);
            var planTab = new TabPage("Plan")
            {
                BackColor = Color.FromArgb(244, 246, 248)
            };
            planTab.Controls.Add(CreateTabContentPanel(planBox));

            var statusBox = CreateReadOnlyTextBox(new Font("Consolas", 9F), InitialStatusText, Color.White);

            var autoRefreshCheckBox = new CheckBox
            {
                Dock = DockStyle.Left,
                Width = 108,
                Margin = new Padding(0),
                Checked = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                Text = "Auto refresh"
            };
            autoRefreshCheckBox.CheckedChanged += (_, _) => UpdateStatusRefreshMode(forceRefresh: autoRefreshCheckBox.Checked);

            var statusRefreshStateLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(120, 128, 138),
                Text = "Auto refresh every 5s while the Status tab is active."
            };

            var manualRefreshButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 104,
                Height = 28,
                Margin = new Padding(0),
                FlatStyle = FlatStyle.System,
                Text = "Refresh now"
            };
            manualRefreshButton.Click += (_, _) => RefreshStatus(force: true);

            var statusToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                Margin = new Padding(0, 0, 0, 10),
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.White
            };
            statusToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            statusToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            statusToolbar.Controls.Add(autoRefreshCheckBox, 0, 0);
            statusToolbar.Controls.Add(statusRefreshStateLabel, 1, 0);
            statusToolbar.Controls.Add(manualRefreshButton, 2, 0);

            var statusTab = new TabPage("Status")
            {
                BackColor = Color.FromArgb(244, 246, 248)
            };
            statusTab.Controls.Add(CreateTabContentPanel(statusBox, statusToolbar));

            var toolsBox = CreateReadOnlyTextBox(new Font("Segoe UI", 9F), InitialToolsText, Color.White);
            var toolsTab = new TabPage("Tools")
            {
                BackColor = Color.FromArgb(244, 246, 248)
            };
            toolsTab.Controls.Add(CreateTabContentPanel(toolsBox));

            var detailsTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Point(12, 4)
            };
            detailsTabs.TabPages.Add(planTab);
            detailsTabs.TabPages.Add(statusTab);
            detailsTabs.TabPages.Add(toolsTab);
            detailsTabs.SelectedIndexChanged += (_, _) => UpdateStatusRefreshMode(forceRefresh: detailsTabs.SelectedTab == statusTab);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BorderStyle = BorderStyle.None,
                SplitterWidth = 6,
                Panel1MinSize = 220,
                Panel2MinSize = 180
            };
            split.Panel1.Padding = new Padding(12, 0, 12, 8);
            split.Panel2.Padding = new Padding(12, 0, 12, 12);
            split.Panel1.BackColor = BackColor;
            split.Panel2.BackColor = BackColor;
            split.Panel1.Controls.Add(answerPanel);
            split.Panel2.Controls.Add(detailsTabs);
            split.SplitterMoved += (_, _) =>
            {
                _splitterAdjustedByUser = true;
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 156F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(headerLabel, 0, 0);
            root.Controls.Add(composerPanel, 0, 1);
            root.Controls.Add(split, 0, 2);

            _requestBox = requestBox;
            _answerBox = answerBox;
            _answerFooterLabel = answerFooterLabel;
            _planBox = planBox;
            _statusBox = statusBox;
            _toolsBox = toolsBox;
            _runButton = runButton;
            _runStateLabel = runStateLabel;
            _refreshTimer = new System.Windows.Forms.Timer { Interval = StatusRefreshIntervalMilliseconds };
            _refreshTimer.Tick += (_, _) => RefreshStatus();
            _mainSplitContainer = split;
            _detailsTabs = detailsTabs;
            _statusTab = statusTab;
            _autoRefreshStatusCheckBox = autoRefreshCheckBox;
            _statusRefreshStateLabel = statusRefreshStateLabel;

            Controls.Add(root);
            ApplyRequestPlaceholder();
        }
        catch (Exception ex)
        {
            FileLogger.Error("Task Pane control initialization failed.", ex);
            throw;
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ScheduleDeferredStatusRefresh();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnHandleDestroyed(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyResponsiveSplitLayout();
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

    private static TextBox CreateReadOnlyTextBox(Font font, string text, Color backColor)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = backColor,
            Font = font,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Text = text
        };
    }

    private static Panel CreateTabContentPanel(Control content, Control? toolbar = null)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 12, 12, 12),
            BackColor = Color.White
        };

        panel.Controls.Add(content);
        if (toolbar != null)
        {
            panel.Controls.Add(toolbar);
        }

        return panel;
    }

    private void ApplyRequestPlaceholder()
    {
        _requestPlaceholderActive = true;
        _requestBox.ForeColor = Color.FromArgb(120, 128, 138);
        _requestBox.Text = RequestPlaceholderText;
    }

    private void RemoveRequestPlaceholder()
    {
        if (!_requestPlaceholderActive)
        {
            return;
        }

        _requestPlaceholderActive = false;
        _requestBox.Clear();
        _requestBox.ForeColor = Color.FromArgb(34, 41, 47);
    }

    private void ApplyRequestPlaceholderIfNeeded()
    {
        if (_requestPlaceholderActive || !string.IsNullOrWhiteSpace(_requestBox.Text))
        {
            return;
        }

        ApplyRequestPlaceholder();
    }

    private string GetRequestText()
    {
        return _requestPlaceholderActive ? string.Empty : _requestBox.Text;
    }

    private void RefreshStatus(bool force = false)
    {
        if (_isRunning)
        {
            return;
        }

        if (!force && !ShouldAutoRefreshStatus())
        {
            return;
        }

        try
        {
            ApplyResponsiveSplitLayout();
            ReplaceTextPreserveView(_statusBox, HostState.BuildStatusText(), preserveView: true);
            UpdateStatusRefreshIndicator();
        }
        catch (Exception ex)
        {
            FileLogger.Error("Task Pane status refresh failed.", ex);
            ReplaceTextPreserveView(
                _statusBox,
                "Status refresh failed." + Environment.NewLine + Environment.NewLine + ex.Message,
                preserveView: false);
            _statusRefreshStateLabel.Text = "Auto refresh paused after an error.";
            _refreshTimer.Stop();
        }
    }

    private void RunAssistant()
    {
        if (_isRunning)
        {
            return;
        }

        AssistantRunPreparation? preparation = null;

        try
        {
            _isRunning = true;
            _runButton.Enabled = false;
            _requestBox.Enabled = false;
            _runStateLabel.Text = "Running...";
            _refreshTimer.Stop();
            UpdateStatusRefreshIndicator();
            Update();

            preparation = HostState.PrepareAssistantRun(GetRequestText());
        }
        catch (Exception ex)
        {
            ShowRunFailure(ex);
            FinishAssistantRunUi();
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                AssistantRunSnapshot snapshot = HostState.CompleteAssistantRun(preparation);
                PostToUi(() => ApplyAssistantRunSnapshot(snapshot));
            }
            catch (Exception ex)
            {
                PostToUi(() => ShowRunFailure(ex));
            }
            finally
            {
                PostToUi(FinishAssistantRunUi);
            }
        });
    }

    private void ApplyAssistantRunSnapshot(AssistantRunSnapshot snapshot)
    {
        _answerBox.Text = string.IsNullOrWhiteSpace(snapshot.AnswerText) ? InitialAnswerText : snapshot.AnswerText;
        _answerFooterLabel.Text = string.IsNullOrWhiteSpace(snapshot.AnswerFooterText) ? InitialAnswerFooterText : snapshot.AnswerFooterText;
        ReplaceTextPreserveView(_planBox, snapshot.PlanText, preserveView: false);
        ReplaceTextPreserveView(_toolsBox, snapshot.ToolsText, preserveView: false);
        _runStateLabel.Text = BuildRunStateText(snapshot);
    }

    private void ShowRunFailure(Exception ex)
    {
        _answerBox.Text =
            "The assistant run failed before a grounded answer could be produced." +
            Environment.NewLine +
            Environment.NewLine +
            ex.Message;
        _answerFooterLabel.Text = "No trace captured for the failed run.";
        _runStateLabel.Text = "Run failed. Review the answer and status panels.";
    }

    private void FinishAssistantRunUi()
    {
        _requestBox.Enabled = true;
        _runButton.Enabled = true;
        _isRunning = false;
        ApplyRequestPlaceholderIfNeeded();
        RefreshStatus(force: _detailsTabs.SelectedTab == _statusTab);
        UpdateStatusRefreshMode(forceRefresh: false);
    }

    private void ScheduleDeferredStatusRefresh()
    {
        if (_statusRefreshScheduled || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        _statusRefreshScheduled = true;
        BeginInvoke((Action)(() =>
        {
            _statusRefreshScheduled = false;

            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            ApplyResponsiveSplitLayout(forceDefault: true);
            RefreshStatus(force: true);
            UpdateStatusRefreshMode(forceRefresh: false);
        }));
    }

    private void UpdateStatusRefreshMode(bool forceRefresh)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        if (forceRefresh && _detailsTabs.SelectedTab == _statusTab)
        {
            RefreshStatus(force: true);
        }

        if (ShouldAutoRefreshStatus())
        {
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }

        UpdateStatusRefreshIndicator();
    }

    private bool ShouldAutoRefreshStatus()
    {
        return !_isRunning &&
               _autoRefreshStatusCheckBox.Checked &&
               _detailsTabs.SelectedTab == _statusTab;
    }

    private void UpdateStatusRefreshIndicator()
    {
        if (_isRunning)
        {
            _statusRefreshStateLabel.Text = "Status refresh paused while the assistant is running.";
            return;
        }

        if (!_autoRefreshStatusCheckBox.Checked)
        {
            _statusRefreshStateLabel.Text = "Auto refresh paused.";
            return;
        }

        if (_detailsTabs.SelectedTab == _statusTab)
        {
            _statusRefreshStateLabel.Text = "Auto refresh every 5s while visible.";
            return;
        }

        _statusRefreshStateLabel.Text = "Auto refresh resumes when the Status tab is active.";
    }

    private void ApplyResponsiveSplitLayout(bool forceDefault = false)
    {
        if (_mainSplitContainer.IsDisposed)
        {
            return;
        }

        int availableHeight = _mainSplitContainer.ClientSize.Height;
        if (availableHeight <= 0)
        {
            return;
        }

        int maxTopHeight = availableHeight - _mainSplitContainer.Panel2MinSize;
        if (maxTopHeight <= 0)
        {
            return;
        }

        int nextSplitDistance;
        if (!_splitterInitialized || (forceDefault && !_splitterAdjustedByUser))
        {
            nextSplitDistance = (int)Math.Round(availableHeight * 0.68);
        }
        else if (_splitterAdjustedByUser)
        {
            nextSplitDistance = _mainSplitContainer.SplitterDistance;
        }
        else
        {
            nextSplitDistance = (int)Math.Round(availableHeight * 0.68);
        }

        nextSplitDistance = Math.Max(_mainSplitContainer.Panel1MinSize, Math.Min(maxTopHeight, nextSplitDistance));
        if (nextSplitDistance <= 0)
        {
            return;
        }

        if (_mainSplitContainer.SplitterDistance != nextSplitDistance)
        {
            _mainSplitContainer.SplitterDistance = nextSplitDistance;
        }

        _splitterInitialized = true;
    }

    private void ReplaceTextPreserveView(TextBox target, string text, bool preserveView)
    {
        if (string.Equals(target.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        int selectionStart = target.SelectionStart;
        int selectionLength = target.SelectionLength;
        int firstVisibleLine = preserveView && target.IsHandleCreated
            ? SendMessage(target.Handle, EmGetFirstVisibleLine, 0, 0)
            : 0;

        target.Text = text;

        if (!preserveView || !target.IsHandleCreated)
        {
            return;
        }

        int clampedSelectionStart = Math.Min(selectionStart, target.TextLength);
        int clampedSelectionLength = Math.Min(selectionLength, Math.Max(0, target.TextLength - clampedSelectionStart));
        target.SelectionStart = clampedSelectionStart;
        target.SelectionLength = clampedSelectionLength;

        int currentFirstVisibleLine = SendMessage(target.Handle, EmGetFirstVisibleLine, 0, 0);
        int lineDelta = firstVisibleLine - currentFirstVisibleLine;
        if (lineDelta != 0)
        {
            SendMessage(target.Handle, EmLineScroll, 0, lineDelta);
        }
    }

    private void PostToUi(Action action)
    {
        if (action == null || IsDisposed)
        {
            return;
        }

        try
        {
            if (!IsHandleCreated)
            {
                return;
            }

            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string BuildRunStateText(AssistantRunSnapshot snapshot)
    {
        string source = snapshot.AnswerSource;
        if (!string.IsNullOrWhiteSpace(snapshot.AnswerModelId))
        {
            source += " (" + snapshot.AnswerModelId + ")";
        }

        return "Last run: " + source + ".";
    }
}
