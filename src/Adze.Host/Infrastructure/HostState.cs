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

    public static CancellationTokenSource? CurrentRunCancellation => _currentRunCts;

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
        lock (Sync)
        {
            _currentRunCts?.Cancel();
            _telemetry.RecordCancellation("user");
        }
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
        lock (Sync)
        {
            foreach (var pw in _pendingWrites)
            {
                if (!pw.Applied && !pw.Cancelled)
                {
                    pw.Cancelled = true;
                    pw.ResultMessage = "Cancelled by user.";
                    _telemetry.RecordWriteCancelled();
                }
            }
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
            return error;
        }
    }

    public static void CancelPendingWrite(int index)
    {
        lock (Sync)
        {
            if (index >= 0 && index < _pendingWrites.Count)
            {
                _pendingWrites[index].Cancelled = true;
                _pendingWrites[index].ResultMessage = "Cancelled by user.";
                _telemetry.RecordWriteCancelled();
            }
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

            default:
                return "Unknown write tool: " + action.ToolName;
        }
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

    public static void LogSnapshot(string reason)
    {
        try
        {
            ISldWorks? application = GetApplicationSnapshot();
            GroundingDashboardSnapshot snapshot = BuildSnapshotUnsafe(application);

            FileLogger.Info(
                "Snapshot captured. reason=" + reason +
                " tool_count=" + snapshot.ToolResults.Count +
                " achievements=" + snapshot.AchievementCount +
                " review_ready_recipes=" + snapshot.ReviewReadyCandidateCount);
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
            FileLogger.Info(
                "Trace recorded: " +
                recorded.TraceEvent.TraceId +
                " tier=" +
                recorded.ProgressionState.ToolUnlockTier +
                " exploration=" +
                recorded.ProgressionState.ExplorationPercent.ToString("0.0"));
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
        lock (Sync)
        {
            _chatHistory.Add(new ChatEntry
            {
                UserMessage = request,
                AssistantMessage = snapshot.AnswerText,
                Source = snapshot.AnswerSource,
                Footer = snapshot.AnswerFooterText,
                TimestampUtc = DateTimeOffset.UtcNow
            });
        }

        return snapshot;
    }

    private static AssistantRunSnapshot RunAgenticAssistant(SessionContext context, string request, IAgentModelClient agentClient, Action<string>? onStreamChunk = null, Action<AgentProgressUpdate>? onProgress = null)
    {
        AgentModelSettings agentSettings = AgentModelSettings.LoadFromEnvironment();
        bool includeRetrieval = FeatureGateRegistry.IsEnabled(FeatureGateRegistry.Retrieval);
        List<AgentToolDefinition> toolDefinitions = AgentModelClientFactory.IsFirstWaveWritesEnabled()
            ? ToolDefinitionBuilder.BuildAllToolDefinitions(includeRetrieval)
            : ToolDefinitionBuilder.BuildReadToolDefinitions();
        if (includeRetrieval && !AgentModelClientFactory.IsFirstWaveWritesEnabled())
            toolDefinitions.AddRange(ToolDefinitionBuilder.BuildRetrievalToolDefinitions());
        var toolDispatcher = new AgentToolDispatcher();
        var loopRunner = new AgentLoopRunner();

        var executionContext = new ToolExecutionContext
        {
            SessionId = context.Session.RequestId,
            DocumentKey = context.Document?.Path ?? string.Empty,
            SessionContext = context
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

        string source = result.Outcome == AgentRunOutcome.Success ? "agent_loop" : "agent_fallback";
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

        string answerSourceText = synthesis.Source;
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
            (!string.IsNullOrWhiteSpace(synthesis.FailureReason)).ToString());

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
            Contracts.Tooling.ToolNames.CreateDrawingView
        };

        // Class 3 (HardWriteAdvanced) tools require elevated confirmation
        private static readonly HashSet<string> ElevatedToolNames = new(StringComparer.OrdinalIgnoreCase)
        {
            Contracts.Tooling.ToolNames.InsertComponent,
            Contracts.Tooling.ToolNames.CreateDrawingView
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
