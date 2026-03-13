using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Trace.Serialization;
using Adze.Trace.Storage;

namespace Adze.Trace.Recipes;

public static class RecipeCandidateEngine
{
    public static int CountReviewReadyCandidates()
    {
        StatePaths.Ensure();
        int count = 0;
        foreach (string path in Directory.GetFiles(StatePaths.RecipeDirectory, "*.json"))
        {
            if (!JsonFileStore.TryRead(path, out Dictionary<string, object> payload))
            {
                continue;
            }

            RecipeCandidate candidate = ModelJsonMapper.ToRecipeCandidate(payload, Path.GetFileNameWithoutExtension(path));
            if (string.Equals(candidate.PromotionState, "review_ready", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    public static RecipeCandidate? UpdateFromTrace(TraceEvent traceEvent)
    {
        if (!string.Equals(traceEvent.Result.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (traceEvent.ToolSequence.Count == 0)
        {
            return null;
        }

        StatePaths.Ensure();
        string recipeId = ComputeRecipeId(traceEvent.Intent, traceEvent.ToolSequence);
        string path = Path.Combine(StatePaths.RecipeDirectory, recipeId + ".json");
        RecipeCandidate candidate = LoadOrCreate(path, recipeId, traceEvent);

        if (!candidate.SourceTraceIds.Contains(traceEvent.TraceId, StringComparer.OrdinalIgnoreCase))
        {
            candidate.SourceTraceIds.Add(traceEvent.TraceId);
        }

        candidate.ReliabilityScore = Math.Round(Math.Min(1.0, candidate.SourceTraceIds.Count / 3.0), 2);
        candidate.PromotionState = candidate.SourceTraceIds.Count >= 3 ? "review_ready" : "candidate";
        candidate.RequiredUnlockTier = ToolUnlockTier.Assisted;

        if (candidate.PromotionState == "review_ready" &&
            !candidate.ReviewNotes.Contains("Ready for human review after repeated successful traces.", StringComparer.OrdinalIgnoreCase))
        {
            candidate.ReviewNotes.Add("Ready for human review after repeated successful traces.");
        }

        JsonFileStore.Write(path, ModelJsonMapper.ToJson(candidate));
        return candidate;
    }

    private static RecipeCandidate LoadOrCreate(string path, string recipeId, TraceEvent traceEvent)
    {
        if (JsonFileStore.TryRead(path, out Dictionary<string, object> payload))
        {
            return ModelJsonMapper.ToRecipeCandidate(payload, recipeId);
        }

        return new RecipeCandidate
        {
            RecipeId = recipeId,
            Title = traceEvent.Intent,
            Intent = traceEvent.Intent,
            ToolSequence = traceEvent.ToolSequence.ToList(),
            PromotionState = "candidate",
            RequiredUnlockTier = ToolUnlockTier.Assisted
        };
    }

    private static string ComputeRecipeId(string intent, IEnumerable<string> toolSequence)
    {
        string basis = intent + "|" + string.Join("|", toolSequence);
        using SHA1 sha1 = SHA1.Create();
        byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(basis));
        return "recipe_" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
