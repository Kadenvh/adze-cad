using System;
using System.Collections.Generic;
using Adze.Contracts.Enums;

namespace Adze.Contracts.Models;

public sealed class ProgressionState
{
    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }

    public ToolUnlockTier ToolUnlockTier { get; set; }

    public double ExplorationPercent { get; set; }

    public List<string> UnlockedTools { get; set; } = new();

    public List<AchievementState> Achievements { get; set; } = new();
}

public sealed class AchievementState
{
    public string AchievementId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset UnlockedUtc { get; set; }

    public string? SourceTraceId { get; set; }
}
