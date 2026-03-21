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
}

internal sealed class CompletedWriteEntry
{
    public string ToolName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ResultMessage { get; set; } = string.Empty;

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
            // Apply the write tool directly via COM
            var result = ApplyWriteToolDirect(action);
            lock (Sync)
            {
                action.Applied = true;
                action.ResultMessage = result;
                _writeHistory.Add(new CompletedWriteEntry
                {
                    ToolName = action.ToolName,
                    Summary = action.Preview?.Summary ?? action.ToolName,
                    ResultMessage = result,
                    AppliedUtc = DateTimeOffset.UtcNow
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            string error = "Write failed: " + ex.Message;
            lock (Sync)
            {
                action.ResultMessage = error;
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

                dynamic? dim = modelDoc.Parameter(dimFullName);
                if (dim == null)
                    return "Dimension '" + dimFullName + "' not found.";

                dim.SystemValue = newValue / 1000.0; // Convert mm to meters
                modelDoc.EditRebuild3();
                return "Dimension '" + dimFullName + "' set to " + newValue + " mm.";
            }

            case Contracts.Tooling.ToolNames.SuppressFeature:
            {
                string featureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() ?? "" : "";
                dynamic? feature = modelDoc.FeatureByName(featureName);
                if (feature == null)
                    return "Feature '" + featureName + "' not found.";

                feature.SetSuppression2(0, 2, null); // 0=suppressed, 2=all configs
                modelDoc.EditRebuild3();
                return "Feature '" + featureName + "' suppressed.";
            }

            case Contracts.Tooling.ToolNames.UnsuppressFeature:
            {
                string featureName = action.Arguments.TryGetValue("feature_name", out var fn) ? fn?.ToString() ?? "" : "";
                dynamic? feature = modelDoc.FeatureByName(featureName);
                if (feature == null)
                    return "Feature '" + featureName + "' not found.";

                feature.SetSuppression2(1, 2, null); // 1=resolved, 2=all configs
                modelDoc.EditRebuild3();
                return "Feature '" + featureName + "' unsuppressed.";
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
        if (preparation == null)
        {
            throw new ArgumentNullException(nameof(preparation));
        }

        return RunAssistantUnsafe(preparation.Context, preparation.Request, preparation.IsApplicationConnected);
    }

    public static void LogSnapshot(string reason)
    {
        try
        {
            ISldWorks? application = GetApplicationSnapshot();
            GroundingDashboardSnapshot snapshot = BuildSnapshotUnsafe(application);

            FileLogger.Info(reason + System.Environment.NewLine + snapshot.Text);
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
                snapshot = RunAgenticAssistant(context, request, agentClient);
            }
            else
            {
                snapshot = RunClassicAssistant(context, request, isApplicationConnected);
            }
        }
        else
        {
            snapshot = RunClassicAssistant(context, request, isApplicationConnected);
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

    private static AssistantRunSnapshot RunAgenticAssistant(SessionContext context, string request, IAgentModelClient agentClient)
    {
        AgentModelSettings agentSettings = AgentModelSettings.LoadFromEnvironment();
        List<AgentToolDefinition> toolDefinitions = AgentModelClientFactory.IsFirstWaveWritesEnabled()
            ? ToolDefinitionBuilder.BuildAllToolDefinitions()
            : ToolDefinitionBuilder.BuildReadToolDefinitions();
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
            progress => FileLogger.Info("Agent: " + progress.Kind + " — " + progress.Message),
            priorConversation);

        string intent = "agent_run: " + request;
        RecordedSnapshot recorded = TraceRecorder.RecordGroundingSnapshot(intent, new List<Contracts.Models.ToolResult>(), UserId);

        ModelUsage runUsage = result.AggregateUsage ?? new ModelUsage();
        lock (Sync)
        {
            _sessionRunCount++;
            _sessionUsage = _sessionUsage + runUsage;
        }

        string answerText = !string.IsNullOrWhiteSpace(result.FinalAnswer)
            ? result.FinalAnswer
            : "The agent loop completed with outcome: " + result.Outcome + ".";

        string source = result.Outcome == AgentRunOutcome.Success ? "agent_loop" : "agent_fallback";
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
            lock (Sync)
            {
                _pendingWrites.Clear();
                _pendingWrites.AddRange(writeTracker.CapturedWrites);
            }

            FileLogger.Info("Captured " + writeTracker.CapturedWrites.Count + " pending write action(s) for confirmation.");
        }

        FileLogger.Info(
            "Agent run completed. outcome=" + result.Outcome +
            " tools=" + toolsSummary +
            " tokens=" + runUsage.TotalTokens +
            failureInfo +
            System.Environment.NewLine + answerText);

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
        GroundingSynthesisOutcome synthesis = GroundingSynthesisService.Build(report, contextJson, toolResultsJson, modelClient);
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

        ModelUsage runUsage = synthesis.Usage ?? new ModelUsage();
        lock (Sync)
        {
            _sessionRunCount++;
            _sessionUsage = _sessionUsage + runUsage;
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
            System.Environment.NewLine +
            answerText +
            System.Environment.NewLine +
            planText);

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
            Contracts.Tooling.ToolNames.UnsuppressFeature
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
                var preview = ExtractPreviewFromResult(result, toolName);
                CapturedWrites.Add(new PendingWriteAction
                {
                    ToolName = toolName,
                    Arguments = new Dictionary<string, object?>(arguments ?? new Dictionary<string, object?>()),
                    Preview = preview
                });
            }

            return result;
        }

        private static WritePreview ExtractPreviewFromResult(AgentToolResult result, string toolName)
        {
            var preview = new WritePreview { ToolName = toolName };
            try
            {
                var serializer = new JavaScriptSerializer();
                var parsed = serializer.Deserialize<Dictionary<string, object>>(result.OutputJson);
                if (parsed != null && parsed.TryGetValue("summary", out object? summary))
                    preview.Summary = summary?.ToString() ?? "";

                if (parsed != null && parsed.TryGetValue("data", out object? dataObj) && dataObj is Dictionary<string, object> data)
                {
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
            return preview;
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
