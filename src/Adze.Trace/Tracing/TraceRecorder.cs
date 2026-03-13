using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Trace.Progression;
using Adze.Trace.Recipes;
using Adze.Trace.Serialization;
using Adze.Trace.Storage;

namespace Adze.Trace.Tracing;

public sealed class RecordedSnapshot
{
    public TraceEvent TraceEvent { get; set; } = new();

    public ProgressionState ProgressionState { get; set; } = new();

    public RecipeCandidate? RecipeCandidate { get; set; }
}

public static class TraceRecorder
{
    public static RecordedSnapshot RecordGroundingSnapshot(string intent, IReadOnlyList<ToolResult> toolResults, string userId)
    {
        var traceEvent = new TraceEvent
        {
            TraceId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTimeOffset.UtcNow,
            Intent = intent,
            ApprovalState = ApprovalState.Completed,
            ToolSequence = toolResults.Select(result => result.ToolName).ToList(),
            Result = new TraceResult
            {
                Status = DetermineStatus(toolResults),
                Summary = BuildSummary(toolResults),
                Warnings = toolResults
                    .SelectMany(result => result.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            }
        };

        List<string> successfulTools = toolResults
            .Where(result => result.Success)
            .Select(result => result.ToolName)
            .ToList();

        RecipeCandidate? candidate = RecipeCandidateEngine.UpdateFromTrace(traceEvent);
        ProgressionUpdate update = ProgressionEngine.Record(
            traceEvent,
            successfulTools,
            string.Equals(candidate?.PromotionState, "review_ready", StringComparison.OrdinalIgnoreCase),
            userId);

        traceEvent.AchievementEvents = update.NewAchievementIds;
        traceEvent.ExplorationPercent = update.State.ExplorationPercent;
        traceEvent.ToolUnlockTier = update.State.ToolUnlockTier;

        WriteTrace(traceEvent);

        return new RecordedSnapshot
        {
            TraceEvent = traceEvent,
            ProgressionState = update.State,
            RecipeCandidate = candidate
        };
    }

    private static void WriteTrace(TraceEvent traceEvent)
    {
        StatePaths.Ensure();
        string dayDirectory = Path.Combine(StatePaths.TraceDirectory, traceEvent.TimestampUtc.ToString("yyyyMMdd"));
        string fileName = traceEvent.TimestampUtc.ToString("HHmmss") + "_" + traceEvent.TraceId + ".json";
        JsonFileStore.Write(Path.Combine(dayDirectory, fileName), ModelJsonMapper.ToJson(traceEvent));
    }

    private static string DetermineStatus(IReadOnlyList<ToolResult> toolResults)
    {
        if (toolResults.Count == 0)
        {
            return "failure";
        }

        if (toolResults.All(result => result.Success))
        {
            return "success";
        }

        if (toolResults.Any(result => result.Success))
        {
            return "partial";
        }

        return "failure";
    }

    private static string BuildSummary(IReadOnlyList<ToolResult> toolResults)
    {
        int successCount = toolResults.Count(result => result.Success);
        return "Grounding snapshot recorded. Successful tools: " + successCount + "/" + toolResults.Count + ".";
    }
}
