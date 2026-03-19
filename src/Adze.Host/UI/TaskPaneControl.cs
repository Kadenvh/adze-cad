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
    private const int StatusRefreshIntervalMilliseconds = 5000;

    private readonly TextBox _requestBox;
    private readonly WebBrowser _contentBrowser;
    private readonly Button _runButton;
    private readonly Label _runStateLabel;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Panel _clarificationPanel;
    private readonly LinkLabel _clarificationToggle;
    private readonly ComboBox _intentComboBox;
    private readonly CheckedListBox _scopeListBox;
    private readonly ComboBox _outputModeComboBox;
    private readonly CheckBox _diagnosticsCheckBox;
    private bool _isRunning;
    private bool _requestPlaceholderActive;
    private bool _clarificationExpanded;
    private bool _statusRefreshScheduled;
    private string? _lastErrorMessage;

    // Current content for HTML tabs
    private string _planText = "";
    private string _statusText = "";
    private string _toolsText = "";
    private string _activeTab = "answer";

    public TaskPaneControl()
    {
        SuspendLayout();

        try
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            BackColor = Color.FromArgb(244, 246, 248);

            // --- Header ---
            var headerLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Margin = new Padding(0),
                Padding = new Padding(14, 6, 14, 2),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 48, 76),
                BackColor = BackColor,
                Text = "ADZE"
            };

            // --- Request box ---
            var requestBox = new TextBox
            {
                Dock = DockStyle.Top,
                Multiline = true,
                Height = 56,
                Margin = new Padding(0),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                BackColor = Color.White,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                WordWrap = true
            };
            requestBox.Enter += (_, _) => RemoveRequestPlaceholder();
            requestBox.Leave += (_, _) => ApplyRequestPlaceholderIfNeeded();

            // --- Run button + state ---
            var runButton = new Button
            {
                Dock = DockStyle.Left,
                Width = 110,
                Height = 26,
                Margin = new Padding(0),
                FlatStyle = FlatStyle.System,
                Text = "Run assistant"
            };

            var runStateLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(86, 96, 108),
                Text = "Ready."
            };

            runButton.Click += (_, _) =>
            {
                if (_isRunning)
                {
                    HostState.CancelRun();
                    runStateLabel.Text = "Cancelling...";
                    runButton.Enabled = false;
                }
                else
                {
                    RunAssistant();
                }
            };

            var runRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                Margin = new Padding(0),
                Padding = new Padding(0, 2, 0, 2)
            };
            runRow.Controls.Add(runStateLabel);
            runRow.Controls.Add(runButton);

            // --- Clarification panel ---
            var clarificationToggle = new LinkLabel
            {
                Dock = DockStyle.Top,
                Height = 18,
                Margin = new Padding(0, 4, 0, 0),
                Font = new Font("Segoe UI", 7.5F),
                LinkColor = Color.FromArgb(86, 96, 108),
                ActiveLinkColor = Color.FromArgb(34, 41, 47),
                Text = "Show options"
            };
            clarificationToggle.LinkClicked += (_, _) => ToggleClarification();

            var intentComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                Height = 22,
                Margin = new Padding(0, 2, 0, 4),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8F)
            };
            intentComboBox.Items.AddRange(new object[] { "Inspect", "Diagnose", "Explain", "Compare" });
            intentComboBox.SelectedIndex = 0;

            var scopeListBox = new CheckedListBox
            {
                Dock = DockStyle.Top,
                Height = 64,
                Margin = new Padding(0, 2, 0, 4),
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                Font = new Font("Segoe UI", 8F),
                IntegralHeight = false
            };

            var outputModeComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                Height = 22,
                Margin = new Padding(0, 2, 0, 4),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8F)
            };
            outputModeComboBox.Items.AddRange(new object[] { "Brief", "Detailed", "Tabular" });
            outputModeComboBox.SelectedIndex = 0;

            var diagnosticsCheckBox = new CheckBox
            {
                Dock = DockStyle.Top,
                Height = 18,
                Margin = new Padding(0),
                Font = new Font("Segoe UI", 8F),
                Text = "Include diagnostics"
            };

            var clarificationInner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 160,
                Visible = false
            };
            clarificationInner.Controls.Add(diagnosticsCheckBox);
            clarificationInner.Controls.Add(outputModeComboBox);
            clarificationInner.Controls.Add(scopeListBox);
            clarificationInner.Controls.Add(intentComboBox);

            var clarificationOuter = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true
            };
            clarificationOuter.Controls.Add(clarificationInner);
            clarificationOuter.Controls.Add(clarificationToggle);

            // --- Composer panel (top area) ---
            var composer = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10, 4, 10, 6),
                BackColor = Color.White
            };
            composer.Controls.Add(runRow);
            composer.Controls.Add(clarificationOuter);
            composer.Controls.Add(requestBox);

            // --- Content browser (fills remaining space) ---
            var contentBrowser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                AllowWebBrowserDrop = false,
                IsWebBrowserContextMenuEnabled = true,
                ScriptErrorsSuppressed = true,
                WebBrowserShortcutsEnabled = true,
                ObjectForScripting = this
            };
            contentBrowser.Navigate("about:blank");

            // --- Assemble ---
            Controls.Add(contentBrowser);
            Controls.Add(composer);
            Controls.Add(headerLabel);

            _requestBox = requestBox;
            _contentBrowser = contentBrowser;
            _runButton = runButton;
            _runStateLabel = runStateLabel;
            _refreshTimer = new System.Windows.Forms.Timer { Interval = StatusRefreshIntervalMilliseconds };
            _refreshTimer.Tick += (_, _) => RefreshStatus();
            _clarificationPanel = clarificationInner;
            _clarificationToggle = clarificationToggle;
            _intentComboBox = intentComboBox;
            _scopeListBox = scopeListBox;
            _outputModeComboBox = outputModeComboBox;
            _diagnosticsCheckBox = diagnosticsCheckBox;

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RenderContent();
        ScheduleDeferredStatusRefresh();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnHandleDestroyed(e);
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

    // -----------------------------------------------------------------------
    // Request / Placeholder
    // -----------------------------------------------------------------------

    private void ApplyRequestPlaceholder()
    {
        _requestPlaceholderActive = true;
        _requestBox.ForeColor = Color.FromArgb(120, 128, 138);
        _requestBox.Text = RequestPlaceholderText;
    }

    private void RemoveRequestPlaceholder()
    {
        if (!_requestPlaceholderActive) return;
        _requestPlaceholderActive = false;
        _requestBox.Clear();
        _requestBox.ForeColor = Color.FromArgb(34, 41, 47);
    }

    private void ApplyRequestPlaceholderIfNeeded()
    {
        if (_requestPlaceholderActive || !string.IsNullOrWhiteSpace(_requestBox.Text)) return;
        ApplyRequestPlaceholder();
    }

    private string GetRequestText()
    {
        string rawText = _requestPlaceholderActive ? string.Empty : _requestBox.Text;
        string prefix = BuildClarificationPrefix();
        return string.IsNullOrWhiteSpace(prefix) ? rawText : prefix + Environment.NewLine + rawText;
    }

    // -----------------------------------------------------------------------
    // Clarification
    // -----------------------------------------------------------------------

    private void ToggleClarification()
    {
        _clarificationExpanded = !_clarificationExpanded;
        _clarificationPanel.Visible = _clarificationExpanded;
        _clarificationToggle.Text = _clarificationExpanded ? "Hide options" : "Show options";
        if (_clarificationExpanded) PopulateScopeList();
    }

    private void PopulateScopeList()
    {
        try
        {
            _scopeListBox.Items.Clear();
            var context = HostState.PrepareAssistantRun(null).Context;

            if (context.Selection.Count > 0)
            {
                string preview = string.Join(", ", context.Selection.Items.ConvertAll(i => i.Kind + ":" + i.Name));
                _scopeListBox.Items.Add("Selection: " + preview, true);
            }

            if (context.Document != null)
            {
                foreach (var f in context.FeatureTree.Features)
                {
                    if (_scopeListBox.Items.Count >= 15) break;
                    if (f.Kind == "OriginProfileFeature" || f.Kind == "RefPlane") continue;
                    _scopeListBox.Items.Add("Feature: " + f.Name);
                }
                foreach (var d in context.Dimensions.Items)
                {
                    if (_scopeListBox.Items.Count >= 15) break;
                    _scopeListBox.Items.Add("Dim: " + d.FullName + " = " + d.Value);
                }
            }

            bool hasIssues = context.Diagnostics.Warnings.Count > 0 ||
                             context.Diagnostics.MissingReferences.Count > 0;
            _diagnosticsCheckBox.Checked = hasIssues;
        }
        catch (Exception ex)
        {
            FileLogger.Error("Clarification scope population failed.", ex);
        }
    }

    private string BuildClarificationPrefix()
    {
        if (!_clarificationExpanded) return string.Empty;

        var parts = new System.Collections.Generic.List<string>();
        string intent = _intentComboBox.SelectedItem?.ToString() ?? "Inspect";
        if (!string.Equals(intent, "Inspect", StringComparison.OrdinalIgnoreCase))
            parts.Add("intent=" + intent.ToLowerInvariant());

        var checkedItems = new System.Collections.Generic.List<string>();
        for (int i = 0; i < _scopeListBox.CheckedItems.Count; i++)
            checkedItems.Add(_scopeListBox.CheckedItems[i]?.ToString() ?? "");
        if (checkedItems.Count > 0)
            parts.Add("scope=" + string.Join("; ", checkedItems));

        string output = _outputModeComboBox.SelectedItem?.ToString() ?? "Brief";
        if (!string.Equals(output, "Brief", StringComparison.OrdinalIgnoreCase))
            parts.Add("output=" + output.ToLowerInvariant());

        if (_diagnosticsCheckBox.Checked) parts.Add("diagnostics=yes");
        if (parts.Count == 0) return string.Empty;
        return "[clarification] " + string.Join(", ", parts) + " [/clarification]";
    }

    // -----------------------------------------------------------------------
    // Run assistant
    // -----------------------------------------------------------------------

    private void RunAssistant()
    {
        if (_isRunning) return;

        AssistantRunPreparation? preparation = null;
        try
        {
            _isRunning = true;
            _runButton.Text = "Cancel";
            _runButton.Enabled = true;
            _requestBox.Enabled = false;
            _runStateLabel.Text = "Running...";
            _refreshTimer.Stop();
            Update();

            HostState.BeginRun();
            preparation = HostState.PrepareAssistantRun(GetRequestText());
        }
        catch (Exception ex)
        {
            ShowRunFailure(ex);
            FinishRun();
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                AssistantRunSnapshot snapshot = HostState.CompleteAssistantRun(preparation);
                PostToUi(() => ApplySnapshot(snapshot));
            }
            catch (Exception ex)
            {
                PostToUi(() => ShowRunFailure(ex));
            }
            finally
            {
                HostState.EndRun();
                PostToUi(FinishRun);
            }
        });
    }

    private void ApplySnapshot(AssistantRunSnapshot snapshot)
    {
        _lastErrorMessage = null;
        _planText = snapshot.PlanText ?? "";
        _toolsText = snapshot.ToolsText ?? "";
        _runStateLabel.Text = BuildRunStateText(snapshot);
        RenderContent();
    }

    private void ShowRunFailure(Exception ex)
    {
        _runStateLabel.Text = "Run failed.";
        _lastErrorMessage = "The assistant run failed.\n\n" + ex.Message;
        RenderContent();
    }

    private void FinishRun()
    {
        _requestBox.Enabled = true;
        _requestBox.Clear();
        _runButton.Enabled = true;
        _runButton.Text = "Run assistant";
        _isRunning = false;
        ApplyRequestPlaceholder();
        ScheduleDeferredStatusRefresh();
    }

    private static string BuildRunStateText(AssistantRunSnapshot snapshot)
    {
        string source = snapshot.AnswerSource ?? "";
        var usage = snapshot.RunUsage;
        if (usage != null && usage.TotalTokens > 0)
            return source + " | " + usage.TotalTokens + " tokens";
        return string.IsNullOrWhiteSpace(source) ? "Done." : source;
    }

    // -----------------------------------------------------------------------
    // Status refresh
    // -----------------------------------------------------------------------

    private void RefreshStatus(bool force = false)
    {
        if (_isRunning) return;
        try
        {
            _statusText = HostState.BuildStatusText();
            if (_activeTab == "status")
            {
                string statusHtml = string.IsNullOrWhiteSpace(_statusText)
                    ? "<p class=\"muted\">Status refreshes automatically.</p>"
                    : "<pre>" + HtmlEncode(_statusText) + "</pre>";
                _contentBrowser.Document?.InvokeScript("updateTabContent",
                    new object[] { "tc-status", statusHtml });
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("Status refresh failed.", ex);
            _refreshTimer.Stop();
        }
    }

    private void ScheduleDeferredStatusRefresh()
    {
        if (_statusRefreshScheduled || !IsHandleCreated || IsDisposed) return;
        _statusRefreshScheduled = true;
        BeginInvoke((Action)(() =>
        {
            _statusRefreshScheduled = false;
            if (!IsHandleCreated || IsDisposed) return;
            RefreshStatus(force: true);
            _refreshTimer.Start();
        }));
    }

    // -----------------------------------------------------------------------
    // HTML rendering — single WebBrowser for answer + tabs
    // -----------------------------------------------------------------------

    private void RenderContent()
    {
        try
        {
            string html = BuildFullPageHtml();
            if (_contentBrowser.Document == null)
                _contentBrowser.Navigate("about:blank");
            _contentBrowser.DocumentText = html;
        }
        catch (Exception ex)
        {
            FileLogger.Error("Failed to render content HTML.", ex);
        }
    }

    private string BuildConversationHtml()
    {
        var history = HostState.GetChatHistory();
        if (history.Count == 0 && string.IsNullOrEmpty(_lastErrorMessage))
        {
            return "<p class=\"muted\">Open a document and ask a question. Adze will inspect the live session and ground an answer.</p>";
        }

        var sb = new System.Text.StringBuilder();

        foreach (var entry in history)
        {
            // User message
            if (!string.IsNullOrWhiteSpace(entry.UserMessage))
            {
                sb.Append("<div class=\"chat-user\"><div class=\"chat-label\">You</div><div class=\"chat-bubble user-bubble\">");
                sb.Append(HtmlEncode(entry.UserMessage));
                sb.Append("</div></div>");
            }

            // Assistant message
            if (!string.IsNullOrWhiteSpace(entry.AssistantMessage))
            {
                sb.Append("<div class=\"chat-assistant\"><div class=\"chat-label\">Adze</div><div class=\"chat-bubble assistant-bubble\">");
                sb.Append(ConvertTextToHtmlBody(entry.AssistantMessage));
                sb.Append("</div>");
                if (!string.IsNullOrWhiteSpace(entry.Footer))
                {
                    sb.Append("<div class=\"chat-footer\">");
                    sb.Append(HtmlEncode(entry.Footer));
                    sb.Append("</div>");
                }
                sb.Append("</div>");
            }
        }

        // Show error if last run failed
        if (!string.IsNullOrEmpty(_lastErrorMessage))
        {
            sb.Append("<div class=\"chat-assistant\"><div class=\"chat-label\">Adze</div><div class=\"chat-bubble assistant-bubble error-bubble\">");
            sb.Append(HtmlEncode(_lastErrorMessage!));
            sb.Append("</div></div>");
        }

        // Show pending write confirmations
        var pendingWrites = HostState.GetPendingWrites();
        for (int i = 0; i < pendingWrites.Count; i++)
        {
            var pw = pendingWrites[i];
            sb.Append(BuildWriteConfirmationCard(pw, i));
        }

        return sb.ToString();
    }

    private static string BuildWriteConfirmationCard(PendingWriteAction pw, int index)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"write-card\">");

        if (pw.Applied)
        {
            sb.Append("<div class=\"write-status applied\">Applied</div>");
            sb.Append("<div class=\"write-summary\">" + HtmlEncode(pw.Preview.Summary) + "</div>");
            if (!string.IsNullOrWhiteSpace(pw.ResultMessage))
                sb.Append("<div class=\"write-result\">" + HtmlEncode(pw.ResultMessage!) + "</div>");
        }
        else if (pw.Cancelled)
        {
            sb.Append("<div class=\"write-status cancelled\">Cancelled</div>");
            sb.Append("<div class=\"write-summary\">" + HtmlEncode(pw.Preview.Summary) + "</div>");
        }
        else
        {
            sb.Append("<div class=\"write-header\">Proposed Change</div>");
            sb.Append("<div class=\"write-summary\">" + HtmlEncode(pw.Preview.Summary) + "</div>");

            if (pw.Preview.Changes.Count > 0)
            {
                sb.Append("<table class=\"write-changes\">");
                sb.Append("<tr><th>Target</th><th>Before</th><th>After</th></tr>");
                foreach (var c in pw.Preview.Changes)
                {
                    sb.Append("<tr>");
                    sb.Append("<td>" + HtmlEncode(c.TargetLabel) + "</td>");
                    sb.Append("<td>" + HtmlEncode(c.BeforeValue) + "</td>");
                    sb.Append("<td><strong>" + HtmlEncode(c.AfterValue) + "</strong></td>");
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }

            foreach (var w in pw.Preview.Warnings)
            {
                sb.Append("<div class=\"write-warning\">" + HtmlEncode(w) + "</div>");
            }

            sb.Append("<div class=\"write-actions\">");
            sb.Append("<button class=\"btn-apply\" onclick=\"window.external.ApplyWrite(" + index + ")\">Apply</button>");
            sb.Append("<button class=\"btn-cancel\" onclick=\"window.external.CancelWrite(" + index + ")\">Cancel</button>");
            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private string BuildFullPageHtml()
    {
        string conversationHtml = BuildConversationHtml();

        string planBody = string.IsNullOrWhiteSpace(_planText)
            ? "<p class=\"muted\">Plan details appear after a run.</p>"
            : "<pre>" + HtmlEncode(_planText) + "</pre>";

        string statusBody = string.IsNullOrWhiteSpace(_statusText)
            ? "<p class=\"muted\">Status refreshes automatically.</p>"
            : "<pre>" + HtmlEncode(_statusText) + "</pre>";

        string toolsBody = string.IsNullOrWhiteSpace(_toolsText)
            ? "<p class=\"muted\">Tool execution results appear after a run.</p>"
            : "<pre>" + HtmlEncode(_toolsText) + "</pre>";

        string TabClass(string name) => _activeTab == name ? "tab active" : "tab";

        return @"<!DOCTYPE html>
<html><head><meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body {
    height: 100%;
    font-family: 'Segoe UI', sans-serif;
    font-size: 13px;
    line-height: 1.5;
    color: #22292F;
    background: #F4F6F8;
  }
  .container {
    display: flex;
    flex-direction: column;
    height: 100%;
    min-height: 100%;
  }

  /* --- Chat area --- */
  .answer-area {
    flex: 1 1 auto;
    overflow-y: auto;
    background: #F4F6F8;
    padding: 8px 10px 6px 10px;
    margin: 0;
  }
  .chat-user, .chat-assistant { margin: 0 0 10px 0; }
  .chat-label {
    font-size: 11px;
    font-weight: 600;
    color: #566370;
    margin: 0 0 2px 4px;
  }
  .chat-bubble {
    padding: 8px 12px;
    border-radius: 6px;
    font-size: 13px;
    line-height: 1.5;
  }
  .user-bubble {
    background: #E3EAF2;
    color: #18304C;
    white-space: pre-wrap;
    word-wrap: break-word;
  }
  .assistant-bubble {
    background: #FFFFFF;
    color: #22292F;
    border: 1px solid #E8EAED;
  }
  .error-bubble {
    background: #FFF5F5;
    border-color: #E8BFBF;
    color: #8B2020;
  }
  .chat-footer {
    font-size: 10px;
    color: #98A0A8;
    margin: 2px 0 0 4px;
  }

  /* --- Markdown in assistant bubbles --- */
  .assistant-bubble h1 { font-size: 15px; font-weight: 600; color: #18304C; margin: 10px 0 5px 0; }
  .assistant-bubble h2 { font-size: 14px; font-weight: 600; color: #18304C; margin: 8px 0 4px 0; }
  .assistant-bubble h3 { font-size: 13px; font-weight: 600; color: #22292F; margin: 6px 0 3px 0; }
  .assistant-bubble p { margin: 0 0 7px 0; }
  .assistant-bubble strong, .assistant-bubble b { font-weight: 600; }
  .assistant-bubble ul, .assistant-bubble ol { margin: 3px 0 7px 18px; }
  .assistant-bubble li { margin: 1px 0; }
  .assistant-bubble code {
    font-family: Consolas, monospace;
    font-size: 12px;
    background: #F0F2F4;
    padding: 1px 4px;
    border-radius: 2px;
  }
  .assistant-bubble pre {
    font-family: Consolas, monospace;
    font-size: 11px;
    background: #F4F6F8;
    padding: 8px 10px;
    margin: 4px 0 8px 0;
    border-radius: 3px;
    overflow-x: auto;
    white-space: pre-wrap;
    word-wrap: break-word;
    line-height: 1.4;
  }
  .assistant-bubble table { border-collapse: collapse; margin: 4px 0 8px 0; font-size: 12px; width: 100%; }
  .assistant-bubble th, .assistant-bubble td { border: 1px solid #DDE1E6; padding: 3px 6px; text-align: left; }
  .assistant-bubble th { background: #F4F6F8; font-weight: 600; }

  /* --- Tab bar --- */
  .tab-bar {
    display: flex;
    background: #EBEDF0;
    border-top: 1px solid #DDE1E6;
    flex-shrink: 0;
  }
  .tab {
    flex: 1;
    padding: 6px 0;
    text-align: center;
    font-size: 11px;
    font-weight: 500;
    color: #566370;
    cursor: pointer;
    border-bottom: 2px solid transparent;
    -ms-user-select: none;
  }
  .tab:hover { color: #22292F; background: #E0E3E7; }
  .tab.active {
    color: #18304C;
    font-weight: 600;
    background: #FFFFFF;
    border-bottom-color: #18304C;
  }

  /* --- Tab content --- */
  .tab-content {
    flex: 0 0 auto;
    max-height: 40%;
    overflow-y: auto;
    background: #FFFFFF;
    padding: 8px 12px;
    display: none;
  }
  .tab-content.active { display: block; }
  .tab-content pre {
    font-family: Consolas, monospace;
    font-size: 11px;
    background: #F4F6F8;
    padding: 8px 10px;
    margin: 4px 0 8px 0;
    border-radius: 3px;
    overflow-x: auto;
    white-space: pre-wrap;
    word-wrap: break-word;
    line-height: 1.4;
  }

  /* --- Write confirmation cards --- */
  .write-card {
    margin: 8px 0;
    padding: 10px 12px;
    background: #FFFDF5;
    border: 1px solid #E8D9A0;
    border-radius: 6px;
  }
  .write-header {
    font-size: 11px;
    font-weight: 600;
    color: #8B6914;
    text-transform: uppercase;
    margin-bottom: 4px;
  }
  .write-summary { margin-bottom: 6px; }
  .write-changes {
    border-collapse: collapse;
    width: 100%;
    font-size: 12px;
    margin: 4px 0 6px 0;
  }
  .write-changes th, .write-changes td {
    border: 1px solid #E8D9A0;
    padding: 3px 6px;
    text-align: left;
  }
  .write-changes th { background: #FFF8E1; font-weight: 600; }
  .write-warning {
    font-size: 11px;
    color: #A66B00;
    margin: 3px 0;
    padding-left: 4px;
    border-left: 2px solid #E8A500;
  }
  .write-actions { margin-top: 8px; }
  .btn-apply {
    padding: 4px 16px;
    font-size: 12px;
    font-weight: 600;
    color: #FFFFFF;
    background: #2E7D32;
    border: none;
    border-radius: 3px;
    cursor: pointer;
    margin-right: 6px;
  }
  .btn-apply:hover { background: #1B5E20; }
  .btn-cancel {
    padding: 4px 16px;
    font-size: 12px;
    color: #566370;
    background: #EBEDF0;
    border: 1px solid #DDE1E6;
    border-radius: 3px;
    cursor: pointer;
  }
  .btn-cancel:hover { background: #DDE1E6; }
  .write-status {
    font-size: 11px;
    font-weight: 600;
    margin-bottom: 4px;
  }
  .write-status.applied { color: #2E7D32; }
  .write-status.cancelled { color: #78808A; }
  .write-result {
    font-size: 12px;
    color: #2E7D32;
    margin-top: 4px;
  }

  .muted { color: #78808A; font-style: italic; }
</style>
</head><body>
<div class=""container"">
  <div class=""answer-area"">" + conversationHtml + @"</div>
  <div class=""tab-bar"">
    <div class=""" + TabClass("answer") + @""" onclick=""switchTab('answer')"">Chat</div>
    <div class=""" + TabClass("plan") + @""" onclick=""switchTab('plan')"">Plan</div>
    <div class=""" + TabClass("status") + @""" onclick=""switchTab('status')"">Status</div>
    <div class=""" + TabClass("tools") + @""" onclick=""switchTab('tools')"">Tools</div>
  </div>
  <div id=""tc-answer"" class=""tab-content""></div>
  <div id=""tc-plan"" class=""tab-content" + (_activeTab == "plan" ? " active" : "") + @""">" + planBody + @"</div>
  <div id=""tc-status"" class=""tab-content" + (_activeTab == "status" ? " active" : "") + @""">" + statusBody + @"</div>
  <div id=""tc-tools"" class=""tab-content" + (_activeTab == "tools" ? " active" : "") + @""">" + toolsBody + @"</div>
</div>
<script>
function switchTab(name) {
  window.external.SwitchTab(name);
  var tabs = document.querySelectorAll('.tab');
  for (var i = 0; i < tabs.length; i++) tabs[i].className = 'tab';
  var contents = document.querySelectorAll('.tab-content');
  for (var i = 0; i < contents.length; i++) contents[i].className = 'tab-content';
  var area = document.querySelector('.answer-area');
  var footer = document.querySelector('.footer');

  if (name === 'answer') {
    tabs[0].className = 'tab active';
    if (area) area.style.display = 'block';
    if (footer) footer.style.display = 'block';
  } else {
    if (area) area.style.display = 'none';
    if (footer) footer.style.display = 'none';
    var idx = name === 'plan' ? 1 : name === 'status' ? 2 : 3;
    tabs[idx].className = 'tab active';
    document.getElementById('tc-' + name).className = 'tab-content active';
  }
}
function updateTabContent(tabId, html) {
  var el = document.getElementById(tabId);
  if (el) el.innerHTML = html;
}
function scrollToBottom() {
  var area = document.querySelector('.answer-area');
  if (area) area.scrollTop = area.scrollHeight;
}
scrollToBottom();
</script>
</body></html>";
    }

    // -----------------------------------------------------------------------
    // Markdown → HTML conversion
    // -----------------------------------------------------------------------

    private static string ConvertTextToHtmlBody(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var sb = new System.Text.StringBuilder(text.Length * 2);
        string[] lines = text.Split('\n');
        bool inUl = false, inOl = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                CloseList(sb, ref inUl, ref inOl);
                continue;
            }

            if (trimmed.StartsWith("### "))
            {
                CloseList(sb, ref inUl, ref inOl);
                sb.Append("<h3>").Append(Inline(HtmlEncode(trimmed.Substring(4)))).Append("</h3>");
                continue;
            }
            if (trimmed.StartsWith("## "))
            {
                CloseList(sb, ref inUl, ref inOl);
                sb.Append("<h2>").Append(Inline(HtmlEncode(trimmed.Substring(3)))).Append("</h2>");
                continue;
            }
            if (trimmed.StartsWith("# "))
            {
                CloseList(sb, ref inUl, ref inOl);
                sb.Append("<h1>").Append(Inline(HtmlEncode(trimmed.Substring(2)))).Append("</h1>");
                continue;
            }

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                if (inOl) { sb.Append("</ol>"); inOl = false; }
                if (!inUl) { sb.Append("<ul>"); inUl = true; }
                sb.Append("<li>").Append(Inline(HtmlEncode(trimmed.Substring(2)))).Append("</li>");
                continue;
            }

            if (trimmed.Length >= 3 && char.IsDigit(trimmed[0]))
            {
                int dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
                if (dot > 0 && dot <= 3 && int.TryParse(trimmed.Substring(0, dot), out _))
                {
                    if (inUl) { sb.Append("</ul>"); inUl = false; }
                    if (!inOl) { sb.Append("<ol>"); inOl = true; }
                    sb.Append("<li>").Append(Inline(HtmlEncode(trimmed.Substring(dot + 2)))).Append("</li>");
                    continue;
                }
            }

            CloseList(sb, ref inUl, ref inOl);
            sb.Append("<p>").Append(Inline(HtmlEncode(trimmed))).Append("</p>");
        }

        CloseList(sb, ref inUl, ref inOl);
        return sb.ToString();
    }

    private static void CloseList(System.Text.StringBuilder sb, ref bool inUl, ref bool inOl)
    {
        if (inUl) { sb.Append("</ul>"); inUl = false; }
        if (inOl) { sb.Append("</ol>"); inOl = false; }
    }

    private static string Inline(string html)
    {
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        html = System.Text.RegularExpressions.Regex.Replace(html, @"`(.+?)`", "<code>$1</code>");
        return html;
    }

    private static string HtmlEncode(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    // -----------------------------------------------------------------------
    // JavaScript → C# bridge (called via window.external)
    // -----------------------------------------------------------------------

    public void SwitchTab(string tabName)
    {
        _activeTab = tabName ?? "answer";
    }

    public void ApplyWrite(int index)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string result = HostState.ApplyPendingWrite(index);
            PostToUi(() =>
            {
                _runStateLabel.Text = result;
                RenderContent();
            });
        });
    }

    public void CancelWrite(int index)
    {
        HostState.CancelPendingWrite(index);
        RenderContent();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void PostToUi(Action action)
    {
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }
}
