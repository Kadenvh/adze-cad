using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Adze.Manager;

/// <summary>
/// Logs tab. Hosts three observability surfaces:
///   * Manager script log (output of install/uninstall/eject scripts) — top of left column
///   * Live host.log tail with FileSystemWatcher follow-mode and prefix coloring
///   * Chat JSONL session browser at the bottom (list of sessions + pretty body view)
/// </summary>
public sealed class LogsTab : UserControl
{
    private const int MaxTailLines = 1000;
    private const string AdzeRoot = "Adze";
    private const string LogsSubdir = "logs";
    private const string ChatSubdir = "chat";
    private const string HostLogFileName = "host.log";

    private readonly RichTextBox _scriptLog;
    private readonly RichTextBox _hostLogView;
    private readonly Button _btnClear;
    private readonly Button _btnOpenFolder;
    private readonly Button _btnExport;
    private readonly Button _btnPauseResume;
    private readonly Label _tailStatus;

    private readonly ListBox _sessionList;
    private readonly RichTextBox _sessionView;
    private readonly Button _btnRefreshSessions;

    private FileSystemWatcher? _hostLogWatcher;
    private long _lastReadOffset;
    private bool _tailPaused;
    private string? _hostLogPath;

    public LogsTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 248, 250);

        // Top-level: SplitContainer dividing the tab vertically — top half (logs)
        // gets ~62%, bottom half (chat browser) gets ~38%.
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.None,
            BackColor = Color.FromArgb(247, 248, 250)
        };
        Controls.Add(split);

        // ----- Top: log surfaces -----
        var topRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.FromArgb(247, 248, 250),
            Padding = new Padding(8, 6, 8, 6)
        };
        topRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // section label
        topRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 30F));   // script log
        topRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // button row + tail status
        topRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 70F));   // host.log tail
        split.Panel1.Controls.Add(topRoot);

        var scriptLabel = new Label
        {
            Text = "Manager script log",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(50, 60, 80),
            TextAlign = ContentAlignment.MiddleLeft
        };
        topRoot.Controls.Add(scriptLabel, 0, 0);

        _scriptLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5F),
            BackColor = Color.FromArgb(24, 24, 28),
            ForeColor = Color.FromArgb(224, 232, 240),
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            DetectUrls = false,
            WordWrap = true
        };
        topRoot.Controls.Add(_scriptLog, 0, 1);

        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(0, 4, 0, 4)
        };
        _btnClear = MakeButton("Clear View");
        _btnOpenFolder = MakeButton("Open in Explorer");
        _btnExport = MakeButton("Export...");
        _btnPauseResume = MakeButton("Pause Tail");

        // Lambdas capture the field by reference, but the nullable flow analysis
        // sees _hostLogView as potentially null at this lexical point. It is in fact
        // initialized later in this same constructor, before any handler can fire.
        _btnClear.Click += (s, e) => { _hostLogView!.Clear(); };
        _btnOpenFolder.Click += (s, e) => OpenLogsFolder();
        _btnExport.Click += (s, e) => ExportHostLog();
        _btnPauseResume.Click += (s, e) => TogglePause();

        _tailStatus = new Label
        {
            Text = "Tail: starting...",
            AutoSize = true,
            Margin = new Padding(8, 8, 0, 0),
            ForeColor = Color.FromArgb(80, 90, 110),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic)
        };

        btnRow.Controls.Add(_btnClear);
        btnRow.Controls.Add(_btnOpenFolder);
        btnRow.Controls.Add(_btnExport);
        btnRow.Controls.Add(_btnPauseResume);
        btnRow.Controls.Add(_tailStatus);
        topRoot.Controls.Add(btnRow, 0, 2);

        _hostLogView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5F),
            BackColor = Color.FromArgb(18, 18, 22),
            ForeColor = Color.FromArgb(220, 228, 236),
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            DetectUrls = false,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        topRoot.Controls.Add(_hostLogView, 0, 3);

        // ----- Bottom: chat session browser -----
        var bottomRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.FromArgb(247, 248, 250),
            Padding = new Padding(8, 6, 8, 8)
        };
        bottomRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        bottomRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        bottomRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        bottomRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        split.Panel2.Controls.Add(bottomRoot);

        var sessionsHeader = new Label
        {
            Text = "Chat sessions (%LOCALAPPDATA%\\Adze\\chat)",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(50, 60, 80),
            TextAlign = ContentAlignment.MiddleLeft
        };
        bottomRoot.Controls.Add(sessionsHeader, 0, 0);

        _btnRefreshSessions = MakeButton("Refresh Sessions");
        _btnRefreshSessions.Click += (s, e) => ReloadSessions(preserveSelection: true);
        _btnRefreshSessions.Margin = new Padding(0, 0, 0, 4);
        var refreshHolder = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        refreshHolder.Controls.Add(_btnRefreshSessions);
        bottomRoot.Controls.Add(refreshHolder, 1, 0);

        _sessionList = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5F),
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false
        };
        _sessionList.SelectedIndexChanged += (s, e) => RenderSelectedSession();
        bottomRoot.Controls.Add(_sessionList, 0, 1);

        _sessionView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5F),
            BackColor = Color.FromArgb(248, 248, 252),
            ForeColor = Color.FromArgb(30, 30, 30),
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            DetectUrls = false,
            WordWrap = true
        };
        bottomRoot.Controls.Add(_sessionView, 1, 1);

        Load += (s, e) =>
        {
            // Defer to Load so SplitterDistance is anchored on a real ClientSize.
            try { split.SplitterDistance = (int)(ClientSize.Height * 0.62); } catch { /* layout race — fall back to default */ }
            StartHostLogTail();
            ReloadSessions(preserveSelection: false);
        };
        HandleDestroyed += (s, e) => DisposeWatcher();
    }

    // -------------------------------------------------------------------
    // Public API used by MainForm
    // -------------------------------------------------------------------

    /// <summary>Append a line from the script runner (Install/Uninstall/Eject).</summary>
    public void AppendScriptLine(string message)
    {
        if (_scriptLog.InvokeRequired)
        {
            BeginInvoke((Action<string>)AppendScriptLine, message);
            return;
        }
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _scriptLog.AppendText("[" + timestamp + "] " + message + Environment.NewLine);
        _scriptLog.SelectionStart = _scriptLog.TextLength;
        _scriptLog.ScrollToCaret();
    }

    // -------------------------------------------------------------------
    // host.log tail
    // -------------------------------------------------------------------

    private void StartHostLogTail()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logsDir = Path.Combine(root, AdzeRoot, LogsSubdir);
        _hostLogPath = Path.Combine(logsDir, HostLogFileName);

        try
        {
            if (!Directory.Exists(logsDir))
            {
                _tailStatus.Text = "Tail: logs folder does not exist yet (" + logsDir + ")";
                return;
            }
            if (!File.Exists(_hostLogPath))
            {
                _tailStatus.Text = "Tail: host.log not found (waiting for Adze.Host to write it)";
            }
            else
            {
                LoadInitialTail();
            }

            _hostLogWatcher = new FileSystemWatcher(logsDir, HostLogFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _hostLogWatcher.Changed += OnHostLogChanged;
            _hostLogWatcher.Created += OnHostLogChanged;
            _hostLogWatcher.Renamed += (s, e) => OnHostLogChanged(s, e);
        }
        catch (Exception ex)
        {
            _tailStatus.Text = "Tail: failed to start (" + ex.Message + ")";
        }
    }

    private void LoadInitialTail()
    {
        if (string.IsNullOrEmpty(_hostLogPath) || !File.Exists(_hostLogPath)) return;

        try
        {
            using var fs = new FileStream(_hostLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            // Cheap last-N-lines: read all, take tail. For a 12 MB log this still
            // reads under 100 ms; if log files grow much larger we'll switch to
            // a reverse-byte-block scan, but that's overkill today.
            var allLines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                allLines.Add(line);
            }
            _lastReadOffset = fs.Position;

            int start = Math.Max(0, allLines.Count - MaxTailLines);
            _hostLogView.SuspendLayout();
            _hostLogView.Clear();
            for (int i = start; i < allLines.Count; i++)
            {
                AppendColored(allLines[i] + Environment.NewLine);
            }
            _hostLogView.ResumeLayout();
            _hostLogView.SelectionStart = _hostLogView.TextLength;
            _hostLogView.ScrollToCaret();
            _tailStatus.Text = "Tail: live (" + (allLines.Count - start) + " of " + allLines.Count + " lines)";
        }
        catch (Exception ex)
        {
            _tailStatus.Text = "Tail: read failed (" + ex.Message + ")";
        }
    }

    private void OnHostLogChanged(object sender, FileSystemEventArgs e)
    {
        if (_tailPaused) return;
        // FileSystemWatcher events fire on a non-UI thread.
        try
        {
            BeginInvoke((Action)AppendNewBytes);
        }
        catch (InvalidOperationException)
        {
            // Form is shutting down — ignore.
        }
    }

    private void AppendNewBytes()
    {
        if (string.IsNullOrEmpty(_hostLogPath) || !File.Exists(_hostLogPath)) return;
        try
        {
            using var fs = new FileStream(_hostLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Truncation / rotation guard — if file shrank, replay tail.
            if (fs.Length < _lastReadOffset)
            {
                _lastReadOffset = 0;
                _hostLogView.Clear();
            }
            fs.Seek(_lastReadOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            int appended = 0;
            while ((line = reader.ReadLine()) != null)
            {
                AppendColored(line + Environment.NewLine);
                appended++;
            }
            _lastReadOffset = fs.Position;

            if (appended > 0)
            {
                TrimToMaxLines();
                _hostLogView.SelectionStart = _hostLogView.TextLength;
                _hostLogView.ScrollToCaret();
            }
        }
        catch
        {
            // Transient lock during write — next change event will retry.
        }
    }

    private void TrimToMaxLines()
    {
        // Keep the in-memory view bounded. RichTextBox doesn't expose a direct
        // line-count API that's cheap on huge buffers, so we count newlines.
        int newlineCount = 0;
        string text = _hostLogView.Text;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') newlineCount++;
        }
        if (newlineCount <= MaxTailLines) return;

        int linesToDrop = newlineCount - MaxTailLines;
        int dropAt = 0;
        int seen = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                seen++;
                if (seen == linesToDrop) { dropAt = i + 1; break; }
            }
        }
        if (dropAt > 0)
        {
            _hostLogView.Select(0, dropAt);
            _hostLogView.SelectedText = string.Empty;
        }
    }

    private void AppendColored(string line)
    {
        Color color = ResolveLineColor(line);
        int start = _hostLogView.TextLength;
        _hostLogView.AppendText(line);
        _hostLogView.Select(start, line.Length);
        _hostLogView.SelectionColor = color;
        _hostLogView.SelectionLength = 0;
        _hostLogView.SelectionColor = _hostLogView.ForeColor;
    }

    private static Color ResolveLineColor(string line)
    {
        // Severity colors override category colors.
        if (line.Contains(" ERROR ") || line.StartsWith("ERROR ") || line.StartsWith("[err]"))
            return Color.FromArgb(255, 110, 100);
        if (line.Contains(" WARN ") || line.StartsWith("WARN "))
            return Color.FromArgb(255, 200, 90);

        // Category prefixes (BrokerDiagnostics) — scan the part after the level token.
        if (Contains(line, "Settings: ")) return Color.FromArgb(120, 200, 255);
        if (Contains(line, "Policy: ")) return Color.FromArgb(180, 170, 255);
        if (Contains(line, "Budget: ")) return Color.FromArgb(255, 220, 130);
        if (Contains(line, "RateLimit: ")) return Color.FromArgb(255, 170, 90);
        if (Contains(line, "HealthCheck: ")) return Color.FromArgb(140, 230, 170);
        if (Contains(line, "ToolProbe: ")) return Color.FromArgb(170, 220, 255);
        if (Contains(line, "Streaming: ")) return Color.FromArgb(180, 240, 220);
        if (Contains(line, "Clarification: ")) return Color.FromArgb(220, 200, 255);

        return Color.FromArgb(220, 228, 236);
    }

    private static bool Contains(string line, string needle)
    {
        return line.IndexOf(needle, StringComparison.Ordinal) >= 0;
    }

    private void TogglePause()
    {
        _tailPaused = !_tailPaused;
        _btnPauseResume.Text = _tailPaused ? "Resume Tail" : "Pause Tail";
        _tailStatus.Text = _tailPaused ? "Tail: paused" : "Tail: live";
        if (!_tailPaused)
        {
            // On resume, re-sync any bytes that arrived while paused.
            AppendNewBytes();
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logsDir = Path.Combine(root, AdzeRoot, LogsSubdir);
            if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
            System.Diagnostics.Process.Start("explorer.exe", "\"" + logsDir + "\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open logs folder: " + ex.Message, "Adze Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportHostLog()
    {
        if (string.IsNullOrEmpty(_hostLogPath) || !File.Exists(_hostLogPath))
        {
            MessageBox.Show(this, "host.log not found yet — nothing to export.", "Adze Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export host.log",
            Filter = "Log files (*.log)|*.log|All files (*.*)|*.*",
            FileName = "adze-host-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            // Open with FileShare.ReadWrite so we can copy even while Adze.Host
            // is actively appending. File.Copy throws if another writer holds the file.
            using var src = new FileStream(_hostLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write);
            src.CopyTo(dst);
            MessageBox.Show(this, "Exported to:\n" + dlg.FileName, "Adze Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed: " + ex.Message, "Adze Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DisposeWatcher()
    {
        try
        {
            if (_hostLogWatcher != null)
            {
                _hostLogWatcher.EnableRaisingEvents = false;
                _hostLogWatcher.Dispose();
                _hostLogWatcher = null;
            }
        }
        catch { /* shutdown best-effort */ }
    }

    // -------------------------------------------------------------------
    // Chat JSONL browser
    // -------------------------------------------------------------------

    private string ChatDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AdzeRoot, ChatSubdir);

    private void ReloadSessions(bool preserveSelection)
    {
        string? prior = preserveSelection ? _sessionList.SelectedItem as string : null;
        _sessionList.BeginUpdate();
        try
        {
            _sessionList.Items.Clear();
            if (!Directory.Exists(ChatDir)) return;

            var files = new DirectoryInfo(ChatDir)
                .GetFiles("session-*.jsonl")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            foreach (var f in files)
            {
                _sessionList.Items.Add(f.Name);
            }

            if (prior != null && _sessionList.Items.Contains(prior))
            {
                _sessionList.SelectedItem = prior;
            }
            else if (_sessionList.Items.Count > 0)
            {
                _sessionList.SelectedIndex = 0;
            }
        }
        finally
        {
            _sessionList.EndUpdate();
        }
    }

    private void RenderSelectedSession()
    {
        _sessionView.Clear();
        if (_sessionList.SelectedItem is not string fileName) return;
        string fullPath = Path.Combine(ChatDir, fileName);
        if (!File.Exists(fullPath))
        {
            _sessionView.Text = "(file no longer exists)";
            return;
        }

        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var sb = new StringBuilder();
        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            int entryIndex = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                entryIndex++;
                AppendEntry(sb, line, entryIndex, serializer);
            }
            if (entryIndex == 0)
            {
                sb.Append("(empty session)");
            }
            _sessionView.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            _sessionView.Text = "Failed to read session: " + ex.Message;
        }
    }

    private static void AppendEntry(StringBuilder sb, string line, int index, JavaScriptSerializer serializer)
    {
        try
        {
            var obj = serializer.Deserialize<Dictionary<string, object>>(line);
            string ts = obj != null && obj.TryGetValue("timestamp_utc", out var t) ? t?.ToString() ?? "" : "";
            string user = obj != null && obj.TryGetValue("user", out var u) ? u?.ToString() ?? "" : "";
            string asst = obj != null && obj.TryGetValue("assistant", out var a) ? a?.ToString() ?? "" : "";
            string source = obj != null && obj.TryGetValue("source", out var s) ? s?.ToString() ?? "" : "";

            sb.Append("=== Entry ").Append(index);
            if (!string.IsNullOrEmpty(ts)) sb.Append(" · ").Append(ts);
            if (!string.IsNullOrEmpty(source)) sb.Append(" · ").Append(source);
            sb.AppendLine(" ===");
            if (!string.IsNullOrEmpty(user))
            {
                sb.Append("USER: ").AppendLine(Truncate(user, 800));
            }
            if (!string.IsNullOrEmpty(asst))
            {
                sb.Append("ASSISTANT: ").AppendLine(Truncate(asst, 1500));
            }
            sb.AppendLine();
        }
        catch
        {
            // Tolerate malformed lines — show raw, truncated.
            sb.Append("=== Entry ").Append(index).AppendLine(" (unparsed) ===");
            sb.AppendLine(Truncate(line, 800));
            sb.AppendLine();
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s.Substring(0, max) + "... (" + (s.Length - max) + " more chars)";
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(6, 2, 6, 2),
            Margin = new Padding(0, 0, 6, 0),
            FlatStyle = FlatStyle.System,
            Font = new Font("Segoe UI", 9F)
        };
    }
}
