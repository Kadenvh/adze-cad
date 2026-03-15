using System;
using SolidWorks.Interop.sldworks;
using Adze.Contracts.Models;
using Adze.Host.Services;
using Adze.Trace.Progression;
using Adze.Trace.Recipes;
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
}

internal static class HostState
{
    private static readonly object Sync = new();
    private static readonly string UserId = System.Environment.UserName;
    private static ISldWorks? _application;

    public static void SetApplication(ISldWorks? application)
    {
        lock (Sync)
        {
            _application = application;
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

        GroundingSynthesisOutcome synthesis = GroundingSynthesisService.Build(context, report);
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

        FileLogger.Info(
            "Assistant run completed. trace=" +
            recorded.TraceEvent.TraceId +
            " answer_source=" +
            answerSourceText +
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
                "    Trace ID: " +
                recorded.TraceEvent.TraceId,
            PlanText = planText,
            ToolsText = toolsText,
            TraceId = recorded.TraceEvent.TraceId,
            AnswerSource = synthesis.Source,
            AnswerModelId = synthesis.ModelId
        };
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
