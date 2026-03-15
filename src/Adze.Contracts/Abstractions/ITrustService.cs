using Adze.Contracts.Enums;
using Adze.Contracts.Models;

namespace Adze.Contracts.Abstractions;

public interface ITrustService
{
    ToolUnlockTier GetCurrentTier(string userId);

    bool CanExecuteWriteTool(string toolName, ToolUnlockTier requiredTier, string userId);

    bool CanPromoteRecipe(RecipeCandidate candidate);
}
