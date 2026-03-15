using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Trace.Progression;

public sealed class TrustService : ITrustService
{
    public ToolUnlockTier GetCurrentTier(string userId)
    {
        ProgressionState state = ProgressionEngine.LoadCurrent(userId);
        return state.ToolUnlockTier;
    }

    public bool CanExecuteWriteTool(string toolName, ToolUnlockTier requiredTier, string userId)
    {
        ToolUnlockTier currentTier = GetCurrentTier(userId);

        // Read tools always allowed
        if (IsReadTool(toolName))
        {
            return true;
        }

        // Write tools require at least Assisted tier
        if (currentTier < ToolUnlockTier.Assisted)
        {
            return false;
        }

        // Check specific tier requirement
        return currentTier >= requiredTier;
    }

    public bool CanPromoteRecipe(RecipeCandidate candidate)
    {
        // Must be review-ready
        if (candidate.PromotionState != "review_ready")
        {
            return false;
        }

        // Must have sufficient reliability score
        if (candidate.ReliabilityScore < 0.66)
        {
            return false;
        }

        // Must have at least 2 source traces
        if (candidate.SourceTraceIds.Count < 2)
        {
            return false;
        }

        return true;
    }

    private static bool IsReadTool(string toolName)
    {
        return toolName == ToolNames.GetActiveDocument ||
               toolName == ToolNames.GetDocumentSummary ||
               toolName == ToolNames.GetSelectionContext ||
               toolName == ToolNames.GetFeatureTreeSlice ||
               toolName == ToolNames.GetDimensions ||
               toolName == ToolNames.GetConfigurations ||
               toolName == ToolNames.GetCustomProperties ||
               toolName == ToolNames.GetMates ||
               toolName == ToolNames.GetRebuildDiagnostics ||
               toolName == ToolNames.GetReferenceGraph;
    }
}
