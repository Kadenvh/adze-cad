using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
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
                Height = 36,
                Margin = new Padding(0),
                Padding = new Padding(14, 8, 14, 4),
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(24, 48, 76),
                Text = "Adze  \u2014  SOLIDWORKS AI"
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
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 114, 198),
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Text = "Run assistant",
                Cursor = Cursors.Hand
            };
            runButton.FlatAppearance.BorderSize = 0;
            runButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 90, 158);
            runButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 70, 130);

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
                Padding = new Padding(10, 6, 10, 8),
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

            // Register UI-thread invoker for COM write marshaling
            HostState.SetUiThreadInvoker(new WinFormsUiThreadInvoker(this));
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
        string userText = string.Empty;
        try
        {
            _isRunning = true;
            _runButton.Text = "Cancel";
            _runButton.Enabled = true;
            _requestBox.Enabled = false;
            _runStateLabel.Text = "Running...";
            _refreshTimer.Stop();
            Update();

            userText = GetRequestText();
            HostState.BeginRun();
            preparation = HostState.PrepareAssistantRun(userText);
        }
        catch (Exception ex)
        {
            ShowRunFailure(ex);
            FinishRun();
            return;
        }

        // Prepare streaming: add user bubble + streaming target before the background run
        string capturedUserText = userText;
        string userBubbleHtml = "<div class=\"chat-user\"><div class=\"chat-label\">You</div>" +
            "<div class=\"chat-bubble user-bubble\">" + HtmlEncode(capturedUserText) + "</div></div>";
        try
        {
            _contentBrowser.Document?.InvokeScript("startStreaming", new object[] { userBubbleHtml });
        }
        catch
        {
            // If InvokeScript fails (page not ready), streaming will degrade gracefully
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Action<string> streamCallback = chunk =>
                {
                    try { PostToUi(() => AppendStreamChunk(chunk)); }
                    catch { /* swallow UI errors during streaming */ }
                };

                Action<AgentProgressUpdate> progressCallback = progress =>
                {
                    try { PostToUi(() => UpdateRunProgress(progress)); }
                    catch { /* swallow UI errors during progress updates */ }
                };

                AssistantRunSnapshot snapshot = HostState.CompleteAssistantRun(preparation, streamCallback, progressCallback);
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

    private void UpdateRunProgress(AgentProgressUpdate progress)
    {
        string text;
        switch (progress.Kind)
        {
            case AgentProgressKind.ToolRequested:
            case AgentProgressKind.ToolExecuting:
                string toolName = progress.ToolName ?? "tool";
                text = "Calling " + toolName + "...";
                bool isWrite = IsWriteToolName(toolName);
                try { _contentBrowser.Document?.InvokeScript("showToolChip", new object[] { toolName, isWrite ? "write" : "read" }); } catch { }
                break;
            case AgentProgressKind.Thinking:
                text = progress.Iteration > 0
                    ? "Thinking (turn " + progress.Iteration + ")..."
                    : "Thinking...";
                try { _contentBrowser.Document?.InvokeScript("showThinking", new object[] { progress.Iteration }); } catch { }
                break;
            case AgentProgressKind.Completed:
                text = "Generating answer...";
                try { _contentBrowser.Document?.InvokeScript("clearProgress", Array.Empty<object>()); } catch { }
                break;
            case AgentProgressKind.Failed:
                text = "Error encountered.";
                try { _contentBrowser.Document?.InvokeScript("clearProgress", Array.Empty<object>()); } catch { }
                break;
            case AgentProgressKind.FellBack:
                text = "Falling back...";
                break;
            default:
                text = "Running...";
                break;
        }
        _runStateLabel.Text = text;
    }

    private static bool IsWriteToolName(string name) =>
        name is "set_custom_property" or "set_dimension_value" or "suppress_feature"
            or "unsuppress_feature" or "rename_object" or "insert_component" or "create_drawing_view";

    private void AppendStreamChunk(string text)
    {
        try
        {
            _contentBrowser.Document?.InvokeScript("appendStreamChunk", new object[] { text });
        }
        catch
        {
            // Swallow errors — streaming is best-effort
        }
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
        ClassifiedError classified = ErrorClassifier.Classify(ex);
        _runStateLabel.Text = classified.Tier == ErrorTier.ApiError ? "Provider error." : "Run failed.";
        _lastErrorMessage = ErrorClassifier.FormatForUser(classified);
        FileLogger.Error("Classified error (tier=" + classified.Tier + "): " + (classified.TechnicalDetail ?? ex.Message), ex);
        RenderContent();
    }

    private void FinishRun()
    {
        _requestBox.Enabled = true;
        _requestBox.Clear();
        _runButton.Enabled = true;
        _runButton.BackColor = Color.FromArgb(0, 114, 198);
        _runButton.Text = "Run assistant";
        _isRunning = false;
        ApplyRequestPlaceholder();
        ScheduleDeferredStatusRefresh();
        try { _contentBrowser.Document?.InvokeScript("clearProgress", Array.Empty<object>()); } catch { }
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
            string healthHtml = BuildLocalHealthHtml();
            string statusHtml = healthHtml + (string.IsNullOrWhiteSpace(_statusText)
                ? "<pre>Status refreshes when expanded.</pre>"
                : "<pre>" + HtmlEncode(_statusText) + "</pre>");
            try
            {
                _contentBrowser.Document?.InvokeScript("updateSectionContent",
                    new object[] { "status-body", statusHtml });
            }
            catch { /* Section may not exist yet */ }
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
        HostState.RunLocalHealthCheckAsync();
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

    private static string BuildQuickActionsHtml() =>
        "<div class=\"quick-actions\">" +
        "<button class=\"quick-btn\" onclick=\"window.external.QuickAction('diagnose')\">Diagnose</button>" +
        "<button class=\"quick-btn\" onclick=\"window.external.QuickAction('mates')\">Mates</button>" +
        "<button class=\"quick-btn\" onclick=\"window.external.QuickAction('dims')\">Dimensions</button>" +
        "<button class=\"quick-btn\" onclick=\"window.external.QuickAction('props')\">Properties</button>" +
        "</div>";

    private string BuildConversationHtml()
    {
        var history = HostState.GetChatHistory();
        if (history.Count == 0 && string.IsNullOrEmpty(_lastErrorMessage))
        {
            return BuildQuickActionsHtml() +
                "<p class=\"muted\">Open a document and ask a question above, or tap a quick action.</p>";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(BuildQuickActionsHtml());

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
        int actionableCount = 0;
        foreach (var pw in pendingWrites)
        {
            if (!pw.Applied && !pw.Cancelled)
                actionableCount++;
        }
        if (actionableCount > 1)
        {
            sb.Append("<div class=\"plan-header\">Write Plan (" + pendingWrites.Count + " steps)</div>");
            sb.Append("<div class=\"plan-actions\">");
            sb.Append("<button class=\"btn-apply\" onclick=\"window.external.ApplyAllWrites()\">Apply All</button>");
            sb.Append("<button class=\"btn-cancel\" onclick=\"window.external.CancelAllWrites()\">Cancel All</button>");
            sb.Append("</div>");
        }
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
        string extraClass = pw.IsElevated && !pw.Applied && !pw.Cancelled ? " write-card-elevated" : "";
        sb.Append("<div class=\"write-card" + extraClass + "\">");

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
            if (pw.IsElevated)
            {
                sb.Append("<div class=\"write-header write-header-elevated\">&#9888; Elevated Change</div>");
            }
            else
            {
                sb.Append("<div class=\"write-header\">Proposed Change</div>");
            }
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

    private string BuildRecipeSuggestionsHtml()
    {
        var recipes = HostState.GetSuggestedRecipes();
        if (recipes.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append(BuildCollapsibleHeader("recipes", "Suggested Recipes (" + recipes.Count + ")", false));
        sb.Append("<div id=\"recipes-body\" class=\"section-body\" style=\"display:none\">");
        for (int i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            string stateClass = string.Equals(recipe.PromotionState, "promoted", StringComparison.OrdinalIgnoreCase)
                ? "recipe-promoted" : "recipe-review";
            string stateLabel = string.Equals(recipe.PromotionState, "promoted", StringComparison.OrdinalIgnoreCase)
                ? "Promoted" : "Review Ready";

            sb.Append("<div class=\"recipe-card " + stateClass + "\">");
            sb.Append("<div class=\"recipe-title\">" + HtmlEncode(recipe.Title) + "</div>");
            sb.Append("<div class=\"recipe-state\">" + stateLabel + " &middot; " +
                      recipe.ReliabilityScore.ToString("P0") + " reliability</div>");
            sb.Append("<div class=\"recipe-tools\">");
            foreach (string tool in recipe.ToolSequence)
            {
                sb.Append("<span class=\"recipe-tool-tag\">" + HtmlEncode(tool) + "</span> ");
            }
            sb.Append("</div>");
            sb.Append("<div class=\"recipe-actions\">");
            sb.Append("<button class=\"btn-recipe-run\" onclick=\"window.external.RunRecipe(" + i + ")\">Run</button>");
            if (!string.Equals(recipe.PromotionState, "promoted", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("<button class=\"btn-recipe-promote\" onclick=\"window.external.PromoteRecipe(" + i + ")\">Promote</button>");
            }
            sb.Append("</div>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private string BuildWriteHistoryHtml()
    {
        var history = HostState.GetWriteHistory();
        if (history.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append(BuildCollapsibleHeader("write-history", "Write History (" + history.Count + ")", false));
        sb.Append("<div id=\"write-history-body\" class=\"section-body\" style=\"display:none\">");
        foreach (var entry in history)
        {
            sb.Append("<div class=\"history-entry\">");
            sb.Append("<span class=\"history-tool\">" + HtmlEncode(entry.ToolName) + "</span> ");
            sb.Append("<span class=\"history-summary\">" + HtmlEncode(entry.Summary) + "</span>");
            sb.Append("<div class=\"history-result\">" + HtmlEncode(entry.ResultMessage) + "</div>");
            if (!string.IsNullOrWhiteSpace(entry.UndoLabel))
            {
                sb.Append("<div class=\"history-undo\">Undo: " + HtmlEncode(entry.UndoLabel) + "</div>");
            }
            sb.Append("<div class=\"history-time\">" + entry.AppliedUtc.ToString("HH:mm:ss") + "</div>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildLocalHealthHtml()
    {
        var result = HostState.GetLocalHealthResult();
        if (result == null || result.Status == Adze.Broker.Clients.LocalHealthStatus.NotApplicable)
            return string.Empty;

        string cssClass;
        string icon;
        switch (result.Status)
        {
            case Adze.Broker.Clients.LocalHealthStatus.Ready:
                cssClass = "health-ready";
                icon = "&#10003;";
                break;
            case Adze.Broker.Clients.LocalHealthStatus.Unreachable:
                cssClass = "health-error";
                icon = "&#9888;";
                break;
            case Adze.Broker.Clients.LocalHealthStatus.NoModels:
            case Adze.Broker.Clients.LocalHealthStatus.ModelNotFound:
                cssClass = "health-warning";
                icon = "&#9888;";
                break;
            default:
                cssClass = "health-error";
                icon = "&#9888;";
                break;
        }

        string message = HtmlEncode(result.Message);
        string guidance = string.Empty;
        switch (result.Status)
        {
            case Adze.Broker.Clients.LocalHealthStatus.Ready:
                guidance = "<div class=\"health-guidance\">Local model support is experimental.</div>";
                break;
            case Adze.Broker.Clients.LocalHealthStatus.Unreachable:
                guidance = message.IndexOf("ollama", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "<div class=\"health-guidance\">Start the server: <code>ollama serve</code></div>"
                    : "<div class=\"health-guidance\">Start the LM Studio server from the application.</div>";
                break;
            case Adze.Broker.Clients.LocalHealthStatus.NoModels:
                guidance = "<div class=\"health-guidance\">Pull a model: <code>ollama pull qwen2.5:32b</code></div>";
                break;
            case Adze.Broker.Clients.LocalHealthStatus.ModelNotFound:
                if (result.AvailableModels.Count > 0)
                    guidance = "<div class=\"health-guidance\">Available: " +
                        HtmlEncode(string.Join(", ", result.AvailableModels)) + "</div>";
                break;
        }

        return "<div class=\"health-banner " + cssClass + "\">" + icon + " " + message + guidance + "</div>";
    }

    private static string BuildBudgetHtml()
    {
        var budget = HostState.GetBudgetStatus();
        var (runCount, usage) = HostState.GetSessionUsage();
        if (usage.TotalTokens == 0 && runCount == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();

        // Warning banner if near or over limit
        var budgetSettings = Adze.Broker.Configuration.CostBudgetSettings.LoadFromEnvironment();
        if (budget.IsOverBudget)
        {
            sb.Append("<div class=\"health-banner health-error\">&#9888; Token budget exhausted. ");
            if (budget.SessionLimitReached)
                sb.Append("Session limit reached (" + budget.SessionTokensUsed.ToString("N0") + "/" + budget.SessionTokenLimit.ToString("N0") + "). ");
            if (budget.DailyLimitReached)
                sb.Append("Daily limit reached (" + budget.DailyTokensUsed.ToString("N0") + "/" + budget.DailyTokenLimit.ToString("N0") + "). ");
            sb.Append("</div>");
        }
        else if (budget.IsNearLimit(budgetSettings.WarningThresholdPercent))
        {
            sb.Append("<div class=\"health-banner health-warning\">&#9888; Approaching token budget limit. ");
            double sessionPct = budget.SessionTokenLimit > 0 ? (double)budget.SessionTokensUsed / budget.SessionTokenLimit * 100 : 0;
            sb.Append("Session: " + sessionPct.ToString("0") + "% used.");
            sb.Append("</div>");
        }

        // Usage dashboard
        sb.Append("<div class=\"telemetry-dashboard\">");
        sb.Append("<div class=\"telemetry-title\">Usage</div>");

        sb.Append("<div class=\"telemetry-row\">");
        sb.Append("<span class=\"telemetry-label\">Runs:</span> " + runCount);
        sb.Append("</div>");

        sb.Append("<div class=\"telemetry-row\">");
        sb.Append("<span class=\"telemetry-label\">Tokens:</span> " + usage.TotalTokens.ToString("N0"));
        sb.Append(" (prompt: " + usage.PromptTokens.ToString("N0") + ", completion: " + usage.CompletionTokens.ToString("N0") + ")");
        sb.Append("</div>");

        // Budget bars
        double sessionPercent = budget.SessionTokenLimit > 0 ? (double)budget.SessionTokensUsed / budget.SessionTokenLimit * 100 : 0;
        string sessionBarColor = sessionPercent >= 90 ? "#C53030" : sessionPercent >= 70 ? "#D69E2E" : "#38A169";
        sb.Append("<div class=\"telemetry-row\">");
        sb.Append("<span class=\"telemetry-label\">Session budget:</span> " +
            budget.SessionTokensUsed.ToString("N0") + " / " + budget.SessionTokenLimit.ToString("N0") +
            " (" + sessionPercent.ToString("0.0") + "%)");
        sb.Append("</div>");
        sb.Append("<div class=\"budget-bar\"><div class=\"budget-fill\" style=\"width:" +
            Math.Min(sessionPercent, 100).ToString("0.0") + "%;background:" + sessionBarColor + "\"></div></div>");

        // Cost estimate (rough: $0.003/1K prompt tokens, $0.015/1K completion tokens for typical models)
        double estimatedCost = (usage.PromptTokens / 1000.0) * 0.003 + (usage.CompletionTokens / 1000.0) * 0.015;
        if (estimatedCost > 0.001)
        {
            sb.Append("<div class=\"telemetry-row\">");
            sb.Append("<span class=\"telemetry-label\">Est. cost:</span> ~$" + estimatedCost.ToString("0.00"));
            sb.Append(" <span style=\"opacity:0.6\">(approximate)</span>");
            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildTelemetryHtml()
    {
        var telemetry = HostState.GetTelemetry();
        if (telemetry.RunsTotal == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"telemetry-dashboard\">");
        sb.Append("<div class=\"telemetry-title\">Session Telemetry</div>");

        // Run stats row
        sb.Append("<div class=\"telemetry-row\">");
        sb.Append("<span class=\"telemetry-label\">Runs:</span> ");
        sb.Append(telemetry.RunsTotal + " total");
        if (telemetry.AgenticRuns > 0)
            sb.Append(" (" + telemetry.AgenticRuns + " agentic");
        if (telemetry.ClassicRuns > 0)
            sb.Append(telemetry.AgenticRuns > 0 ? ", " : " (");
        if (telemetry.ClassicRuns > 0)
            sb.Append(telemetry.ClassicRuns + " classic");
        if (telemetry.AgenticRuns > 0 || telemetry.ClassicRuns > 0)
            sb.Append(")");
        sb.Append("</div>");

        // Success rate
        sb.Append("<div class=\"telemetry-row\">");
        sb.Append("<span class=\"telemetry-label\">Success:</span> ");
        sb.Append(telemetry.RunsSuccess + "/" + telemetry.RunsTotal);
        sb.Append(" (" + (telemetry.SuccessRate * 100).ToString("0") + "%)");
        if (telemetry.RunsCancelled > 0)
            sb.Append(" &middot; " + telemetry.RunsCancelled + " cancelled");
        if (telemetry.RunsFailed > 0)
            sb.Append(" &middot; " + telemetry.RunsFailed + " failed");
        sb.Append("</div>");

        // Tool call ranking (top 5)
        var ranking = telemetry.GetToolCallRanking();
        if (ranking.Count > 0)
        {
            sb.Append("<div class=\"telemetry-row\"><span class=\"telemetry-label\">Top tools:</span></div>");
            var errorCounts = telemetry.GetToolErrorCounts();
            int shown = 0;
            foreach (var kv in ranking)
            {
                if (shown >= 5) break;
                sb.Append("<div class=\"telemetry-tool\">");
                sb.Append(HtmlEncode(kv.Key) + ": " + kv.Value);
                if (errorCounts.ContainsKey(kv.Key) && errorCounts[kv.Key] > 0)
                    sb.Append(" <span style=\"color:#C53030\">(" + errorCounts[kv.Key] + " err)</span>");
                sb.Append("</div>");
                shown++;
            }
        }

        // Writes
        if (telemetry.WritesProposed > 0)
        {
            sb.Append("<div class=\"telemetry-row\">");
            sb.Append("<span class=\"telemetry-label\">Writes:</span> ");
            sb.Append(telemetry.WritesApplied + " applied, " + telemetry.WritesCancelled + " cancelled");
            if (telemetry.WritesFailed > 0)
                sb.Append(", " + telemetry.WritesFailed + " failed");
            sb.Append(" of " + telemetry.WritesProposed + " proposed");
            sb.Append("</div>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    private static string BuildCollapsibleHeader(string id, string label, bool open)
    {
        string arrow = open ? "&#9662;" : "&#9656;";
        return "<div class=\"section-header\" onclick=\"toggleSection('" + id + "-body', this)\">" +
               "<span class=\"section-arrow\">" + arrow + "</span> " + HtmlEncode(label) + "</div>";
    }

    private string BuildFullPageHtml()
    {
        string conversationHtml = BuildConversationHtml();
        string recipeHtml = BuildRecipeSuggestionsHtml();
        string writeHistoryHtml = BuildWriteHistoryHtml();

        string planSection = string.Empty;
        if (!string.IsNullOrWhiteSpace(_planText))
        {
            planSection = BuildCollapsibleHeader("plan", "Plan", false) +
                "<div id=\"plan-body\" class=\"section-body\" style=\"display:none\"><pre>" +
                HtmlEncode(_planText) + "</pre></div>";
        }

        string toolsLabel = "Tools Log";
        string toolsSection = string.Empty;
        if (!string.IsNullOrWhiteSpace(_toolsText))
        {
            toolsSection = BuildCollapsibleHeader("tools", toolsLabel, false) +
                "<div id=\"tools-body\" class=\"section-body\" style=\"display:none\"><pre>" +
                HtmlEncode(_toolsText) + "</pre></div>";
        }

        string healthBanner = BuildLocalHealthHtml();
        string budgetHtml = BuildBudgetHtml();
        string telemetryHtml = BuildTelemetryHtml();
        string statusSection = BuildCollapsibleHeader("status", "Status", false) +
            "<div id=\"status-body\" class=\"section-body\" style=\"display:none\">" +
            healthBanner + budgetHtml + telemetryHtml + "<pre>" +
            HtmlEncode(string.IsNullOrWhiteSpace(_statusText) ? "Status refreshes when expanded." : _statusText) +
            "</pre></div>";

        return @"<!DOCTYPE html>
<html><head><meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body {
    height: 100%;
    font-family: 'Segoe UI', sans-serif;
    font-size: 13px;
    line-height: 1.5;
    color: #1A2332;
    background: #EFF1F5;
    overflow-y: auto;
  }

  /* --- Quick action toolbar --- */
  .quick-actions {
    display: -ms-flexbox; display: flex;
    -ms-flex-wrap: wrap; flex-wrap: wrap;
    gap: 5px;
    padding: 10px 12px 10px 12px;
    background: #FFFFFF;
    border-bottom: 1px solid #DDE6F0;
  }
  .quick-btn {
    padding: 4px 11px;
    font-size: 11px;
    font-weight: 600;
    color: #0058A3;
    background: #EBF3FF;
    border: 1px solid #B3D4F5;
    border-radius: 12px;
    cursor: pointer;
    font-family: 'Segoe UI', sans-serif;
    letter-spacing: 0.2px;
    -ms-user-select: none;
  }
  .quick-btn:hover { background: #D0E6FF; color: #003E78; border-color: #85BBEE; }
  .quick-btn:active { background: #B3D4F5; }

  /* --- Chat area --- */
  .content-area { padding: 12px 12px 6px 12px; }
  .chat-user, .chat-assistant { margin: 0 0 12px 0; }
  .chat-label {
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.6px;
    color: #8A9AAA;
    margin: 0 0 3px 2px;
  }
  .chat-bubble {
    padding: 9px 13px;
    border-radius: 8px;
    font-size: 13px;
    line-height: 1.55;
  }
  .user-bubble {
    background: #1B3A6B;
    color: #FFFFFF;
    white-space: pre-wrap;
    word-wrap: break-word;
    border-radius: 8px 8px 8px 2px;
  }
  .assistant-bubble {
    background: #FFFFFF;
    color: #1A2332;
    border: 1px solid #E0E6EF;
    box-shadow: 0 1px 4px rgba(0,30,70,.07);
    border-radius: 8px 8px 2px 8px;
  }
  .error-bubble {
    background: #FEF2F2;
    border-color: #FECACA;
    color: #991B1B;
    box-shadow: none;
  }
  .chat-footer {
    font-size: 10px;
    color: #9AAAB8;
    margin: 3px 0 0 4px;
  }
  .muted { color: #8A9AAA; font-size: 12px; font-style: italic; padding: 8px 0; }

  /* --- Agent progress chips --- */
  #agent-progress { margin: 2px 0 8px 0; min-height: 0; }
  .tool-chip {
    display: inline-block;
    padding: 2px 8px;
    border-radius: 10px;
    font-size: 10px;
    font-weight: 600;
    margin: 0 3px 4px 0;
    font-family: Consolas, monospace;
    letter-spacing: 0.2px;
  }
  .chip-read    { background: #E8F3FF; color: #0058A3; border: 1px solid #BAD7F5; }
  .chip-write   { background: #FFF3E0; color: #B54A00; border: 1px solid #FFCC80; }
  .chip-thinking{ background: #F0F4FF; color: #3B4EAA; border: 1px solid #C5CCEE; }
  @keyframes blink { 0%,80%,100%{opacity:0} 40%{opacity:1} }
  .dot { animation: blink 1.4s infinite both; display: inline-block; }
  .dot:nth-child(2) { animation-delay: .2s; }
  .dot:nth-child(3) { animation-delay: .4s; }

  /* --- Markdown in assistant bubbles --- */
  .assistant-bubble h1 { font-size: 15px; font-weight: 700; color: #0D1F35; margin: 10px 0 5px 0; }
  .assistant-bubble h2 { font-size: 14px; font-weight: 700; color: #0D1F35; margin: 8px 0 4px 0; }
  .assistant-bubble h3 { font-size: 13px; font-weight: 600; color: #1A2332; margin: 6px 0 3px 0; }
  .assistant-bubble p { margin: 0 0 7px 0; }
  .assistant-bubble strong, .assistant-bubble b { font-weight: 700; color: #0D1F35; }
  .assistant-bubble ul, .assistant-bubble ol { margin: 3px 0 7px 20px; }
  .assistant-bubble li { margin: 2px 0; }
  .assistant-bubble code {
    font-family: Consolas, monospace;
    font-size: 12px;
    background: #EBF1FF;
    color: #1B3A6B;
    padding: 1px 5px;
    border-radius: 3px;
  }
  .assistant-bubble pre {
    font-family: Consolas, monospace;
    font-size: 11px;
    background: #F4F7FB;
    padding: 9px 11px;
    margin: 4px 0 8px 0;
    border-radius: 5px;
    border: 1px solid #DDE6F0;
    overflow-x: auto;
    white-space: pre-wrap;
    word-wrap: break-word;
    line-height: 1.45;
  }
  .assistant-bubble table { border-collapse: collapse; margin: 4px 0 8px 0; font-size: 12px; width: 100%; }
  .assistant-bubble th, .assistant-bubble td { border: 1px solid #DDE6F0; padding: 4px 7px; text-align: left; }
  .assistant-bubble th { background: #EBF1FF; font-weight: 700; color: #1B3A6B; }
  .assistant-bubble tr:nth-child(even) td { background: #F7FAFF; }

  /* --- Collapsible sections --- */
  .sections-area {
    border-top: 2px solid #D0DAE8;
    background: #E8ECF2;
  }
  .section-header {
    padding: 7px 12px;
    font-size: 11px;
    font-weight: 700;
    color: #3D4F60;
    cursor: pointer;
    border-bottom: 1px solid #D0DAE8;
    border-left: 3px solid #0072C6;
    -ms-user-select: none;
    letter-spacing: 0.3px;
  }
  .section-header:hover { color: #0072C6; background: #EBF3FF; }
  .section-arrow { font-size: 10px; color: #8A9AAA; }
  .section-body {
    background: #FFFFFF;
    padding: 9px 13px;
    border-bottom: 1px solid #D0DAE8;
    max-height: 300px;
    overflow-y: auto;
  }
  .section-body pre {
    font-family: Consolas, monospace;
    font-size: 11px;
    background: #F4F7FB;
    padding: 8px 10px;
    margin: 4px 0;
    border-radius: 4px;
    border: 1px solid #DDE6F0;
    white-space: pre-wrap;
    word-wrap: break-word;
    line-height: 1.4;
  }
  .history-entry {
    padding: 5px 0;
    border-bottom: 1px solid #EEF2F7;
    font-size: 12px;
  }
  .history-entry:last-child { border-bottom: none; }
  .history-tool { font-family: Consolas, monospace; font-size: 11px; color: #0058A3; font-weight: 700; }
  .history-summary { color: #1A2332; }
  .history-result { color: #16A34A; font-size: 11px; }
  .history-undo { color: #6B7A8A; font-size: 10px; font-style: italic; }
  .history-time { color: #9AAAB8; font-size: 10px; }

  /* --- Write plan header --- */
  .plan-header { font-size: 13px; font-weight: 700; color: #1B3A6B; padding: 8px 0 4px 0; }
  .plan-actions { margin: 0 0 6px 0; }
  .plan-actions .btn-apply, .plan-actions .btn-cancel { margin-right: 6px; }

  /* --- Write confirmation cards --- */
  .write-card {
    margin: 8px 0;
    padding: 11px 13px;
    background: #FFFEF5;
    border: 1px solid #E4D38A;
    border-radius: 8px;
    box-shadow: 0 1px 4px rgba(160,120,0,.08);
  }
  .write-card-elevated {
    background: #FFF8E1;
    border-color: #F9A825;
    border-width: 2px;
    box-shadow: 0 2px 8px rgba(180,80,0,.10);
  }
  .write-header {
    font-size: 10px;
    font-weight: 700;
    color: #8B6914;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-bottom: 5px;
  }
  .write-header-elevated { color: #C84B00; }
  .write-summary { margin-bottom: 7px; font-size: 13px; }
  .write-changes {
    border-collapse: collapse;
    width: 100%;
    font-size: 12px;
    margin: 4px 0 7px 0;
  }
  .write-changes th, .write-changes td {
    border: 1px solid #E4D38A;
    padding: 4px 7px;
    text-align: left;
  }
  .write-changes th { background: #FFF3CC; font-weight: 700; }
  .write-changes tr:nth-child(even) td { background: #FFFDF0; }
  .write-warning {
    font-size: 11px;
    color: #9A6200;
    margin: 3px 0;
    padding: 3px 6px;
    border-left: 3px solid #F59E0B;
    background: #FFFBEB;
    border-radius: 0 3px 3px 0;
  }
  .write-actions { margin-top: 9px; }
  .btn-apply {
    padding: 5px 18px;
    font-size: 12px;
    font-weight: 700;
    color: #FFFFFF;
    background: #16A34A;
    border: none;
    border-radius: 5px;
    cursor: pointer;
    margin-right: 7px;
    letter-spacing: 0.2px;
  }
  .btn-apply:hover { background: #15803D; }
  .btn-apply:active { background: #166534; }
  .btn-cancel {
    padding: 5px 18px;
    font-size: 12px;
    font-weight: 600;
    color: #4A5568;
    background: #EBF0F7;
    border: 1px solid #C8D4E3;
    border-radius: 5px;
    cursor: pointer;
  }
  .btn-cancel:hover { background: #D8E3F0; }
  .write-status {
    font-size: 11px;
    font-weight: 700;
    margin-bottom: 4px;
  }
  .write-status.applied { color: #16A34A; }
  .write-status.cancelled { color: #6B7A8A; }
  .write-result {
    font-size: 12px;
    color: #16A34A;
    margin-top: 5px;
  }

  /* --- Recipe suggestions --- */
  .recipe-card {
    padding: 9px 11px;
    margin: 5px 0;
    border-radius: 6px;
    border: 1px solid #D0DAE8;
    background: #FFFFFF;
    box-shadow: 0 1px 3px rgba(0,30,70,.05);
  }
  .recipe-promoted { border-left: 3px solid #16A34A; }
  .recipe-review { border-left: 3px solid #D97706; }
  .recipe-title { font-size: 12px; font-weight: 700; color: #1B3A6B; }
  .recipe-state { font-size: 10px; color: #6B7A8A; margin: 2px 0; letter-spacing: 0.3px; }
  .recipe-tools { margin: 5px 0; }
  .recipe-tool-tag {
    display: inline-block;
    font-family: Consolas, monospace;
    font-size: 10px;
    background: #EBF3FF;
    color: #0058A3;
    padding: 1px 5px;
    border-radius: 3px;
    margin: 1px 2px 1px 0;
    border: 1px solid #BAD7F5;
  }
  .recipe-actions { margin-top: 7px; }
  .btn-recipe-run {
    padding: 4px 13px;
    font-size: 11px;
    font-weight: 700;
    color: #FFFFFF;
    background: #0072C6;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    margin-right: 5px;
    letter-spacing: 0.2px;
  }
  .btn-recipe-run:hover { background: #005EA3; }
  .btn-recipe-promote {
    padding: 4px 13px;
    font-size: 11px;
    font-weight: 600;
    color: #3D4F60;
    background: #EBF0F7;
    border: 1px solid #C8D4E3;
    border-radius: 4px;
    cursor: pointer;
  }
  .btn-recipe-promote:hover { background: #D8E3F0; }

  /* --- Health check banners --- */
  .health-banner { padding: 7px 11px; margin: 0 0 7px 0; border-radius: 6px; font-size: 12px; line-height: 1.45; }
  .health-ready   { background: #F0FDF4; color: #166534; border: 1px solid #BBF7D0; }
  .health-warning { background: #FFFBEB; color: #92400E; border: 1px solid #FDE68A; }
  .health-error   { background: #FEF2F2; color: #991B1B; border: 1px solid #FECACA; }
  .health-guidance { font-size: 11px; margin-top: 3px; opacity: 0.85; }

  /* --- Telemetry / budget --- */
  .telemetry-dashboard { padding: 9px 11px; margin: 0 0 7px 0; background: #F4F7FB; border: 1px solid #D0DAE8; border-radius: 6px; font-size: 12px; line-height: 1.5; }
  .telemetry-title { font-weight: 700; color: #1B3A6B; margin-bottom: 4px; }
  .telemetry-row { color: #3D4F60; }
  .telemetry-label { font-weight: 700; color: #1B3A6B; }
  .telemetry-tool { padding-left: 12px; color: #3D4F60; font-family: 'Consolas', monospace; font-size: 11px; }
  .budget-bar { height: 7px; background: #DDE6F0; border-radius: 4px; margin: 3px 0 5px 0; overflow: hidden; }
  .budget-fill { height: 100%; border-radius: 4px; -webkit-transition: width .3s; transition: width .3s; }
</style>
</head><body>
<div id=""chat-area"" class=""content-area"">" + conversationHtml + @"</div>
<div class=""sections-area"">"
    + recipeHtml
    + writeHistoryHtml
    + planSection
    + toolsSection
    + statusSection + @"
</div>
<script>
function toggleSection(bodyId, headerEl) {
  var body = document.getElementById(bodyId);
  if (!body) return;
  var arrow = headerEl.querySelector('.section-arrow');
  if (body.style.display === 'none') {
    body.style.display = 'block';
    if (arrow) arrow.innerHTML = '&#9662;';
  } else {
    body.style.display = 'none';
    if (arrow) arrow.innerHTML = '&#9656;';
  }
}
function updateSectionContent(sectionId, html) {
  var el = document.getElementById(sectionId);
  if (el) el.innerHTML = html;
}
function scrollToBottom() {
  window.scrollTo(0, document.body.scrollHeight);
}
function showToolChip(toolName, kind) {
  var prog = document.getElementById('agent-progress');
  if (!prog) return;
  var chip = document.createElement('span');
  chip.className = 'tool-chip ' + (kind === 'write' ? 'chip-write' : 'chip-read');
  chip.appendChild(document.createTextNode(toolName));
  prog.appendChild(chip);
  scrollToBottom();
}
function showThinking(turn) {
  var prog = document.getElementById('agent-progress');
  if (!prog) return;
  var existing = document.getElementById('thinking-ind');
  if (existing) existing.parentNode.removeChild(existing);
  var ind = document.createElement('span');
  ind.id = 'thinking-ind';
  ind.className = 'tool-chip chip-thinking';
  var label = turn > 0 ? 'turn ' + turn : 'thinking';
  ind.innerHTML = label + ' <span class=""dot"">.</span><span class=""dot"">.</span><span class=""dot"">.</span>';
  prog.appendChild(ind);
  scrollToBottom();
}
function clearProgress() {
  var prog = document.getElementById('agent-progress');
  if (prog) prog.innerHTML = '';
}
function startStreaming(userHtml) {
  var chat = document.getElementById('chat-area');
  if (!chat) return;
  clearProgress();
  chat.innerHTML = chat.innerHTML + userHtml +
    '<div id=""agent-progress""></div>' +
    '<div class=""chat-assistant""><div class=""chat-label"">Adze</div>' +
    '<div class=""chat-bubble assistant-bubble"">' +
    '<pre id=""stream-target"" style=""margin:0;padding:0;background:transparent;' +
    'font-family:inherit;font-size:inherit;white-space:pre-wrap;word-wrap:break-word""></pre>' +
    '</div></div>';
  scrollToBottom();
}
function appendStreamChunk(text) {
  var target = document.getElementById('stream-target');
  if (target) {
    target.appendChild(document.createTextNode(text));
    scrollToBottom();
  }
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
        // COM writes must run on the UI thread (STA) — not a background thread.
        string result = HostState.ApplyPendingWrite(index);
        _runStateLabel.Text = result;
        RenderContent();
    }

    public void CancelWrite(int index)
    {
        HostState.CancelPendingWrite(index);
        RenderContent();
    }

    public void ApplyAllWrites()
    {
        var results = HostState.ApplyAllPendingWrites();
        _runStateLabel.Text = results.Count > 0
            ? "Applied " + results.Count + " write(s)."
            : "No pending writes to apply.";
        RenderContent();
    }

    public void CancelAllWrites()
    {
        HostState.CancelAllPendingWrites();
        _runStateLabel.Text = "All pending writes cancelled.";
        RenderContent();
    }

    public void RunRecipe(int index)
    {
        var recipes = HostState.GetSuggestedRecipes();
        if (index < 0 || index >= recipes.Count) return;
        var recipe = recipes[index];

        // Populate the request box with the recipe intent and auto-run
        _requestBox.Text = recipe.Intent;
        _requestPlaceholderActive = false;
        _requestBox.ForeColor = Color.FromArgb(34, 41, 47);
        RunAssistant();
    }

    public void PromoteRecipe(int index)
    {
        var recipes = HostState.GetSuggestedRecipes();
        if (index < 0 || index >= recipes.Count) return;
        var recipe = recipes[index];

        if (string.Equals(recipe.PromotionState, "review_ready", StringComparison.OrdinalIgnoreCase))
        {
            Adze.Trace.Recipes.AgentRecipeCaptureService.Promote(recipe.RecipeId);
            _runStateLabel.Text = "Recipe promoted: " + recipe.Title;
            RenderContent();
        }
    }

    public void QuickAction(string actionKey)
    {
        string prompt = actionKey switch
        {
            "diagnose" => "What's wrong with this assembly? List all mate errors, over-constraints, dangling references, and rebuild failures.",
            "mates"    => "List all mates in this assembly. Show each mate's type, the components it connects, and whether it's healthy or has an error.",
            "dims"     => "Show me all the key dimensions in this document, their current values, and which features they control.",
            "props"    => "Show all custom properties for this document and their current values.",
            _          => ""
        };
        if (string.IsNullOrEmpty(prompt)) return;
        PostToUi(() =>
        {
            _requestBox.Text = prompt;
            _requestBox.ForeColor = Color.FromArgb(34, 41, 47);
            _requestPlaceholderActive = false;
            RunAssistant();
        });
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
