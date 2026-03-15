using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Broker.Models;

namespace Adze.Broker.Orchestration;

public sealed class TruncationPolicy
{
    public bool ProtectSystemMessage { get; set; } = true;

    public bool ProtectInitialUserIntent { get; set; } = true;

    public int ProtectedRecentTurns { get; set; } = 6;
}

public static class ConversationTruncator
{
    /// <summary>
    /// Truncates conversation messages when they exceed <paramref name="maxMessageCount"/>,
    /// preserving system messages, the initial user intent, and recent turns according to the policy.
    /// Returns a new <see cref="AgentConversationState"/> with truncated messages and updated token estimate.
    /// </summary>
    public static AgentConversationState Truncate(
        AgentConversationState state,
        int maxMessageCount,
        TruncationPolicy? policy = null)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        policy ??= new TruncationPolicy();

        List<ConversationMessage> messages = state.Messages.ToList();

        if (messages.Count <= maxMessageCount)
        {
            return CloneWithMessages(state, messages);
        }

        // Identify protected messages by index
        var protectedIndices = new HashSet<int>();

        if (policy.ProtectSystemMessage)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Role == ConversationRole.System)
                {
                    protectedIndices.Add(i);
                }
            }
        }

        if (policy.ProtectInitialUserIntent)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Role == ConversationRole.User)
                {
                    protectedIndices.Add(i);
                    break;
                }
            }
        }

        // Protect the most recent N turns from the end
        int recentCount = Math.Min(policy.ProtectedRecentTurns, messages.Count);
        int recentStart = messages.Count - recentCount;
        for (int i = recentStart; i < messages.Count; i++)
        {
            protectedIndices.Add(i);
        }

        // If all protected messages already exceed or meet the limit, keep only protected ones
        if (protectedIndices.Count >= maxMessageCount)
        {
            var protectedOnly = protectedIndices
                .OrderBy(i => i)
                .Select(i => messages[i])
                .ToList();

            return CloneWithMessages(state, protectedOnly);
        }

        // Drop middle (non-protected) messages until we are at or below the limit
        var result = new List<ConversationMessage>();
        int dropped = 0;
        int mustDrop = messages.Count - maxMessageCount;

        for (int i = 0; i < messages.Count; i++)
        {
            if (protectedIndices.Contains(i))
            {
                result.Add(messages[i]);
            }
            else if (dropped < mustDrop)
            {
                dropped++;
            }
            else
            {
                result.Add(messages[i]);
            }
        }

        return CloneWithMessages(state, result);
    }

    /// <summary>
    /// Estimates the total token count for all messages using a rough 4-characters-per-token heuristic.
    /// </summary>
    public static int EstimateTokens(IEnumerable<ConversationMessage> messages)
    {
        if (messages == null) return 0;

        int totalChars = 0;
        foreach (ConversationMessage msg in messages)
        {
            totalChars += msg.Text?.Length ?? 0;
        }

        return Math.Max(1, (totalChars + 3) / 4);
    }

    private static AgentConversationState CloneWithMessages(
        AgentConversationState original,
        List<ConversationMessage> messages)
    {
        var clone = new AgentConversationState
        {
            SessionId = original.SessionId,
            DocumentKey = original.DocumentKey,
            CreatedUtc = original.CreatedUtc,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };

        foreach (ConversationMessage msg in messages)
        {
            clone.Messages.Add(msg);
        }

        clone.EstimatedTotalTokens = EstimateTokens(messages);

        return clone;
    }
}
