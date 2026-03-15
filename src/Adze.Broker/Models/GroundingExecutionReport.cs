using System;
using System.Collections.Generic;
using Adze.Contracts.Models;

namespace Adze.Broker.Models;

public sealed class GroundingExecutionReport
{
    public DateTimeOffset ExecutedUtc { get; set; }

    public bool IsApplicationConnected { get; set; }

    public string Request { get; set; } = string.Empty;

    public BrokerPrompt Prompt { get; set; } = new();

    public BrokerResponse Response { get; set; } = new();

    public List<ToolResult> ToolResults { get; set; } = new();

    public int SuccessfulToolCount { get; set; }

    public int FailedToolCount { get; set; }
}
