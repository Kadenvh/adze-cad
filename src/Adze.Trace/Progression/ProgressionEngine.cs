using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Trace.Recipes;
using Adze.Trace.Serialization;
using Adze.Trace.Storage;

namespace Adze.Trace.Progression;

public sealed class ProgressionUpdate
{
    public ProgressionState State { get; set; } = new();

    public List<string> NewAchievementIds { get; set; } = new();
}

public static class ProgressionEngine
{
    private static readonly string[] ExplorationTools =
    {
        ToolNames.GetActiveDocument,
        ToolNames.GetDocumentSummary,
        ToolNames.GetSelectionContext
    };

    public static ProgressionState LoadCurrent(string userId)
    {
        StatePaths.Ensure();
        ProgressionState state;
        if (!JsonFileStore.TryRead(StatePaths.ProgressionStatePath, out Dictionary<string, object> payload))
        {
            state = CreateDefault(userId);
        }
        else
        {
            state = ModelJsonMapper.ToProgressionState(payload, userId);
        }

        if (RecipeCandidateEngine.CountReviewReadyCandidates() > 0 && state.ToolUnlockTier < ToolUnlockTier.Reviewed)
        {
            state.ToolUnlockTier = ToolUnlockTier.Reviewed;
            JsonFileStore.Write(StatePaths.ProgressionStatePath, ModelJsonMapper.ToJson(state));
        }

        return state;
    }

    public static void ApplyToContext(SessionContext context, string userId)
    {
        ProgressionState state = LoadCurrent(userId);
        context.Policy.ToolUnlockTier = state.ToolUnlockTier;
        context.Policy.ExplorationPercent = state.ExplorationPercent;
    }

    public static ProgressionUpdate Record(TraceEvent traceEvent, IEnumerable<string> successfulTools, bool hasReviewReadyRecipe, string userId)
    {
        ProgressionState state = LoadCurrent(userId);
        var update = new ProgressionUpdate
        {
            State = state
        };

        var successfulToolSet = new HashSet<string>(successfulTools, StringComparer.OrdinalIgnoreCase);
        state.UserId = userId;
        state.UpdatedUtc = traceEvent.TimestampUtc;

        foreach (string toolName in successfulToolSet.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            if (!state.UnlockedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
            {
                state.UnlockedTools.Add(toolName);
            }
        }

        int exploredToolCount = ExplorationTools.Count(toolName => state.UnlockedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase));
        state.ExplorationPercent = Math.Round(exploredToolCount * 100.0 / ExplorationTools.Length, 1);

        if (traceEvent.Result.Status != "failure")
        {
            UnlockAchievement(state, update.NewAchievementIds, "first_trace_logged", "First trace logged", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetActiveDocument))
        {
            UnlockAchievement(state, update.NewAchievementIds, "active_document_grounded", "Active document grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetDocumentSummary))
        {
            UnlockAchievement(state, update.NewAchievementIds, "document_summary_grounded", "Document summary grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetSelectionContext))
        {
            UnlockAchievement(state, update.NewAchievementIds, "selection_context_grounded", "Selection context grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetFeatureTreeSlice))
        {
            UnlockAchievement(state, update.NewAchievementIds, "feature_tree_grounded", "Feature tree grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetDimensions))
        {
            UnlockAchievement(state, update.NewAchievementIds, "dimensions_grounded", "Dimensions grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetConfigurations))
        {
            UnlockAchievement(state, update.NewAchievementIds, "configurations_grounded", "Configurations grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetCustomProperties))
        {
            UnlockAchievement(state, update.NewAchievementIds, "custom_properties_grounded", "Custom properties grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetMates))
        {
            UnlockAchievement(state, update.NewAchievementIds, "mates_grounded", "Assembly mates grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetReferenceGraph))
        {
            UnlockAchievement(state, update.NewAchievementIds, "reference_graph_grounded", "Reference graph grounded", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetRebuildDiagnostics))
        {
            UnlockAchievement(state, update.NewAchievementIds, "diagnostics_grounded", "Diagnostics grounded", traceEvent.TraceId);
        }

        // Write tool achievements
        if (successfulToolSet.Contains(ToolNames.SetCustomProperty))
        {
            UnlockAchievement(state, update.NewAchievementIds, "first_property_write", "First property write", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.SetDimensionValue))
        {
            UnlockAchievement(state, update.NewAchievementIds, "first_dimension_write", "First dimension write", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.SuppressFeature) || successfulToolSet.Contains(ToolNames.UnsuppressFeature))
        {
            UnlockAchievement(state, update.NewAchievementIds, "first_feature_suppression", "First feature suppression toggle", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.SetCustomProperty) &&
            successfulToolSet.Contains(ToolNames.SetDimensionValue) &&
            (successfulToolSet.Contains(ToolNames.SuppressFeature) || successfulToolSet.Contains(ToolNames.UnsuppressFeature)))
        {
            UnlockAchievement(state, update.NewAchievementIds, "first_wave_writes_completed", "All first-wave write tools used", traceEvent.TraceId);
        }

        if (ExplorationTools.All(toolName => successfulToolSet.Contains(toolName)))
        {
            UnlockAchievement(state, update.NewAchievementIds, "grounding_triad_completed", "Grounding triad completed", traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetActiveDocument) &&
            successfulToolSet.Contains(ToolNames.GetDocumentSummary) &&
            successfulToolSet.Contains(ToolNames.GetSelectionContext) &&
            successfulToolSet.Contains(ToolNames.GetFeatureTreeSlice) &&
            successfulToolSet.Contains(ToolNames.GetConfigurations) &&
            successfulToolSet.Contains(ToolNames.GetRebuildDiagnostics))
        {
            UnlockAchievement(
                state,
                update.NewAchievementIds,
                "expanded_grounding_sweep_completed",
                "Expanded grounding sweep completed",
                traceEvent.TraceId);
        }

        if (successfulToolSet.Contains(ToolNames.GetActiveDocument) &&
            successfulToolSet.Contains(ToolNames.GetDocumentSummary) &&
            successfulToolSet.Contains(ToolNames.GetSelectionContext) &&
            successfulToolSet.Contains(ToolNames.GetFeatureTreeSlice) &&
            successfulToolSet.Contains(ToolNames.GetDimensions) &&
            successfulToolSet.Contains(ToolNames.GetConfigurations) &&
            successfulToolSet.Contains(ToolNames.GetCustomProperties) &&
            successfulToolSet.Contains(ToolNames.GetReferenceGraph) &&
            successfulToolSet.Contains(ToolNames.GetRebuildDiagnostics))
        {
            UnlockAchievement(
                state,
                update.NewAchievementIds,
                "wave1_grounding_suite_completed",
                "Wave 1 grounding suite completed",
                traceEvent.TraceId);
        }

        state.ToolUnlockTier = DetermineTier(state, hasReviewReadyRecipe || RecipeCandidateEngine.CountReviewReadyCandidates() > 0);
        JsonFileStore.Write(StatePaths.ProgressionStatePath, ModelJsonMapper.ToJson(state));
        return update;
    }

    private static ProgressionState CreateDefault(string userId)
    {
        return new ProgressionState
        {
            UserId = userId,
            UpdatedUtc = DateTimeOffset.UtcNow,
            ToolUnlockTier = ToolUnlockTier.Baseline,
            ExplorationPercent = 0
        };
    }

    private static void UnlockAchievement(ProgressionState state, ICollection<string> newAchievementIds, string achievementId, string title, string traceId)
    {
        if (state.Achievements.Any(existing => string.Equals(existing.AchievementId, achievementId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        state.Achievements.Add(new AchievementState
        {
            AchievementId = achievementId,
            Title = title,
            UnlockedUtc = DateTimeOffset.UtcNow,
            SourceTraceId = traceId
        });
        newAchievementIds.Add(achievementId);
    }

    private static ToolUnlockTier DetermineTier(ProgressionState state, bool hasReviewReadyRecipe)
    {
        ToolUnlockTier computedTier = state.Achievements.Any(achievement => string.Equals(achievement.AchievementId, "grounding_triad_completed", StringComparison.OrdinalIgnoreCase))
            ? ToolUnlockTier.Assisted
            : ToolUnlockTier.Baseline;

        if (hasReviewReadyRecipe)
        {
            computedTier = ToolUnlockTier.Reviewed;
        }

        // TrustedBounded requires: reviewed tier + first-wave writes completed + reviewed recipe with writes
        if (computedTier >= ToolUnlockTier.Reviewed &&
            state.Achievements.Any(a => string.Equals(a.AchievementId, "first_wave_writes_completed", StringComparison.OrdinalIgnoreCase)))
        {
            computedTier = ToolUnlockTier.TrustedBounded;
        }

        if (state.ToolUnlockTier > computedTier)
        {
            computedTier = state.ToolUnlockTier;
        }

        return computedTier;
    }
}
