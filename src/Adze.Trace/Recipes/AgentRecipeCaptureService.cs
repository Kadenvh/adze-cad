using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adze.Contracts.Models;
using Adze.Trace.Serialization;
using Adze.Trace.Storage;

namespace Adze.Trace.Recipes;

public static class AgentRecipeCaptureService
{
    private const int PromotionThreshold = 3;

    /// <summary>
    /// Attempts to create or update a recipe candidate from a successful agent run.
    /// Only captures recipes from runs that completed successfully with at least one tool execution.
    /// Write-tool-containing recipes require verified write success.
    /// </summary>
    public static RecipeCandidate? CaptureFromAgentRun(
        string intent,
        List<string> toolSequence,
        bool allWritesVerified,
        string traceId,
        string userId)
    {
        if (toolSequence.Count == 0)
        {
            return null;
        }

        bool hasWriteTools = toolSequence.Any(IsWriteTool);
        if (hasWriteTools && !allWritesVerified)
        {
            return null;
        }

        StatePaths.Ensure();
        string recipeId = ComputeRecipeId(intent, toolSequence);
        string path = Path.Combine(StatePaths.RecipeDirectory, recipeId + ".json");

        RecipeCandidate candidate = LoadOrCreate(path, recipeId, intent, toolSequence);

        if (!candidate.SourceTraceIds.Contains(traceId, StringComparer.OrdinalIgnoreCase))
        {
            candidate.SourceTraceIds.Add(traceId);
        }

        candidate.ReliabilityScore = Math.Round(Math.Min(1.0, candidate.SourceTraceIds.Count / (double)PromotionThreshold), 2);
        candidate.PromotionState = candidate.SourceTraceIds.Count >= PromotionThreshold ? "review_ready" : "candidate";

        if (hasWriteTools)
        {
            candidate.RequiredUnlockTier = Contracts.Enums.ToolUnlockTier.Reviewed;
            if (!candidate.ReviewNotes.Contains("Contains write tools — requires reviewed trust tier.", StringComparer.OrdinalIgnoreCase))
            {
                candidate.ReviewNotes.Add("Contains write tools — requires reviewed trust tier.");
            }
        }

        if (candidate.PromotionState == "review_ready" &&
            !candidate.ReviewNotes.Contains("Ready for human review after repeated successful traces.", StringComparer.OrdinalIgnoreCase))
        {
            candidate.ReviewNotes.Add("Ready for human review after repeated successful traces.");
        }

        JsonFileStore.Write(path, ModelJsonMapper.ToJson(candidate));
        return candidate;
    }

    /// <summary>
    /// Promotes a review-ready recipe to the promoted recipes directory.
    /// Returns true if promotion succeeded.
    /// </summary>
    public static bool Promote(string recipeId)
    {
        StatePaths.Ensure();
        string candidatePath = Path.Combine(StatePaths.RecipeDirectory, recipeId + ".json");
        if (!JsonFileStore.TryRead(candidatePath, out Dictionary<string, object> payload))
        {
            return false;
        }

        RecipeCandidate candidate = ModelJsonMapper.ToRecipeCandidate(payload, recipeId);
        if (candidate.PromotionState != "review_ready")
        {
            return false;
        }

        candidate.PromotionState = "promoted";
        if (!candidate.ReviewNotes.Contains("Promoted by user review.", StringComparer.OrdinalIgnoreCase))
        {
            candidate.ReviewNotes.Add("Promoted by user review.");
        }

        string promotedDir = Path.Combine(StatePaths.RecipeDirectory, "..", "promoted");
        if (!Directory.Exists(promotedDir))
        {
            Directory.CreateDirectory(promotedDir);
        }

        string promotedPath = Path.Combine(promotedDir, recipeId + ".json");
        JsonFileStore.Write(promotedPath, ModelJsonMapper.ToJson(candidate));

        // Update the candidate file to reflect promotion
        JsonFileStore.Write(candidatePath, ModelJsonMapper.ToJson(candidate));

        return true;
    }

    /// <summary>
    /// Lists all promoted recipes.
    /// </summary>
    public static List<RecipeCandidate> ListPromoted()
    {
        StatePaths.Ensure();
        string promotedDir = Path.Combine(StatePaths.RecipeDirectory, "..", "promoted");
        if (!Directory.Exists(promotedDir))
        {
            return new List<RecipeCandidate>();
        }

        var results = new List<RecipeCandidate>();
        foreach (string path in Directory.GetFiles(promotedDir, "*.json"))
        {
            if (JsonFileStore.TryRead(path, out Dictionary<string, object> payload))
            {
                results.Add(ModelJsonMapper.ToRecipeCandidate(payload, Path.GetFileNameWithoutExtension(path)));
            }
        }

        return results;
    }

    private static bool IsWriteTool(string toolName)
    {
        return toolName == Contracts.Tooling.ToolNames.SetCustomProperty ||
               toolName == Contracts.Tooling.ToolNames.SetDimensionValue ||
               toolName == Contracts.Tooling.ToolNames.SuppressFeature ||
               toolName == Contracts.Tooling.ToolNames.UnsuppressFeature;
    }

    private static RecipeCandidate LoadOrCreate(string path, string recipeId, string intent, List<string> toolSequence)
    {
        if (JsonFileStore.TryRead(path, out Dictionary<string, object> payload))
        {
            return ModelJsonMapper.ToRecipeCandidate(payload, recipeId);
        }

        return new RecipeCandidate
        {
            RecipeId = recipeId,
            Title = intent,
            Intent = intent,
            ToolSequence = toolSequence.ToList(),
            PromotionState = "candidate",
            RequiredUnlockTier = Contracts.Enums.ToolUnlockTier.Assisted
        };
    }

    private static string ComputeRecipeId(string intent, List<string> toolSequence)
    {
        string basis = intent + "|" + string.Join("|", toolSequence);
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        byte[] hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(basis));
        return "recipe_" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
