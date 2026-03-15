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
        // Try the agentic tool loop if enabled
        if (isApplicationConnected && AgentModelClientFactory.IsAgentLoopEnabled())
        {
            BrokerModelSettings brokerSettings = BrokerModelSettings.LoadFromEnvironment();
            IAgentModelClient? agentClient = AgentModelClientFactory.Create(brokerSettings);
            if (agentClient != null)
            {
                return RunAgenticAssistant(context, request, agentClient);
            }
        }

        // Fallback to existing two-pass flow
        return RunClassicAssistant(context, request, isApplicationConnected);
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

        // Wrap the dispatcher to inject context
        var contextAwareExecutor = new ContextAwareToolExecutor(toolDispatcher, executionContext);

        string systemPrompt = ContextPromptComposer.BuildAgentSystemPrompt();

        AgentLoopResult result = loopRunner.Run(
            agentClient,
            contextAwareExecutor,
            systemPrompt,
            request,
            toolDefinitions,
            agentSettings,
            ct,
            null);

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

        FileLogger.Info(
            "Agent run completed. outcome=" + result.Outcome +
            " tools=" + toolsSummary +
            " tokens=" + runUsage.TotalTokens +
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
