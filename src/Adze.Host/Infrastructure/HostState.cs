using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using Adze.Broker.Abstractions;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using AgentConversationState = Adze.Broker.Models.AgentConversationState;
using ConversationMessage = Adze.Broker.Models.ConversationMessage;
using ConversationRole = Adze.Broker.Models.ConversationRole;
using ModelUsage = Adze.Broker.Models.ModelUsage;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Host.Services;
using Adze.Trace.Progression;
using Adze.Trace.Recipes;
using Adze.Trace.Serialization;
using Adze.Trace.Tracing;

namespace Adze.Host.Infrastructure;

internal sealed class AssistantRunPreparation
{
    public SessionContext Context { get; set; } = new();

    public string Request { get; set; } = string.Empty;

    public bool IsApplicationConnected { get; set; }
}

internal sealed class ChatEntry
{
    public string UserMessage { get; set; } = string.Empty;

    public string AssistantMessage { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Footer { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class PendingWriteAction
{
    public string ToolName { get; set; } = string.Empty;

    public Dictionary<string, object?> Arguments { get; set; } = new();

    public WritePreview Preview { get; set; } = new();

    public bool Applied { get; set; }

    public bool Cancelled { get; set; }

    public string? ResultMessage { get; set; }

    public bool IsElevated { get; set; }

    public string UndoLabel { get; set; } = string.Empty;
}

internal sealed class CompletedWriteEntry
{
    public string ToolName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ResultMessage { get; set; } = string.Empty;

    public string UndoLabel { get; set; } = string.Empty;

    public DateTimeOffset AppliedUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class AssistantRunSnapshot
{
    public string AnswerText { get; set; } = string.Empty;

    public string AnswerFooterText { get; set; } = string.Empty;

    public string PlanText { get; set; } = string.Empty;

    public string ToolsText { get; set; } = string.Empty;

    public string TraceId { get; set; } = string.Empty;

    public string AnswerSource { get; set; } = "deterministic_fallback";

    public string AnswerModelId { get; set; } = string.Empty;

    public string TurnStatus { get; set; } = "ready";

    public ModelUsage RunUsage { get; set; } = new();
}

internal static class HostState
{
    private static readonly object Sync = new();
    private static readonly string UserId = System.Environment.UserName;
    private static ISldWorks? _application;
    private static int _sessionRunCount;
    private static ModelUsage _sessionUsage = new();
    private static CancellationTokenSource? _currentRunCts;
    private static readonly List<ChatEntry> _chatHistory = new();
    private static readonly List<PendingWriteAction> _pendingWrites = new();
    private static readonly List<CompletedWriteEntry> _writeHistory = new();
    private static string? _chatDocumentKey;
    private static LocalHealthResult? _localHealthResult;
    private static volatile bool _localHealthChecked;
    private static IUiThreadInvoker? _uiThreadInvoker;
    private static Action<string>? _quickActionInvoker;
    private static Action? _taskPaneFocusInvoker;
    private static readonly SessionTelemetry _telemetry = new();
    private static string? _probeFailureMessage;
    private static string? _probeFailedStep;
    private static string? _probeRevision;
    // Phase tracker for cancellation diagnostics. Set by run paths as they
    // progress through stages (planning, tools, synthesis, streaming). Read
    // on cancel so host.log shows WHERE the user cancelled.
    private static volatile string _currentRunPhase = "idle";

    // v1.1 sidebar adapter — single instance per add-in session. Lazy-created
    // on first access so the legacy WebBrowser path doesn't allocate it when
    // the NativeSidebar gate is OFF.
    private static HostStateAdapter? _taskPaneHost;

    internal static void SetRunPhase(string phase)
    {
        _currentRunPhase = string.IsNullOrWhiteSpace(phase) ? "unknown" : phase;
    }

    /// <summary>
    /// Returns the singleton <see cref="HostStateAdapter"/> that exposes
    /// HostState as an <see cref="Adze.Contracts.Abstractions.ITaskPaneHost"/>.
    /// Used by the v1.1 native sidebar shim. Lazy — first call creates the
    /// instance; subsequent calls return the same handle.
    /// </summary>
    public static HostStateAdapter GetTaskPaneHost()
    {
        lock (Sync)
        {
            return _taskPaneHost ??= new HostStateAdapter();
        }
    }

    /// <summary>
    /// Notifies the v1.1 sidebar (if active) that observable state changed.
    /// No-op if the adapter has never been created.
    /// </summary>
    internal static void NotifySidebarStateChanged()
    {
        HostStateAdapter? adapter;
        lock (Sync)
        {
            adapter = _taskPaneHost;
        }
        adapter?.RaiseStateChanged();
    }

    public static CancellationTokenSource? CurrentRunCancellation => _currentRunCts;

    /// <summary>
    /// Records the compatibility-probe outcome so the Task Pane can surface a
    /// banner when ribbon/context-menu were skipped. Pass null message on a
    /// healthy probe; pass the failure message on incompatibility.
    /// </summary>
    public static void SetProbeResult(bool isCompatible, string? failedStep, string? message, string? revision)
    {
        lock (Sync)
        {
            _probeFailureMessage = isCompatible ? null : message;
            _probeFailedStep = isCompatible ? null : failedStep;
            _probeRevision = revision;
        }
    }

    /// <summary>Null when probe passed or has not yet run; otherwise the reason.</summary>
    public static (string? Message, string? FailedStep, string? Revision) GetProbeFailure()
    {
        lock (Sync)
        {
            return (_probeFailureMessage, _probeFailedStep, _probeRevision);
        }
    }

    public static void BeginRun()
    {
        lock (Sync)
        {
            _currentRunCts?.Dispose();
            _currentRunCts = new CancellationTokenSource();
        }
    }

    public static void CancelRun()
    {
        string phase;
        lock (Sync)
        {
            phase = _currentRunPhase;
            _currentRunCts?.Cancel();
            _telemetry.RecordCancellation("user");
        }
        FileLogger.Info("Run cancelled by user at phase=" + phase);
    }

    public static void EndRun()
    {
        lock (Sync)
        {
            _currentRunCts?.Dispose();
            _currentRunCts = null;
        }
    }

    public static void SetApplication(ISldWorks? application)
    {
        lock (Sync)
        {
            _application = application;
        }
        // Reset per-SW-session chat file path so the next ConnectToSW starts
        // a fresh session-<timestamp>.jsonl. Disconnect triggers this via null.
        lock (_chatFileLock)
        {
            if (application == null)
            {
                _chatFilePath = null;
            }
        }
    }

    public static ISldWorks? GetApplication()
    {
        lock (Sync)
        {
            return _application;
        }
    }

    public static void SetUiThreadInvoker(IUiThreadInvoker? invoker)
    {
        lock (Sync)
        {
            _uiThreadInvoker = invoker;
        }
    }

    public static IUiThreadInvoker? GetUiThreadInvoker()
    {
        lock (Sync)
        {
            return _uiThreadInvoker;
        }
    }

    /// <summary>
    /// Register a callback that fires a QuickAction in the Task Pane UI.
    /// Called by TaskPaneControl during initialization; invoked by ribbon/context-menu
    /// handlers to route user intent into the same prompt composer the Task Pane uses.
    /// </summary>
    public static void SetQuickActionInvoker(Action<string>? invoker)
    {
        lock (Sync)
        {
            _quickActionInvoker = invoker;
        }
    }

    public static void InvokeQuickAction(string actionKey)
    {
        Action<string>? invoker;
        lock (Sync)
        {
            invoker = _quickActionInvoker;
        }

        FileLogger.Info("Entry: quick-action invoked key=" + (actionKey ?? "<null>"));
        invoker?.Invoke(actionKey ?? string.Empty);
    }

    /// <summary>
    /// Register a callback that brings the Task Pane into focus (used by the
    /// ribbon "Ask" button when no prompt is supplied).
    /// </summary>
    public static void SetTaskPaneFocusInvoker(Action? invoker)
    {
        lock (Sync)
        {
            _taskPaneFocusInvoker = invoker;
        }
    }

    public static void InvokeTaskPaneFocus()
    {
        Action? invoker;
        lock (Sync)
        {
            invoker = _taskPaneFocusInvoker;
        }

        invoker?.Invoke();
    }

    /// <summary>
    /// Direct-action ribbon entry point for STL → SOLIDWORKS part import.
    /// Bypasses the chat sidebar entirely: opens a file picker, runs the
    /// in-process mesh analyzer, surfaces a native confirmation dialog with
    /// the analysis summary + advisories, and on approval calls
    /// <see cref="ImportMeshTool.Apply"/> on the SW UI thread. This is the
    /// canonical example of a Phase 11 "embedded AI" surface — AI-assisted
    /// CAD work invoked directly from the ribbon, no chat panel required.
    ///
    /// Must be called on the SW UI thread (ribbon callbacks already are).
    /// Gated by <see cref="FeatureGateRegistry.MeshImport"/>; if disabled, a
    /// short message points the user at Settings → Feature Gates.
    /// </summary>
    public static void InvokeMeshImport()
    {
        try
        {
            FileLogger.Info("Entry: mesh import ribbon action invoked.");

            if (!FeatureGateRegistry.IsEnabled(FeatureGateRegistry.MeshImport))
            {
                System.Windows.Forms.MessageBox.Show(
                    "Mesh import is currently disabled. Enable it via Settings → Feature Gates → Mesh Import (or set SOLIDWORKS_AI_MESH_IMPORT=true).",
                    "Adze: Import Mesh",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            ISldWorks? application = GetApplicationSnapshot();
            if (application == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    "SOLIDWORKS is not connected.",
                    "Adze: Import Mesh",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }

            string? selectedPath;
            using (var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Adze: select an STL file to import",
                Filter = "STL mesh files (*.stl)|*.stl|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    FileLogger.Info("Mesh import: user cancelled file picker.");
                    return;
                }
                selectedPath = dialog.FileName;
            }

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            // Run the read-only analyzer first so the user sees what they'll
            // get before approving the import.
            var analyzeTool = new Adze.Tools.Mesh.AnalyzeMeshFileTool();
            SessionContext context = BuildContextUnsafe(application);
            ToolResult analysisResult = analyzeTool.Execute(
                context,
                new AnalyzeMeshFileParameters { FilePath = selectedPath });

            string confirmMessage = BuildMeshImportConfirmMessage(selectedPath, analysisResult);
            System.Windows.Forms.DialogResult confirmation = System.Windows.Forms.MessageBox.Show(
                confirmMessage,
                "Adze: Import Mesh — confirm",
                System.Windows.Forms.MessageBoxButtons.OKCancel,
                analysisResult.Success
                    ? System.Windows.Forms.MessageBoxIcon.Question
                    : System.Windows.Forms.MessageBoxIcon.Warning);

            if (confirmation != System.Windows.Forms.DialogResult.OK)
            {
                FileLogger.Info("Mesh import: user cancelled at confirmation dialog.");
                return;
            }

            // Apply via the ImportMeshTool. We're on the SW UI thread already
            // (ribbon callbacks dispatch on UI), so direct call is safe.
            var importTool = new Adze.Tools.Write.ImportMeshTool();
            WriteApplyResult result = importTool.Apply(
                application,
                new ImportMeshParameters { FilePath = selectedPath });

            if (result.Success)
            {
                FileLogger.Info("Mesh import succeeded: " + selectedPath + " (undo=" + result.UndoLabel + ")");
                System.Windows.Forms.MessageBox.Show(
                    "Mesh imported as a graphics body.\n\nUndo label: " + result.UndoLabel
                        + "\n\nNote: this is a non-editable graphics body. Use SOLIDWORKS' built-in 'Convert to Solid Body' or ScanTo3D for further editing.",
                    "Adze: Import Mesh — done",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            else
            {
                FileLogger.Error("Mesh import failed: " + result.ErrorMessage, null);
                System.Windows.Forms.MessageBox.Show(
                    "Mesh import failed.\n\n" + (result.ErrorMessage ?? "(no error message)"),
                    "Adze: Import Mesh — failed",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("InvokeMeshImport threw an unhandled exception.", ex);
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    "Mesh import encountered an error: " + ex.Message,
                    "Adze: Import Mesh — error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
                // Last-ditch: don't let a UI thread failure tear down the host.
            }
        }
    }

    /// <summary>
    /// Direct-action ribbon entry point for "create a drawing of the active
    /// part / assembly". Phase 11 embedded surface — runs <c>create_drawing_document</c>
    /// directly with the active doc as the model, no chat panel involvement.
    /// </summary>
    public static void InvokeAutoDrawing()
    {
        try
        {
            FileLogger.Info("Entry: auto-drawing ribbon action invoked.");

            ISldWorks? application = GetApplicationSnapshot();
            if (application == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    "SOLIDWORKS is not connected.",
                    "Adze: Auto-Drawing",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }

            dynamic? activeDoc = application.ActiveDoc;
            if (activeDoc == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    "No active document. Open a part or assembly first.",
                    "Adze: Auto-Drawing",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            string modelPath;
            try { modelPath = (string)activeDoc.GetPathName(); }
            catch { modelPath = string.Empty; }

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                System.Windows.Forms.MessageBox.Show(
                    "The active document has not been saved. Save the part / assembly before creating a drawing.",
                    "Adze: Auto-Drawing",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            string confirmText = "Create a drawing of '" + System.IO.Path.GetFileName(modelPath)
                + "' with standard views (Front, Top, Right, Isometric)?";
            System.Windows.Forms.DialogResult confirmation = System.Windows.Forms.MessageBox.Show(
                confirmText,
                "Adze: Auto-Drawing — confirm",
                System.Windows.Forms.MessageBoxButtons.OKCancel,
                System.Windows.Forms.MessageBoxIcon.Question);

            if (confirmation != System.Windows.Forms.DialogResult.OK)
            {
                FileLogger.Info("Auto-Drawing: user cancelled at confirmation dialog.");
                return;
            }

            var tool = new Adze.Tools.Write.CreateDrawingDocumentTool();
            WriteApplyResult result = tool.Apply(application, new CreateDrawingDocumentParameters
            {
                ModelPath = modelPath,
                IncludeStandardViews = true
            });

            if (result.Success)
            {
                FileLogger.Info("Auto-Drawing succeeded for " + modelPath + " (undo=" + result.UndoLabel + ").");
                result.AppliedValues.TryGetValue("drawing_title", out string? title);
                result.AppliedValues.TryGetValue("views_added", out string? viewsAdded);
                System.Windows.Forms.MessageBox.Show(
                    "Drawing '" + (title ?? "(unsaved)") + "' created"
                        + (string.IsNullOrEmpty(viewsAdded) ? "." : " with " + viewsAdded + " standard view(s).")
                        + "\n\nUndo label: " + result.UndoLabel,
                    "Adze: Auto-Drawing — done",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            else
            {
                FileLogger.Error("Auto-Drawing failed: " + result.ErrorMessage, null);
                System.Windows.Forms.MessageBox.Show(
                    "Drawing creation failed.\n\n" + (result.ErrorMessage ?? "(no error message)"),
                    "Adze: Auto-Drawing — failed",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("InvokeAutoDrawing threw an unhandled exception.", ex);
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    "Auto-Drawing encountered an error: " + ex.Message,
                    "Adze: Auto-Drawing — error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }

    private static string BuildMeshImportConfirmMessage(string path, ToolResult analysisResult)
    {
        var lines = new List<string>
        {
            "File: " + path,
            string.Empty
        };

        if (!analysisResult.Success)
        {
            lines.Add("Analysis failed: " + analysisResult.Summary);
            lines.Add(string.Empty);
            lines.Add("You can still attempt the import (graphics_body strategy), but proceed with caution.");
            return string.Join(System.Environment.NewLine, lines);
        }

        lines.Add(analysisResult.Summary);

        if (analysisResult.Data.TryGetValue("triangle_count", out object? tc))
            lines.Add("Triangles: " + tc);
        if (analysisResult.Data.TryGetValue("is_closed", out object? closed))
            lines.Add("Closed: " + closed);
        if (analysisResult.Data.TryGetValue("convertibility", out object? conv))
            lines.Add("Convertibility: " + conv);
        if (analysisResult.Data.TryGetValue("recommended_strategy", out object? strat))
            lines.Add("Recommended: " + strat);

        if (analysisResult.Data.TryGetValue("advisories", out object? advObj)
            && advObj is System.Collections.IEnumerable advisories)
        {
            int shown = 0;
            foreach (object? adv in advisories)
            {
                if (adv == null) continue;
                if (shown == 0) lines.Add(string.Empty + "Advisories:");
                lines.Add("  • " + adv);
                if (++shown >= 6)
                {
                    lines.Add("  • …");
                    break;
                }
            }
        }

        lines.Add(string.Empty);
        lines.Add("Import as a SOLIDWORKS part document (graphics body)?");
        return string.Join(System.Environment.NewLine, lines);
    }

    public static string GetTelemetrySummary()
    {
        lock (Sync)
        {
            return _telemetry.FormatSummary();
        }
    }

    public static SessionTelemetry GetTelemetry()
    {
        lock (Sync)
        {
            return _telemetry;
        }
    }

    public static List<ChatEntry> GetChatHistory()
    {
        lock (Sync)
        {
            return new List<ChatEntry>(_chatHistory);
        }
    }

    public static void ClearChatHistory()
    {
        lock (Sync)
        {
            _chatHistory.Clear();
            _chatDocumentKey = null;
        }
    }

    public static List<PendingWriteAction> GetPendingWrites()
    {
        lock (Sync)
        {
            return new List<PendingWriteAction>(_pendingWrites);
        }
    }

    public static List<string> ApplyAllPendingWrites()
    {
        var results = new List<string>();
        List<PendingWriteAction> snapshot;
        lock (Sync)
        {
            snapshot = new List<PendingWriteAction>(_pendingWrites);
            _telemetry.RecordWritePlanBatchApplied();
        }
        int pending = 0;
        foreach (var pw in snapshot) if (!pw.Applied && !pw.Cancelled) pending++;
        FileLogger.Info("Write batch: applying " + pending + " pending action(s)");
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i].Applied || snapshot[i].Cancelled)
                continue;
            string result = ApplyPendingWrite(i);
            results.Add(result);
            if (result.StartsWith("Write failed"))
                break; // stop on first failure
        }
        return results;
    }

    public static void CancelAllPendingWrites()
    {
        int cancelled = 0;
        lock (Sync)
        {
            foreach (var pw in _pendingWrites)
            {
                if (!pw.Applied && !pw.Cancelled)
                {
                    pw.Cancelled = true;
                    pw.ResultMessage = "Cancelled by user.";
                    _telemetry.RecordWriteCancelled();
                    cancelled++;
                }
            }
        }
        if (cancelled > 0)
        {
            FileLogger.Info("Write batch cancel: " + cancelled + " pending write(s) discarded by user");
            NotifySidebarStateChanged();
        }
    }

    public static string ApplyPendingWrite(int index)
    {
        PendingWriteAction? action;
        lock (Sync)
        {
            if (index < 0 || index >= _pendingWrites.Count)
                return "Invalid write action index.";
            action = _pendingWrites[index];
            if (action.Applied || action.Cancelled)
                return "This write action has already been " + (action.Applied ? "applied" : "cancelled") + ".";
        }

        try
        {
            // Apply the write tool directly via COM — marshal to UI thread if invoker is set
            IUiThreadInvoker? invoker = GetUiThreadInvoker();
            string result;
            if (invoker != null)
            {
                result = invoker.Invoke(() => ApplyWriteToolDirect(action));
            }
            else
            {
                result = ApplyWriteToolDirect(action);
            }
            lock (Sync)
            {
                action.Applied = true;
                action.ResultMessage = result;
                _writeHistory.Add(new CompletedWriteEntry
                {
                    ToolName = action.ToolName,
                    Summary = action.Preview?.Summary ?? action.ToolName,
                    ResultMessage = result,
                    UndoLabel = action.UndoLabel,
                    AppliedUtc = DateTimeOffset.UtcNow
                });
                _telemetry.RecordWriteApplied();
            }
            // Write success log — closes the apply loop visible in host.log so operators
            // can see exactly what was modified and the undo label to look for in SW.
            FileLogger.Info(
                "Write " + action.ToolName + " applied: " +
                (action.Preview?.Summary ?? action.ToolName) +
                " undo_label=\"" + (action.UndoLabel ?? "(none)") + "\"" +
                " result=\"" + (result ?? string.Empty) + "\"");
            NotifySidebarStateChanged();
            return result;
        }
        catch (Exception ex)
        {
            string error = "Write failed: " + ex.Message;
            lock (Sync)
            {
                action.ResultMessage = error;
                _telemetry.RecordWriteFailed();
            }
            FileLogger.Error("Pending write apply failed.", ex);
            NotifySidebarStateChanged();
            return error;
        }
    }

    public static void CancelPendingWrite(int index)
    {
        string? toolName = null;
        lock (Sync)
        {
            if (index >= 0 && index < _pendingWrites.Count)
            {
                toolName = _pendingWrites[index].ToolName;
                _pendingWrites[index].Cancelled = true;
                _pendingWrites[index].ResultMessage = "Cancelled by user.";
                _telemetry.RecordWriteCancelled();
            }
        }
        if (toolName != null)
        {
            FileLogger.Info("Write " + toolName + " cancelled by user (index=" + index + ")");
            NotifySidebarStateChanged();
        }
    }

    public static List<CompletedWriteEntry> GetWriteHistory()
    {
        lock (Sync)
        {
            return new List<CompletedWriteEntry>(_writeHistory);
        }
    }

    public static void ClearWriteHistory()
    {
        lock (Sync)
        {
            _writeHistory.Clear();
        }
    }

    public static List<RecipeCandidate> GetSuggestedRecipes()
    {
        try
        {
            var all = new List<RecipeCandidate>();
            all.AddRange(AgentRecipeCaptureService.ListPromoted());
            foreach (var candidate in AgentRecipeCaptureService.ListReviewReady())
            {
                if (!all.Exists(r => r.RecipeId == candidate.RecipeId))
                    all.Add(candidate);
            }
            return all;
        }
        catch
        {
            return new List<RecipeCandidate>();
        }
    }

    /// <summary>
    /// Kicks off a background health check for local model endpoints (Ollama/LM Studio).
    /// Only runs once per session. Results are available via GetLocalHealthResult().
    /// </summary>
    public static void RunLocalHealthCheckAsync()
    {
        if (_localHealthChecked) return;
        _localHealthChecked = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var settings = BrokerModelSettings.LoadFromEnvironment();
                if (settings.IsLocalProvider)
                {
                    _localHealthResult = LocalEndpointHealthCheck.Check(settings, timeoutMs: 3000);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Local health check failed.", ex);
            }
        });
    }

    public static LocalHealthResult? GetLocalHealthResult() => _localHealthResult;

    private static string ApplyWriteToolDirect(PendingWriteAction action)
    {
        ISldWorks? application = GetApplicationSnapshot();
        if (application == null)
            return "SOLIDWORKS is not connected.";

        // ImportMesh and CreateDrawingDocument open NEW documents and don't
        // require an existing active doc. Handle them before the active-doc
        // check so they can proceed when SW has no document open.
        if (string.Equals(action.ToolName, Contracts.Tooling.ToolNames.ImportMesh, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyImportMeshDirect(application, action);
        }

        if (string.Equals(action.ToolName, Contracts.Tooling.ToolNames.CreateDrawingDocument, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyCreateDrawingDocumentDirect(application, action);
        }

        dynamic? modelDoc = application.ActiveDoc;
        if (modelDoc == null)
            return "No active document.";

        switch (action.ToolName)
        {
            case Contracts.Tooling.ToolNames.SetCustomProperty:
            {
                string propName = action.Arguments.TryGetValue("property_name", out var pn) ? pn?.ToString() ?? "" : "";
                string propValue = action.Arguments.TryGetValue("property_value", out var pv) ? pv?.ToString() ?? "" : "";
                string scope = action.Arguments.TryGetValue("scope", out var sc) ? sc?.ToString() ?? "document" : "document";

                dynamic propMgr;
                if (string.Equals(scope, "configuration", StringComparison.OrdinalIgnoreCase))
                {
                    string configName = action.Arguments.TryGetValue("configuration_name", out var cn) ? cn?.ToString() ?? "" : "";
                    propMgr = modelDoc.Extension.CustomPropertyManager[configName];
                }
                else
                {
                    propMgr = modelDoc.Extension.CustomPropertyManager[""];
                }

                propMgr.Add3(propName, 30, propValue, 2); // 30=swCustomInfoText, 2=overwrite
                return "Property '" + propName + "' set to '" + propValue + "'.";
            }

            case Contracts.Tooling.ToolNames.SetDimensionValue:
            {
                string dimFullName = action.Arguments.TryGetValue("dimension_full_name", out var df) ? df?.ToString() ?? "" : "";
                double newValue = 0;
                if (action.Arguments.TryGetValue("new_value", out var nv) && nv != null)
                    double.TryParse(nv.ToString(), out newValue);
                string dimConfig = action.Arguments.TryGetValue("configuration_name", out var dc) ? dc?.ToString() ?? "" : "";

                dynamic? dim = modelDoc.Parameter(dimFullName);
                if (dim == null)
                    return "Dimension '" + dimFullName + "' not found.";

                if (!string.IsNullOrWhiteSpace(dimConfig))
                {
                    // Set value for a specific configuration
                    dim.SetSystemValue3(newValue / 1000.0, 1, dimConfig); // 1 = swSetValue_InSpecificConfigurations
                }
                else
                {
                    dim.SystemValue = newValue / 1000.0; // Convert mm to meters
                }
                modelDoc.EditRebuild3();
                string configSuffix = string.IsNullOrWhiteSpace(dimConfig) ? "" : " in configuration '" + dimConfig + "'";
                return "Dimension '" + dimFullName + "' set to " + newValue + " mm" + configSuffix + ".";
            }

            case Contracts.Tooling.ToolNames.SuppressFeature:
            {
                string featureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() ?? "" : "";
                string supConfig = action.Arguments.TryGetValue("configuration_name", out var sc) ? sc?.ToString() ?? "" : "";
                dynamic? feature = modelDoc.FeatureByName(featureName);
                if (feature == null)
                    return "Feature '" + featureName + "' not found.";

                int supConfigOpt = string.IsNullOrWhiteSpace(supConfig) ? 2 : 1;
                string[]? supConfigNames = string.IsNullOrWhiteSpace(supConfig) ? null : new[] { supConfig };
                feature.SetSuppression2(0, supConfigOpt, supConfigNames); // 0=suppressed
                modelDoc.EditRebuild3();
                string supSuffix = string.IsNullOrWhiteSpace(supConfig) ? "" : " in configuration '" + supConfig + "'";
                return "Feature '" + featureName + "' suppressed" + supSuffix + ".";
            }

            case Contracts.Tooling.ToolNames.UnsuppressFeature:
            {
                string featureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() ?? "" : "";
                string unsupConfig = action.Arguments.TryGetValue("configuration_name", out var uc) ? uc?.ToString() ?? "" : "";
                dynamic? feature = modelDoc.FeatureByName(featureName);
                if (feature == null)
                    return "Feature '" + featureName + "' not found.";

                int unsupConfigOpt = string.IsNullOrWhiteSpace(unsupConfig) ? 2 : 1;
                string[]? unsupConfigNames = string.IsNullOrWhiteSpace(unsupConfig) ? null : new[] { unsupConfig };
                feature.SetSuppression2(1, unsupConfigOpt, unsupConfigNames); // 1=resolved
                modelDoc.EditRebuild3();
                string unsupSuffix = string.IsNullOrWhiteSpace(unsupConfig) ? "" : " in configuration '" + unsupConfig + "'";
                return "Feature '" + featureName + "' unsuppressed" + unsupSuffix + ".";
            }

            case Contracts.Tooling.ToolNames.InsertComponent:
            {
                string compPath = action.Arguments.TryGetValue("component_path", out var cp) ? cp?.ToString() ?? "" : "";
                string configName = action.Arguments.TryGetValue("configuration_name", out var cfn) ? cfn?.ToString() ?? "" : "";
                double x = 0, y = 0, z = 0;
                if (action.Arguments.TryGetValue("x", out var xv) && xv != null) double.TryParse(xv.ToString(), out x);
                if (action.Arguments.TryGetValue("y", out var yv) && yv != null) double.TryParse(yv.ToString(), out y);
                if (action.Arguments.TryGetValue("z", out var zv) && zv != null) double.TryParse(zv.ToString(), out z);

                int docType = (int)modelDoc.GetType();
                if (docType != 2)
                    return "Active document is not an assembly.";

                dynamic? component = modelDoc.AddComponent5(
                    compPath, 0, "", false, configName,
                    x / 1000.0, y / 1000.0, z / 1000.0);

                if (component == null)
                    return "Could not insert component. Verify the file path is correct.";

                string compName = "(inserted)";
                try { compName = component.Name2; } catch { }
                return "Component '" + System.IO.Path.GetFileName(compPath) + "' inserted as '" + compName + "'.";
            }

            case Contracts.Tooling.ToolNames.CreateDrawingView:
            {
                string viewType = action.Arguments.TryGetValue("view_type", out var vt) ? vt?.ToString() ?? "front" : "front";
                string modelPath = action.Arguments.TryGetValue("model_path", out var mp) ? mp?.ToString() ?? "" : "";
                double vx = 0.15, vy = 0.15;
                if (action.Arguments.TryGetValue("x", out var vxv) && vxv != null) double.TryParse(vxv.ToString(), out vx);
                if (action.Arguments.TryGetValue("y", out var vyv) && vyv != null) double.TryParse(vyv.ToString(), out vy);

                int docType = (int)modelDoc.GetType();
                if (docType != 3)
                    return "Active document is not a drawing.";

                var viewNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["front"] = "*Front", ["back"] = "*Back", ["top"] = "*Top",
                    ["bottom"] = "*Bottom", ["left"] = "*Left", ["right"] = "*Right",
                    ["isometric"] = "*Isometric", ["trimetric"] = "*Trimetric", ["dimetric"] = "*Dimetric"
                };

                if (!viewNameMap.TryGetValue(viewType, out string? swViewName))
                    return "Unsupported view type: " + viewType;

                dynamic? view = modelDoc.CreateDrawViewFromModelView3(modelPath, swViewName, vx, vy);
                if (view == null)
                    return "Could not create " + viewType + " view. Ensure a model is referenced by the drawing.";

                string viewName = "(created)";
                try { viewName = view.Name; } catch { }
                return viewType + " view created as '" + viewName + "'.";
            }

            case Contracts.Tooling.ToolNames.RenameObject:
            {
                string currentName = action.Arguments.TryGetValue("current_name", out var cn) ? cn?.ToString() ?? "" : "";
                string newName = action.Arguments.TryGetValue("new_name", out var nn) ? nn?.ToString() ?? "" : "";

                dynamic? feature = modelDoc.FeatureByName(currentName);
                if (feature == null)
                    return "Feature '" + currentName + "' not found.";

                feature.Name = newName;
                return "Feature '" + currentName + "' renamed to '" + newName + "'.";
            }

            case Contracts.Tooling.ToolNames.CreateSketch:
                return ApplyCreateSketchDirect(application, action);

            case Contracts.Tooling.ToolNames.AddSketchLine:
                return ApplyAddSketchLineDirect(application, action);

            case Contracts.Tooling.ToolNames.AddSketchCircle:
                return ApplyAddSketchCircleDirect(application, action);

            case Contracts.Tooling.ToolNames.AddSketchDimension:
                return ApplyAddSketchDimensionDirect(application, action);

            case Contracts.Tooling.ToolNames.CreateExtrude:
                return ApplyCreateExtrudeDirect(application, action);

            case Contracts.Tooling.ToolNames.CreateRevolve:
                return ApplyCreateRevolveDirect(application, action);

            case Contracts.Tooling.ToolNames.CreateFillet:
                return ApplyCreateFilletDirect(application, action);

            case Contracts.Tooling.ToolNames.CreateChamfer:
                return ApplyCreateChamferDirect(application, action);

            case Contracts.Tooling.ToolNames.CreatePatternLinear:
                return ApplyCreatePatternLinearDirect(application, action);

            case Contracts.Tooling.ToolNames.CreateReferencePlane:
                return ApplyCreateReferencePlaneDirect(application, action);

            default:
                return "Unknown write tool: " + action.ToolName;
        }
    }

    private static string ApplyImportMeshDirect(ISldWorks application, PendingWriteAction action)
    {
        string filePath = action.Arguments.TryGetValue("file_path", out var fp) ? fp?.ToString() ?? "" : "";
        string strategy = action.Arguments.TryGetValue("strategy", out var st) ? st?.ToString() ?? "graphics_body" : "graphics_body";
        string? sourceUnits = action.Arguments.TryGetValue("source_units", out var su) ? su?.ToString() : null;

        var tool = new Adze.Tools.Write.ImportMeshTool();
        var parameters = new ImportMeshParameters
        {
            FilePath = filePath,
            Strategy = strategy,
            SourceUnits = sourceUnits
        };

        WriteApplyResult applyResult = tool.Apply(application, parameters);
        if (!applyResult.Success)
        {
            return applyResult.ErrorMessage ?? "Mesh import failed.";
        }

        applyResult.AppliedValues.TryGetValue("new_document_title", out string? title);
        return "Mesh imported as '" + (title ?? System.IO.Path.GetFileNameWithoutExtension(filePath)) + "'.";
    }

    private static string ApplyCreateDrawingDocumentDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreateDrawingDocumentParameters
        {
            ModelPath = action.Arguments.TryGetValue("model_path", out var mp) ? mp?.ToString() : null,
            TemplatePath = action.Arguments.TryGetValue("template_path", out var tp) ? tp?.ToString() : null,
            IncludeStandardViews = !action.Arguments.TryGetValue("include_standard_views", out var iv)
                || iv == null
                || (bool.TryParse(iv.ToString(), out bool include) && include)
        };

        var tool = new Adze.Tools.Write.CreateDrawingDocumentTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success)
        {
            return result.ErrorMessage ?? "Create drawing failed.";
        }
        result.AppliedValues.TryGetValue("drawing_title", out string? title);
        result.AppliedValues.TryGetValue("views_added", out string? viewCount);
        return "Drawing '" + (title ?? "(unsaved)") + "' created"
            + (string.IsNullOrEmpty(viewCount) ? "." : " with " + viewCount + " standard view(s).");
    }

    private static string ApplyCreateSketchDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreateSketchParameters
        {
            ReferenceName = action.Arguments.TryGetValue("reference_name", out var rn) ? rn?.ToString() ?? "Front Plane" : "Front Plane",
            SketchName = action.Arguments.TryGetValue("sketch_name", out var sn) ? sn?.ToString() : null
        };

        var tool = new Adze.Tools.Write.CreateSketchTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success) return result.ErrorMessage ?? "Create sketch failed.";
        result.AppliedValues.TryGetValue("sketch_name", out string? name);
        return "Sketch '" + (name ?? "(new)") + "' created on '" + parameters.ReferenceName + "'.";
    }

    private static string ApplyCreateExtrudeDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreateExtrudeParameters
        {
            SketchName = action.Arguments.TryGetValue("sketch_name", out var sn) ? sn?.ToString() : null,
            DepthMm = action.Arguments.TryGetValue("depth_mm", out var dm) && dm != null
                && double.TryParse(dm.ToString(), out double depth) ? depth : 10.0,
            EndCondition = action.Arguments.TryGetValue("end_condition", out var ec) ? ec?.ToString() ?? "blind" : "blind",
            Reverse = action.Arguments.TryGetValue("reverse", out var rv) && rv != null
                && bool.TryParse(rv.ToString(), out bool reverse) && reverse,
            FeatureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() : null
        };

        var tool = new Adze.Tools.Write.CreateExtrudeTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success) return result.ErrorMessage ?? "Create extrude failed.";
        result.AppliedValues.TryGetValue("feature_name", out string? name);
        return "Extruded feature '" + (name ?? "(new)") + "' created ("
            + parameters.EndCondition + ", " + parameters.DepthMm + " mm).";
    }

    private static string ApplyCreateRevolveDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreateRevolveParameters
        {
            SketchName = action.Arguments.TryGetValue("sketch_name", out var sn) ? sn?.ToString() : null,
            AxisName = action.Arguments.TryGetValue("axis_name", out var ax) ? ax?.ToString() ?? "" : "",
            AngleDegrees = action.Arguments.TryGetValue("angle_degrees", out var ad) && ad != null
                && double.TryParse(ad.ToString(), out double angle) ? angle : 360.0,
            FeatureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() : null
        };

        var tool = new Adze.Tools.Write.CreateRevolveTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success) return result.ErrorMessage ?? "Create revolve failed.";
        result.AppliedValues.TryGetValue("feature_name", out string? name);
        return "Revolved feature '" + (name ?? "(new)") + "' created around '" + parameters.AxisName + "'.";
    }

    private static string ApplyCreateFilletDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreateFilletParameters
        {
            EntityNames = ReadStringListArg(action, "entity_names"),
            RadiusMm = ReadDoubleArg(action, "radius_mm"),
            FeatureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() : null
        };

        var tool = new Adze.Tools.Write.CreateFilletTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success) return result.ErrorMessage ?? "Create fillet failed.";
        result.AppliedValues.TryGetValue("feature_name", out string? name);
        return "Fillet '" + (name ?? "(new)") + "' created (r=" + parameters.RadiusMm + " mm).";
    }

    private static string ApplyCreateChamferDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreateChamferParameters
        {
            EntityNames = ReadStringListArg(action, "entity_names"),
            DistanceMm = ReadDoubleArg(action, "distance_mm"),
            AngleDegrees = action.Arguments.TryGetValue("angle_degrees", out var ad) && ad != null
                && double.TryParse(ad.ToString(), out double angle) ? angle : 45.0,
            FeatureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() : null
        };

        var tool = new Adze.Tools.Write.CreateChamferTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success) return result.ErrorMessage ?? "Create chamfer failed.";
        result.AppliedValues.TryGetValue("feature_name", out string? name);
        return "Chamfer '" + (name ?? "(new)") + "' created ("
            + parameters.DistanceMm + " mm × " + parameters.AngleDegrees + "°).";
    }

    private static string ApplyCreatePatternLinearDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreatePatternLinearParameters
        {
            FeatureName = action.Arguments.TryGetValue("feature_name", out var fname) ? fname?.ToString() ?? "" : "",
            Direction1Reference = action.Arguments.TryGetValue("direction_1_reference", out var d1) ? d1?.ToString() ?? "" : "",
            Count1 = action.Arguments.TryGetValue("count_1", out var c1) && c1 != null
                && int.TryParse(c1.ToString(), out int count1) ? count1 : 2,
            Spacing1Mm = ReadDoubleArg(action, "spacing_1_mm"),
            FlipDirection1 = action.Arguments.TryGetValue("flip_direction_1", out var f1) && f1 != null
                && bool.TryParse(f1.ToString(), out bool flip1) && flip1,
            Direction2Reference = action.Arguments.TryGetValue("direction_2_reference", out var d2) ? d2?.ToString() : null,
            Count2 = action.Arguments.TryGetValue("count_2", out var c2) && c2 != null
                && int.TryParse(c2.ToString(), out int count2) ? count2 : 1,
            Spacing2Mm = action.Arguments.TryGetValue("spacing_2_mm", out var s2) && s2 != null
                && double.TryParse(s2.ToString(), out double spacing2) ? spacing2 : 10.0,
            FlipDirection2 = action.Arguments.TryGetValue("flip_direction_2", out var f2) && f2 != null
                && bool.TryParse(f2.ToString(), out bool flip2) && flip2,
            PatternName = action.Arguments.TryGetValue("pattern_name", out var pn) ? pn?.ToString() : null
        };

        var tool = new Adze.Tools.Write.CreatePatternLinearTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success) return result.ErrorMessage ?? "Create linear pattern failed.";
        result.AppliedValues.TryGetValue("feature_name", out string? name);
        return "Linear pattern '" + (name ?? "(new)") + "' of '" + parameters.FeatureName + "' created ("
            + parameters.Count1 + "×).";
    }

    private static string ApplyCreateReferencePlaneDirect(ISldWorks application, PendingWriteAction action)
    {
        var parameters = new CreateReferencePlaneParameters
        {
            BaseReference = action.Arguments.TryGetValue("base_reference", out var br) ? br?.ToString() ?? "" : "",
            OffsetMm = ReadDoubleArg(action, "offset_mm"),
            Flip = action.Arguments.TryGetValue("flip", out var fl) && fl != null
                && bool.TryParse(fl.ToString(), out bool flip) && flip,
            PlaneName = action.Arguments.TryGetValue("plane_name", out var pn) ? pn?.ToString() : null
        };

        var tool = new Adze.Tools.Write.CreateReferencePlaneTool();
        WriteApplyResult result = tool.Apply(application, parameters);
        if (!result.Success) return result.ErrorMessage ?? "Create reference plane failed.";
        result.AppliedValues.TryGetValue("feature_name", out string? name);
        return "Reference plane '" + (name ?? "(new)") + "' created offset "
            + parameters.OffsetMm + " mm from '" + parameters.BaseReference + "'.";
    }

    private static System.Collections.Generic.List<string> ReadStringListArg(PendingWriteAction action, string key)
    {
        var list = new System.Collections.Generic.List<string>();
        if (!action.Arguments.TryGetValue(key, out object? raw) || raw == null)
            return list;

        if (raw is System.Collections.IEnumerable enumerable && raw is not string)
        {
            foreach (object? item in enumerable)
            {
                string? text = item?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    list.Add(text!);
            }
            return list;
        }

        if (raw is string single && !string.IsNullOrWhiteSpace(single))
            list.Add(single);
        return list;
    }

    private static string ApplyAddSketchLineDirect(ISldWorks application, PendingWriteAction action)
    {
        var p = new AddSketchLineParameters
        {
            SketchName = action.Arguments.TryGetValue("sketch_name", out var sn) ? sn?.ToString() : null,
            StartXMm = ReadDoubleArg(action, "start_x_mm"),
            StartYMm = ReadDoubleArg(action, "start_y_mm"),
            EndXMm = ReadDoubleArg(action, "end_x_mm"),
            EndYMm = ReadDoubleArg(action, "end_y_mm")
        };

        var tool = new Adze.Tools.Write.AddSketchLineTool();
        WriteApplyResult result = tool.Apply(application, p);
        if (!result.Success) return result.ErrorMessage ?? "Add sketch line failed.";
        return "Line added to " + (p.SketchName ?? "active sketch") + ".";
    }

    private static string ApplyAddSketchCircleDirect(ISldWorks application, PendingWriteAction action)
    {
        var p = new AddSketchCircleParameters
        {
            SketchName = action.Arguments.TryGetValue("sketch_name", out var sn) ? sn?.ToString() : null,
            CenterXMm = ReadDoubleArg(action, "center_x_mm"),
            CenterYMm = ReadDoubleArg(action, "center_y_mm"),
            RadiusMm = ReadDoubleArg(action, "radius_mm")
        };

        var tool = new Adze.Tools.Write.AddSketchCircleTool();
        WriteApplyResult result = tool.Apply(application, p);
        if (!result.Success) return result.ErrorMessage ?? "Add sketch circle failed.";
        return "Circle (r=" + p.RadiusMm + " mm) added to " + (p.SketchName ?? "active sketch") + ".";
    }

    private static string ApplyAddSketchDimensionDirect(ISldWorks application, PendingWriteAction action)
    {
        var p = new AddSketchDimensionParameters
        {
            SketchName = action.Arguments.TryGetValue("sketch_name", out var sn) ? sn?.ToString() : null,
            EntityName = action.Arguments.TryGetValue("entity_name", out var en) ? en?.ToString() ?? "" : "",
            DimensionType = action.Arguments.TryGetValue("dimension_type", out var dt) ? dt?.ToString() ?? "length" : "length",
            TargetValue = ReadDoubleArg(action, "target_value")
        };

        var tool = new Adze.Tools.Write.AddSketchDimensionTool();
        WriteApplyResult result = tool.Apply(application, p);
        if (!result.Success) return result.ErrorMessage ?? "Add sketch dimension failed.";
        return "Dimension applied to '" + p.EntityName + "' = " + p.TargetValue + ".";
    }

    private static double ReadDoubleArg(PendingWriteAction action, string key)
    {
        if (!action.Arguments.TryGetValue(key, out var raw) || raw == null) return 0.0;
        return double.TryParse(raw.ToString(), out double value) ? value : 0.0;
    }

    public static (int RunCount, ModelUsage Usage) GetSessionUsage()
    {
        lock (Sync)
        {
            return (_sessionRunCount, new ModelUsage
            {
                PromptTokens = _sessionUsage.PromptTokens,
                CompletionTokens = _sessionUsage.CompletionTokens,
                TotalTokens = _sessionUsage.TotalTokens
            });
        }
    }

    public static BudgetStatus GetBudgetStatus()
    {
        CostBudgetSettings budgetSettings = CostBudgetSettings.LoadFromEnvironment();
        lock (Sync)
        {
            return new BudgetStatus
            {
                SessionTokensUsed = _sessionUsage.TotalTokens,
                SessionTokenLimit = budgetSettings.MaxSessionTokens,
                DailyTokensUsed = _sessionUsage.TotalTokens, // session-scoped for now
                DailyTokenLimit = budgetSettings.MaxDailyTokens
            };
        }
    }

    public static string BuildStatusText()
    {
        ISldWorks? application = GetApplicationSnapshot();
        return BuildStatusTextUnsafe(application);
    }

    public static string BuildGroundingPlanText(string? userRequest)
    {
        ISldWorks? application = GetApplicationSnapshot();
        SessionContext context = BuildContextUnsafe(application);
        return GroundingPlanBuilder.Build(context, userRequest, application != null);
    }

    public static AssistantRunPreparation PrepareAssistantRun(string? userRequest)
    {
        ISldWorks? application = GetApplicationSnapshot();
        SessionContext context = BuildContextUnsafe(application);
        return new AssistantRunPreparation
        {
            Context = context,
            Request = GroundingExecutionService.NormalizeRequest(userRequest),
            IsApplicationConnected = application != null
        };
    }

    public static AssistantRunSnapshot RunAssistant(string? userRequest)
    {
        AssistantRunPreparation preparation = PrepareAssistantRun(userRequest);
        return CompleteAssistantRun(preparation);
    }

    public static AssistantRunSnapshot CompleteAssistantRun(AssistantRunPreparation preparation)
    {
        return CompleteAssistantRun(preparation, null);
    }

    public static AssistantRunSnapshot CompleteAssistantRun(AssistantRunPreparation preparation, Action<string>? onStreamChunk)
    {
        return CompleteAssistantRun(preparation, onStreamChunk, null);
    }

    public static AssistantRunSnapshot CompleteAssistantRun(AssistantRunPreparation preparation, Action<string>? onStreamChunk, Action<AgentProgressUpdate>? onProgress)
    {
        if (preparation == null)
        {
            throw new ArgumentNullException(nameof(preparation));
        }

        return RunAssistantUnsafe(preparation.Context, preparation.Request, preparation.IsApplicationConnected, onStreamChunk, onProgress);
    }

    // Debounce burst snapshots (selection thrashing fires NewSelection/ClearSelection
    // dozens of times per second when user scrubs). Coalesce same-reason snapshots
    // within a 300ms window — first one logs, subsequent ones are silently merged
    // into the existing snapshot state without re-emitting log lines.
    private static string? _lastSnapshotReason;
    private static DateTimeOffset _lastSnapshotTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan _snapshotDebounceWindow = TimeSpan.FromMilliseconds(300);

    public static void LogSnapshot(string reason)
    {
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool suppress;
            lock (Sync)
            {
                suppress = string.Equals(_lastSnapshotReason, reason, StringComparison.Ordinal)
                    && (now - _lastSnapshotTime) < _snapshotDebounceWindow;
                _lastSnapshotReason = reason;
                _lastSnapshotTime = now;
            }

            ISldWorks? application = GetApplicationSnapshot();
            GroundingDashboardSnapshot snapshot = BuildSnapshotUnsafe(application);
            GroundingSnapshotStore.WriteLatest(new GroundingSnapshotRecord
            {
                Reason = reason,
                TimestampUtc = snapshot.Context.Session.TimestampUtc,
                Context = snapshot.Context,
                ToolResults = snapshot.ToolResults,
                AchievementCount = snapshot.AchievementCount,
                ReviewReadyRecipeCount = snapshot.ReviewReadyCandidateCount,
                LatestAchievementTitle = snapshot.LatestAchievementTitle
            });

            RecordedSnapshot recorded = TraceRecorder.RecordGroundingSnapshot(reason, snapshot.ToolResults, UserId);

            // Fold Snapshot + Trace into one log line (was 2-line pair — redundant).
            // Suppress when debouncing — trace still records to disk, just no log noise.
            if (!suppress)
            {
                FileLogger.Info(
                    "Snapshot: reason=" + reason +
                    " tool_count=" + snapshot.ToolResults.Count +
                    " achievements=" + snapshot.AchievementCount +
                    " recipes_review_ready=" + snapshot.ReviewReadyCandidateCount +
                    " trace=" + recorded.TraceEvent.TraceId +
                    " tier=" + recorded.ProgressionState.ToolUnlockTier +
                    " exploration=" + recorded.ProgressionState.ExplorationPercent.ToString("0.0"));
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("Snapshot logging failed.", ex);
        }
    }

    private static string BuildStatusTextUnsafe(ISldWorks? application)
    {
        GroundingDashboardSnapshot snapshot = BuildSnapshotUnsafe(application);
        return snapshot.Text;
    }

    private static AssistantRunSnapshot RunAssistantUnsafe(SessionContext context, string request, bool isApplicationConnected)
    {
        return RunAssistantUnsafe(context, request, isApplicationConnected, null);
    }

    private static AssistantRunSnapshot RunAssistantUnsafe(SessionContext context, string request, bool isApplicationConnected, Action<string>? onStreamChunk)
    {
        return RunAssistantUnsafe(context, request, isApplicationConnected, onStreamChunk, null);
    }

    private static AssistantRunSnapshot RunAssistantUnsafe(SessionContext context, string request, bool isApplicationConnected, Action<string>? onStreamChunk, Action<AgentProgressUpdate>? onProgress)
    {
        // User query prefix — log a preview before dispatching. Partner-facing diagnostic:
        // reviewers reading host.log see what was asked without opening per-trace files.
        // Truncated at 500 chars to avoid flooding; full prompt still lands in the
        // trace file's intent field and in the chat-history JSONL on this turn.
        string preview = request ?? string.Empty;
        if (preview.Length > 500) preview = preview.Substring(0, 500) + "…";
        // Collapse newlines for single-line log format
        preview = preview.Replace('\r', ' ').Replace('\n', ' ').Trim();
        FileLogger.Info("UserQuery: \"" + preview + "\" (length=" + (request?.Length ?? 0) + ")");

        // Clear chat history if the active document changed
        string currentDocKey = context.Document?.Path ?? string.Empty;
        lock (Sync)
        {
            if (!string.Equals(_chatDocumentKey, currentDocKey, StringComparison.OrdinalIgnoreCase))
            {
                _chatHistory.Clear();
                _chatDocumentKey = currentDocKey;
            }
        }

        AssistantRunSnapshot snapshot;

        // Try the agentic tool loop if enabled
        if (isApplicationConnected && AgentModelClientFactory.IsAgentLoopEnabled())
        {
            BrokerModelSettings brokerSettings = BrokerModelSettings.LoadFromEnvironment();
            IAgentModelClient? agentClient = AgentModelClientFactory.Create(brokerSettings);
            if (agentClient != null)
            {
                Action<string>? agentStreamCallback = onStreamChunk != null && FeatureGateRegistry.IsEnabled(FeatureGateRegistry.StreamFinalText)
                    ? onStreamChunk
                    : null;
                snapshot = RunAgenticAssistant(context, request, agentClient, agentStreamCallback, onProgress);
            }
            else
            {
                snapshot = RunClassicAssistant(context, request, isApplicationConnected, onStreamChunk);
            }
        }
        else
        {
            snapshot = RunClassicAssistant(context, request, isApplicationConnected, onStreamChunk);
        }

        // Record in chat history
        ChatEntry entry = new ChatEntry
        {
            UserMessage = request,
            AssistantMessage = snapshot.AnswerText,
            Source = snapshot.AnswerSource,
            Footer = snapshot.AnswerFooterText,
            TimestampUtc = DateTimeOffset.UtcNow
        };
        lock (Sync)
        {
            _chatHistory.Add(entry);
        }

        // v1.1 sidebar — notify after chat-history mutation so the new
        // NativeTaskPaneControl re-renders the conversation thread. No-op
        // when the legacy WebBrowser sidebar is the active surface.
        NotifySidebarStateChanged();

        // Answer preview — response text was previously in-memory only, dying with SW.
        // Log first 500 chars + total length so host.log has a durable record and partner
        // reviewers can verify what the AI actually said without opening SW.
        string answerPreview = snapshot.AnswerText ?? string.Empty;
        int answerLen = answerPreview.Length;
        if (answerPreview.Length > 500) answerPreview = answerPreview.Substring(0, 500) + "…";
        answerPreview = answerPreview.Replace('\r', ' ').Replace('\n', ' ').Trim();
        FileLogger.Info("Answer: \"" + answerPreview + "\" (length=" + answerLen + ")");

        // Per-turn chat history persistence — writes each turn to a JSONL file so the
        // full conversation (prompt + full response text) survives SW close and supports
        // replay / post-session learning. File rotates daily; one file per SW session
        // is possible via WriteChatTurn's session-scoped naming.
        try
        {
            WriteChatTurn(entry, snapshot.TraceId);
        }
        catch (Exception ex)
        {
            FileLogger.Error("Chat history persist failed (continuing).", ex);
        }

        return snapshot;
    }

    private static readonly object _chatFileLock = new();
    private static string? _chatFilePath;

    private static void WriteChatTurn(ChatEntry entry, string? traceId)
    {
        string dir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Adze", "chat");
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        lock (_chatFileLock)
        {
            if (_chatFilePath == null)
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                _chatFilePath = System.IO.Path.Combine(dir, "session-" + stamp + ".jsonl");
            }
        }
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var payload = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["timestamp_utc"] = entry.TimestampUtc.UtcDateTime.ToString("o"),
            ["trace_id"] = traceId ?? string.Empty,
            ["user"] = entry.UserMessage ?? string.Empty,
            ["assistant"] = entry.AssistantMessage ?? string.Empty,
            ["source"] = entry.Source ?? string.Empty,
            ["footer"] = entry.Footer ?? string.Empty
        };
        string line = serializer.Serialize(payload);
        System.IO.File.AppendAllText(_chatFilePath!, line + "\n");
    }

    private static AssistantRunSnapshot RunAgenticAssistant(SessionContext context, string request, IAgentModelClient agentClient, Action<string>? onStreamChunk = null, Action<AgentProgressUpdate>? onProgress = null)
    {
        AgentModelSettings agentSettings = AgentModelSettings.LoadFromEnvironment();
        bool includeRetrieval = FeatureGateRegistry.IsEnabled(FeatureGateRegistry.Retrieval);
        bool includeMesh = FeatureGateRegistry.IsEnabled(FeatureGateRegistry.MeshImport);
        List<AgentToolDefinition> toolDefinitions = AgentModelClientFactory.IsFirstWaveWritesEnabled()
            ? ToolDefinitionBuilder.BuildAllToolDefinitions(includeRetrieval, includeMesh)
            : ToolDefinitionBuilder.BuildReadToolDefinitions();
        if (includeRetrieval && !AgentModelClientFactory.IsFirstWaveWritesEnabled())
            toolDefinitions.AddRange(ToolDefinitionBuilder.BuildRetrievalToolDefinitions());
        if (includeMesh && !AgentModelClientFactory.IsFirstWaveWritesEnabled())
            toolDefinitions.AddRange(ToolDefinitionBuilder.BuildMeshToolDefinitions());
        var toolDispatcher = new AgentToolDispatcher();
        var loopRunner = new AgentLoopRunner();

        var executionContext = new ToolExecutionContext
        {
            SessionId = context.Session.RequestId,
            DocumentKey = context.Document?.Path ?? string.Empty,
            SessionContext = context,
            Application = GetApplicationSnapshot()
        };

        CancellationToken ct;
        lock (Sync)
        {
            ct = _currentRunCts?.Token ?? CancellationToken.None;
        }

        // Wrap the dispatcher to inject context and track writes
        var contextAwareExecutor = new ContextAwareToolExecutor(toolDispatcher, executionContext);
        var writeTracker = new WriteTrackingToolExecutor(contextAwareExecutor);

        // Extract diagnostic intent from clarification prefix
        string? clarificationIntent = KeywordBrokerOrchestrator.ExtractClarificationIntent(request);
        string? detectedIntent = null;
        if (string.Equals(clarificationIntent, "diagnose", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clarificationIntent, "diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            detectedIntent = "diagnostics_review";
        }
        string systemPrompt = ContextPromptComposer.BuildAgentSystemPrompt(detectedIntent);

        // Build multi-turn context from chat history
        List<object>? priorConversation = BuildPriorConversation(agentClient);

        AgentLoopResult result = loopRunner.Run(
            agentClient,
            writeTracker,
            systemPrompt,
            request,
            toolDefinitions,
            agentSettings,
            ct,
            progress =>
            {
                FileLogger.Info("Agent: " + progress.Kind + " — " + progress.Message);
                onProgress?.Invoke(progress);
            },
            priorConversation,
            onStreamChunk);

        string intent = "agent_run: " + request;
        RecordedSnapshot recorded = TraceRecorder.RecordGroundingSnapshot(intent, new List<Contracts.Models.ToolResult>(), UserId);

        ModelUsage runUsage = result.AggregateUsage ?? new ModelUsage();
        lock (Sync)
        {
            _sessionRunCount++;
            _sessionUsage = _sessionUsage + runUsage;

            // Telemetry: record agentic run
            _telemetry.RecordAgenticRun();
            _telemetry.RecordRunOutcome(result.Outcome);
            foreach (var tool in result.ExecutedTools)
            {
                _telemetry.RecordToolCall(tool.ToolName, tool.IsError);
            }
        }

        // Tools Log event — parity with classic path. One INFO line per agent-executed
        // tool so both code paths produce equivalent diagnostic detail in host.log.
        foreach (var tool in result.ExecutedTools)
        {
            string outcome = tool.IsError ? "fail" : "ok";
            FileLogger.Info(
                "Tool " + tool.ToolName + " " + outcome + " (agent) ");
        }

        lock (Sync)
        {
            if (writeTracker.CapturedWrites.Count > 0)
            {
                for (int w = 0; w < writeTracker.CapturedWrites.Count; w++)
                    _telemetry.RecordWriteProposed();
            }
        }

        string answerText;
        if (!string.IsNullOrWhiteSpace(result.FinalAnswer))
        {
            answerText = result.FinalAnswer;
        }
        else
        {
            answerText = FormatAgentOutcomeMessage(result);
        }

        string source = FormatAnswerSourceForUser(result.Outcome == AgentRunOutcome.Success ? "agent_loop" : "agent_fallback");
        if (BrokerModelSettings.LoadFromEnvironment().IsLocalProvider)
        {
            source += " [Experimental]";
        }
        string usageText = runUsage.TotalTokens > 0
            ? "    Tokens: " + runUsage.TotalTokens + " (prompt=" + runUsage.PromptTokens + " completion=" + runUsage.CompletionTokens + ")"
            : string.Empty;

        string toolsSummary = result.ExecutedTools.Count > 0
            ? string.Join(", ", result.ExecutedTools.ConvertAll(t => t.ToolName))
            : "none";

        string failureInfo = !string.IsNullOrWhiteSpace(result.FailureReason)
            ? " reason=" + result.FailureReason
            : string.Empty;

        // Capture any write previews as pending actions
        if (writeTracker.CapturedWrites.Count > 0)
        {
            int firstPendingIndex;
            lock (Sync)
            {
                _pendingWrites.Clear();
                _pendingWrites.AddRange(writeTracker.CapturedWrites);
                firstPendingIndex = _pendingWrites.Count > 0 ? 0 : -1;
            }

            // v1.1 sidebar — notify after pending writes change so the new
            // NativeTaskPaneControl renders confirmation cards immediately.
            NotifySidebarStateChanged();

            FileLogger.Info("Captured " + writeTracker.CapturedWrites.Count + " pending write action(s) for confirmation.");

            // Feature-gated PMP path: if enabled and supported, surface the
            // first pending write in a native PropertyManager Page. Must be
            // invoked on the UI thread — marshal via the existing invoker.
            if (firstPendingIndex >= 0 && Adze.Host.UI.PropertyManagerPageBroker.IsEnabled)
            {
                IUiThreadInvoker? invoker = GetUiThreadInvoker();
                if (invoker != null)
                {
                    invoker.Invoke(() =>
                    {
                        try
                        {
                            Adze.Host.UI.PropertyManagerPageBroker.TryShow(firstPendingIndex);
                        }
                        catch (Exception ex)
                        {
                            FileLogger.Error("PMP trigger failed; Task Pane card remains available.", ex);
                        }
                    });
                }
            }
        }

        FileLogger.Info(
            "Agent run completed. outcome=" + result.Outcome +
            " tools=" + toolsSummary +
            " tokens=" + runUsage.TotalTokens +
            " pending_writes=" + writeTracker.CapturedWrites.Count +
            failureInfo);

        return new AssistantRunSnapshot
        {
            AnswerText = answerText,
            AnswerFooterText = "Answer source: " + source + usageText + "    Trace ID: " + recorded.TraceEvent.TraceId,
            PlanText = "Agent loop: " + result.Outcome + " | Tools executed: " + result.ExecutedTools.Count + " | " + toolsSummary,
            ToolsText = BuildAgentToolsText(result.ExecutedTools),
            TraceId = recorded.TraceEvent.TraceId,
            AnswerSource = source,
            AnswerModelId = string.Empty,
            TurnStatus = result.Outcome == AgentRunOutcome.Success ? "ready" : "attention_needed",
            RunUsage = runUsage
        };
    }

    /// <summary>
    /// Maps an internal answer-source identifier to a user-facing label for the footer.
    /// Suffixes like " (modelId)" and " [Experimental]" are appended by callers after this transform.
    /// </summary>
    internal static string FormatAnswerSourceForUser(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Unknown";
        return source switch
        {
            "deterministic_fallback" => "Built-in broker",
            "model_openai" => "OpenAI",
            "model_anthropic" => "Anthropic",
            "model_openrouter" => "OpenRouter",
            "agent_loop" => "AI agent",
            "agent_fallback" => "AI agent (fallback)",
            _ => source
        };
    }

    private static string FormatAgentOutcomeMessage(AgentLoopResult result)
    {
        switch (result.Outcome)
        {
            case AgentRunOutcome.Cancelled:
                return "Run cancelled.";
            case AgentRunOutcome.Failed:
                if (!string.IsNullOrWhiteSpace(result.FailureReason))
                {
                    if (result.FailureReason.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        result.FailureReason.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "Rate limited by the AI provider. Try again in a few seconds.";
                    if (result.FailureReason.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "The AI provider took too long to respond. Try again.";
                    if (result.FailureReason.IndexOf("Max consecutive errors", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "The assistant encountered repeated errors and stopped. Check the Tools Log for details.";
                    return "The assistant could not complete this request. Check the Tools Log for details.";
                }
                return "The assistant could not complete this request.";
            case AgentRunOutcome.FellBack:
                return "The assistant could not produce a complete answer and fell back to a simpler path.";
            case AgentRunOutcome.BlockedByPolicy:
                return "This operation was blocked by the current safety policy.";
            default:
                return "The assistant completed with outcome: " + result.Outcome + ".";
        }
    }

    private static string BuildAgentToolsText(List<AgentToolResult> executedTools)
    {
        if (executedTools.Count == 0)
        {
            return "No tools were executed during this agent run.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Agent tool execution log");
        sb.AppendLine("-----------------------");
        foreach (AgentToolResult tool in executedTools)
        {
            sb.AppendLine(tool.ToolName + " [" + (tool.IsError ? "error" : "ok") + "]");
            if (tool.OutputJson.Length > 500)
            {
                sb.AppendLine(tool.OutputJson.Substring(0, 500) + "...");
            }
            else
            {
                sb.AppendLine(tool.OutputJson);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static AssistantRunSnapshot RunClassicAssistant(SessionContext context, string request, bool isApplicationConnected)
    {
        return RunClassicAssistant(context, request, isApplicationConnected, null);
    }

    private static AssistantRunSnapshot RunClassicAssistant(SessionContext context, string request, bool isApplicationConnected, Action<string>? onStreamChunk)
    {
        GroundingExecutionReport report = GroundingExecutionService.Execute(context, request, isApplicationConnected);
        string intent = "assistant_run: " + report.Request;
        RecordedSnapshot recorded = TraceRecorder.RecordGroundingSnapshot(intent, report.ToolResults, UserId);
        int reviewReadyCandidateCount = RecipeCandidateEngine.CountReviewReadyCandidates();
        AchievementState? latestAchievement = recorded.ProgressionState.Achievements.Count == 0
            ? null
            : recorded.ProgressionState.Achievements[recorded.ProgressionState.Achievements.Count - 1];

        GroundingSnapshotStore.WriteLatest(new GroundingSnapshotRecord
        {
            Reason = intent,
            TimestampUtc = report.ExecutedUtc,
            Context = context,
            ToolResults = report.ToolResults,
            AchievementCount = recorded.ProgressionState.Achievements.Count,
            ReviewReadyRecipeCount = reviewReadyCandidateCount,
            LatestAchievementTitle = latestAchievement?.Title
        });

        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        string contextJson = serializer.Serialize(ModelJsonMapper.ToJson(context));
        string toolResultsJson = serializer.Serialize(report.ToolResults.Select(ModelJsonMapper.ToJson).ToArray());
        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();
        IModelClient? modelClient = settings.IsUsable() ? ModelClientFactory.Create(settings) : null;

        // Use streaming when the feature gate is enabled and a callback is provided
        Action<string>? streamCallback = onStreamChunk != null && FeatureGateRegistry.IsEnabled(FeatureGateRegistry.StreamFinalText)
            ? onStreamChunk
            : null;
        GroundingSynthesisOutcome synthesis = GroundingSynthesisService.Build(report, contextJson, toolResultsJson, modelClient, streamCallback);
        string answerText = synthesis.AnswerText;
        string planText = GroundingPlanBuilder.Build(report);
        string toolsText = GroundingToolResultsBuilder.Build(report.ToolResults);
        if (!string.IsNullOrWhiteSpace(synthesis.FailureReason))
        {
            planText +=
                System.Environment.NewLine +
                System.Environment.NewLine +
                "Answer synthesis fallback: " +
                synthesis.FailureReason;
        }

        string answerSourceText = FormatAnswerSourceForUser(synthesis.Source);
        if (!string.IsNullOrWhiteSpace(synthesis.ModelId))
        {
            answerSourceText += " (" + synthesis.ModelId + ")";
        }
        if (settings.IsLocalProvider)
        {
            answerSourceText += " [Experimental]";
        }

        ModelUsage runUsage = synthesis.Usage ?? new ModelUsage();
        bool classicSuccess = !string.IsNullOrWhiteSpace(synthesis.Source) && synthesis.Source != "deterministic_fallback";
        lock (Sync)
        {
            _sessionRunCount++;
            _sessionUsage = _sessionUsage + runUsage;
            _telemetry.RecordClassicRun(classicSuccess);
        }

        string usageText = runUsage.TotalTokens > 0
            ? "    Tokens: " + runUsage.TotalTokens + " (prompt=" + runUsage.PromptTokens + " completion=" + runUsage.CompletionTokens + ")"
            : string.Empty;

        string synthesisReason = !string.IsNullOrWhiteSpace(synthesis.FailureReason)
            ? " synthesis_reason=\"" + synthesis.FailureReason.Replace("\"", "'") + "\""
            : string.Empty;
        FileLogger.Info(
            "Assistant run completed. trace=" +
            recorded.TraceEvent.TraceId +
            " answer_source=" +
            answerSourceText +
            " tokens=" +
            runUsage.TotalTokens +
            " tool_count=" +
            report.ToolResults.Count +
            " synthesis_fallback=" +
            (!string.IsNullOrWhiteSpace(synthesis.FailureReason)).ToString() +
            synthesisReason);

        // Stats event — richer completion summary mirroring the Task Pane Status section.
        // Emits budget % (best effort) and pending-writes count alongside the base summary.
        FileLogger.Info(
            "Stats: run=" + _sessionRunCount +
            " session_tokens=" + _sessionUsage.TotalTokens +
            " (prompt=" + _sessionUsage.PromptTokens +
            " completion=" + _sessionUsage.CompletionTokens + ")" +
            " pending_writes=" + _pendingWrites.Count);

        return new AssistantRunSnapshot
        {
            AnswerText = answerText,
            AnswerFooterText =
                "Answer source: " +
                answerSourceText +
                usageText +
                "    Trace ID: " +
                recorded.TraceEvent.TraceId,
            PlanText = planText,
            ToolsText = toolsText,
            TraceId = recorded.TraceEvent.TraceId,
            AnswerSource = synthesis.Source,
            AnswerModelId = synthesis.ModelId,
            TurnStatus = report.Response.TurnStatus,
            RunUsage = runUsage
        };
    }

    /// <summary>
    /// Converts recent chat history into OpenAI-format message objects for multi-turn context.
    /// Uses ConversationTruncator to keep within token budget.
    /// </summary>
    private static List<object>? BuildPriorConversation(IAgentModelClient agentClient)
    {
        List<ChatEntry> history;
        lock (Sync)
        {
            if (_chatHistory.Count == 0) return null;
            history = new List<ChatEntry>(_chatHistory);
        }

        // Convert to ConversationMessage for truncation
        var state = new AgentConversationState
        {
            SessionId = "current",
            DocumentKey = _chatDocumentKey ?? string.Empty
        };

        foreach (ChatEntry entry in history)
        {
            state.Messages.Add(new ConversationMessage
            {
                Role = ConversationRole.User,
                Text = entry.UserMessage,
                TimestampUtc = entry.TimestampUtc
            });
            state.Messages.Add(new ConversationMessage
            {
                Role = ConversationRole.Assistant,
                Text = entry.AssistantMessage,
                TimestampUtc = entry.TimestampUtc
            });
        }

        // Truncate to keep context manageable (max 20 messages = ~10 turns)
        var policy = new TruncationPolicy
        {
            ProtectSystemMessage = false,
            ProtectInitialUserIntent = true,
            ProtectedRecentTurns = 6
        };
        AgentConversationState truncated = ConversationTruncator.Truncate(state, 20, policy);

        if (truncated.Messages.Count == 0) return null;

        // Convert to OpenAI-format message objects
        var messages = new List<object>();
        foreach (ConversationMessage msg in truncated.Messages)
        {
            string role = msg.Role == ConversationRole.User ? "user" : "assistant";
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = msg.Text
            });
        }

        return messages;
    }

    private sealed class ContextAwareToolExecutor : IToolExecutor
    {
        private readonly IToolExecutor _inner;
        private readonly ToolExecutionContext _context;

        public ContextAwareToolExecutor(IToolExecutor inner, ToolExecutionContext context)
        {
            _inner = inner;
            _context = context;
        }

        public AgentToolResult Execute(string toolName, System.Collections.Generic.Dictionary<string, object?> arguments, ToolExecutionContext context)
        {
            return _inner.Execute(toolName, arguments, _context);
        }
    }

    private sealed class WriteTrackingToolExecutor : IToolExecutor
    {
        private static readonly HashSet<string> WriteToolNames = new(StringComparer.OrdinalIgnoreCase)
        {
            Contracts.Tooling.ToolNames.SetCustomProperty,
            Contracts.Tooling.ToolNames.SetDimensionValue,
            Contracts.Tooling.ToolNames.SuppressFeature,
            Contracts.Tooling.ToolNames.UnsuppressFeature,
            Contracts.Tooling.ToolNames.RenameObject,
            Contracts.Tooling.ToolNames.InsertComponent,
            Contracts.Tooling.ToolNames.CreateDrawingView,
            Contracts.Tooling.ToolNames.ImportMesh,
            Contracts.Tooling.ToolNames.CreateSketch,
            Contracts.Tooling.ToolNames.AddSketchLine,
            Contracts.Tooling.ToolNames.AddSketchCircle,
            Contracts.Tooling.ToolNames.AddSketchDimension,
            Contracts.Tooling.ToolNames.CreateDrawingDocument,
            Contracts.Tooling.ToolNames.CreateExtrude,
            Contracts.Tooling.ToolNames.CreateRevolve,
            Contracts.Tooling.ToolNames.CreateFillet,
            Contracts.Tooling.ToolNames.CreateChamfer,
            Contracts.Tooling.ToolNames.CreatePatternLinear,
            Contracts.Tooling.ToolNames.CreateReferencePlane
        };

        // Class 3 (HardWriteAdvanced) tools require elevated confirmation
        private static readonly HashSet<string> ElevatedToolNames = new(StringComparer.OrdinalIgnoreCase)
        {
            Contracts.Tooling.ToolNames.InsertComponent,
            Contracts.Tooling.ToolNames.CreateDrawingView,
            Contracts.Tooling.ToolNames.ImportMesh,
            Contracts.Tooling.ToolNames.CreateSketch,
            Contracts.Tooling.ToolNames.CreateDrawingDocument,
            Contracts.Tooling.ToolNames.CreateExtrude,
            Contracts.Tooling.ToolNames.CreateRevolve,
            Contracts.Tooling.ToolNames.CreateFillet,
            Contracts.Tooling.ToolNames.CreateChamfer,
            Contracts.Tooling.ToolNames.CreatePatternLinear,
            Contracts.Tooling.ToolNames.CreateReferencePlane
        };

        private readonly IToolExecutor _inner;
        public List<PendingWriteAction> CapturedWrites { get; } = new();

        public WriteTrackingToolExecutor(IToolExecutor inner)
        {
            _inner = inner;
        }

        public AgentToolResult Execute(string toolName, Dictionary<string, object?> arguments, ToolExecutionContext context)
        {
            AgentToolResult result = _inner.Execute(toolName, arguments, context);

            if (!result.IsError && WriteToolNames.Contains(toolName))
            {
                var (preview, undoLabel) = ExtractPreviewFromResult(result, toolName);
                CapturedWrites.Add(new PendingWriteAction
                {
                    ToolName = toolName,
                    Arguments = new Dictionary<string, object?>(arguments ?? new Dictionary<string, object?>()),
                    Preview = preview,
                    IsElevated = ElevatedToolNames.Contains(toolName),
                    UndoLabel = undoLabel
                });
            }

            return result;
        }

        private static (WritePreview preview, string undoLabel) ExtractPreviewFromResult(AgentToolResult result, string toolName)
        {
            var preview = new WritePreview { ToolName = toolName };
            string undoLabel = string.Empty;
            try
            {
                var serializer = new JavaScriptSerializer();
                var parsed = serializer.Deserialize<Dictionary<string, object>>(result.OutputJson);
                if (parsed != null && parsed.TryGetValue("summary", out object? summary))
                    preview.Summary = summary?.ToString() ?? "";

                if (parsed != null && parsed.TryGetValue("data", out object? dataObj) && dataObj is Dictionary<string, object> data)
                {
                    if (data.TryGetValue("undo_label", out object? undoObj) && undoObj != null)
                        undoLabel = undoObj.ToString() ?? "";

                    if (data.TryGetValue("changes", out object? changesObj) && changesObj is object[] changes)
                    {
                        foreach (var change in changes)
                        {
                            if (change is Dictionary<string, object> changeMap)
                            {
                                preview.Changes.Add(new WriteChangeItem
                                {
                                    TargetLabel = changeMap.TryGetValue("target", out var t) ? t?.ToString() ?? "" : "",
                                    BeforeValue = changeMap.TryGetValue("before", out var b) ? b?.ToString() ?? "" : "",
                                    AfterValue = changeMap.TryGetValue("after", out var a) ? a?.ToString() ?? "" : ""
                                });
                            }
                        }
                    }

                    if (data.TryGetValue("warnings", out object? warningsObj) && warningsObj is object[] warnings)
                    {
                        foreach (var w in warnings)
                        {
                            if (w != null) preview.Warnings.Add(w.ToString() ?? "");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Failed to extract write preview from tool result.", ex);
                preview.Summary = "Write preview (details unavailable)";
            }
            return (preview, undoLabel);
        }
    }

    private static ISldWorks? GetApplicationSnapshot()
    {
        lock (Sync)
        {
            return _application;
        }
    }

    private static GroundingDashboardSnapshot BuildSnapshotUnsafe(ISldWorks? application)
    {
        SessionContext context = BuildContextUnsafe(application);
        ProgressionState progressionState = ProgressionEngine.LoadCurrent(UserId);
        int reviewReadyCandidateCount = RecipeCandidateEngine.CountReviewReadyCandidates();
        return GroundingDashboardBuilder.BuildSnapshot(application != null, context, progressionState, reviewReadyCandidateCount);
    }

    private static SessionContext BuildContextUnsafe(ISldWorks? application)
    {
        SessionContext context = SessionContextBuilder.Build(application);
        ProgressionState progressionState = ProgressionEngine.LoadCurrent(UserId);
        context.Policy.ToolUnlockTier = progressionState.ToolUnlockTier;
        context.Policy.ExplorationPercent = progressionState.ExplorationPercent;
        return context;
    }
}
