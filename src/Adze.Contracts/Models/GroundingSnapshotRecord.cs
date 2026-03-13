using System;
using System.Collections.Generic;

namespace Adze.Contracts.Models;

public sealed class GroundingSnapshotRecord
{
    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; }

    public SessionContext Context { get; set; } = new();

    public List<ToolResult> ToolResults { get; set; } = new();

    public int AchievementCount { get; set; }

    public int ReviewReadyRecipeCount { get; set; }

    public string? LatestAchievementTitle { get; set; }
}
