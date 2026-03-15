using System;
using System.Collections.Generic;

namespace Adze.Broker.Models;

public enum ConversationRole { System, User, Assistant, Tool }

public sealed class ConversationMessage
{
    public ConversationRole Role { get; set; }

    public string Text { get; set; } = string.Empty;

    public object? RawPayload { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentConversationState
{
    public string SessionId { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public List<ConversationMessage> Messages { get; } = new();

    public int EstimatedTotalTokens { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
