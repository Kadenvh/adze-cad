using System.Collections.Generic;
using Adze.Contracts.Enums;

namespace Adze.Contracts.Models;

public sealed class RecipeCandidate
{
    public string RecipeId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Intent { get; set; } = string.Empty;

    public List<string> SourceTraceIds { get; set; } = new();

    public List<string> ToolSequence { get; set; } = new();

    public string PromotionState { get; set; } = "candidate";

    public double ReliabilityScore { get; set; }

    public ToolUnlockTier RequiredUnlockTier { get; set; }

    public List<string> ReviewNotes { get; set; } = new();
}
