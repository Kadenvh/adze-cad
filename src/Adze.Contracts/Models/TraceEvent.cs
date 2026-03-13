using System;
using System.Collections.Generic;
using Adze.Contracts.Enums;

namespace Adze.Contracts.Models;

public sealed class TraceEvent
{
    public string TraceId { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; }

    public string Intent { get; set; } = string.Empty;

    public ApprovalState ApprovalState { get; set; }

    public List<string> ToolSequence { get; set; } = new();

    public TraceResult Result { get; set; } = new();

    public List<string> AchievementEvents { get; set; } = new();

    public double ExplorationPercent { get; set; }

    public ToolUnlockTier ToolUnlockTier { get; set; }
}

public sealed class TraceResult
{
    public string Status { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new();
}
