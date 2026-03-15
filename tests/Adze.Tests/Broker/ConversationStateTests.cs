using System;
using System.Linq;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class ConversationStateTests
{
    // --- Message accumulation ---

    [Test]
    public void NewState_HasEmptyMessages()
    {
        var state = new AgentConversationState();

        Assert.That(state.Messages, Is.Empty);
        Assert.That(state.SessionId, Is.EqualTo(string.Empty));
        Assert.That(state.DocumentKey, Is.EqualTo(string.Empty));
    }

    [Test]
    public void AddMessages_AccumulatesInOrder()
    {
        var state = new AgentConversationState { SessionId = "s1" };

        state.Messages.Add(new ConversationMessage { Role = ConversationRole.System, Text = "You are an assistant." });
        state.Messages.Add(new ConversationMessage { Role = ConversationRole.User, Text = "Hello" });
        state.Messages.Add(new ConversationMessage { Role = ConversationRole.Assistant, Text = "Hi there" });

        Assert.That(state.Messages.Count, Is.EqualTo(3));
        Assert.That(state.Messages[0].Role, Is.EqualTo(ConversationRole.System));
        Assert.That(state.Messages[1].Role, Is.EqualTo(ConversationRole.User));
        Assert.That(state.Messages[2].Role, Is.EqualTo(ConversationRole.Assistant));
    }

    [Test]
    public void ConversationMessage_DefaultTimestamp_IsRecentUtc()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        var msg = new ConversationMessage { Role = ConversationRole.User, Text = "test" };
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.That(msg.TimestampUtc, Is.GreaterThanOrEqualTo(before));
        Assert.That(msg.TimestampUtc, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void ConversationMessage_ProviderAndModel_AreNullByDefault()
    {
        var msg = new ConversationMessage();

        Assert.That(msg.Provider, Is.Null);
        Assert.That(msg.Model, Is.Null);
    }

    [Test]
    public void ConversationMessage_RawPayload_CanStoreArbitraryObject()
    {
        var payload = new { foo = "bar" };
        var msg = new ConversationMessage { Role = ConversationRole.Tool, RawPayload = payload };

        Assert.That(msg.RawPayload, Is.Not.Null);
        Assert.That(msg.RawPayload, Is.SameAs(payload));
    }

    // --- Truncation: preserves system + initial user + recent turns ---

    [Test]
    public void Truncate_UnderLimit_ReturnsAllMessages()
    {
        AgentConversationState state = BuildConversation(5);

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 10);

        Assert.That(result.Messages.Count, Is.EqualTo(5));
    }

    [Test]
    public void Truncate_ExactlyAtLimit_ReturnsAllMessages()
    {
        AgentConversationState state = BuildConversation(8);

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 8);

        Assert.That(result.Messages.Count, Is.EqualTo(8));
    }

    [Test]
    public void Truncate_PreservesSystemMessage()
    {
        AgentConversationState state = BuildConversation(12);
        // First message is System in BuildConversation

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 8);

        Assert.That(result.Messages[0].Role, Is.EqualTo(ConversationRole.System));
        Assert.That(result.Messages[0].Text, Is.EqualTo("System prompt"));
    }

    [Test]
    public void Truncate_PreservesInitialUserIntent()
    {
        AgentConversationState state = BuildConversation(12);

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 8);

        // The initial user message should be present
        bool hasInitialUser = result.Messages.Any(m =>
            m.Role == ConversationRole.User && m.Text == "User message 1");

        Assert.That(hasInitialUser, Is.True, "Initial user intent should be preserved after truncation.");
    }

    [Test]
    public void Truncate_PreservesRecentTurns()
    {
        AgentConversationState state = BuildConversation(20);
        var policy = new TruncationPolicy { ProtectedRecentTurns = 6 };

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 10, policy);

        // The last 6 messages of the original should appear at the end of the result
        var originalLast6 = state.Messages.Skip(state.Messages.Count - 6).ToList();
        var resultLast6 = result.Messages.Skip(result.Messages.Count - 6).ToList();

        for (int i = 0; i < 6; i++)
        {
            Assert.That(resultLast6[i].Text, Is.EqualTo(originalLast6[i].Text),
                $"Recent turn at offset {i} should be preserved.");
        }
    }

    [Test]
    public void Truncate_DropsMiddleMessages()
    {
        // Build: System, User1, Asst1, User2, Asst2, User3, Asst3, User4, Asst4, User5, Asst5
        AgentConversationState state = BuildConversation(11);
        var policy = new TruncationPolicy { ProtectedRecentTurns = 4 };

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 7, policy);

        Assert.That(result.Messages.Count, Is.LessThanOrEqualTo(7));

        // System and first user must be present
        Assert.That(result.Messages[0].Role, Is.EqualTo(ConversationRole.System));
        Assert.That(result.Messages.Any(m => m.Text == "User message 1"), Is.True);

        // Last 4 messages must be present
        var originalLast4 = state.Messages.Skip(state.Messages.Count - 4).ToList();
        foreach (ConversationMessage expected in originalLast4)
        {
            Assert.That(result.Messages.Any(m => m.Text == expected.Text), Is.True,
                $"Recent message '{expected.Text}' should be preserved.");
        }

        // Some middle messages must have been dropped
        Assert.That(result.Messages.Count, Is.LessThan(state.Messages.Count));
    }

    // --- Truncation: empty/edge cases ---

    [Test]
    public void Truncate_EmptyState_ProducesNoErrors()
    {
        var state = new AgentConversationState { SessionId = "empty" };

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 10);

        Assert.That(result.Messages, Is.Empty);
        Assert.That(result.SessionId, Is.EqualTo("empty"));
    }

    [Test]
    public void Truncate_NullState_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ConversationTruncator.Truncate(null!, maxMessageCount: 10));
    }

    [Test]
    public void Truncate_SingleSystemMessage_Preserved()
    {
        var state = new AgentConversationState();
        state.Messages.Add(new ConversationMessage { Role = ConversationRole.System, Text = "sys" });

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 1);

        Assert.That(result.Messages.Count, Is.EqualTo(1));
        Assert.That(result.Messages[0].Text, Is.EqualTo("sys"));
    }

    [Test]
    public void Truncate_PolicyDisableProtections_DropsEverythingExceptRecent()
    {
        AgentConversationState state = BuildConversation(10);
        var policy = new TruncationPolicy
        {
            ProtectSystemMessage = false,
            ProtectInitialUserIntent = false,
            ProtectedRecentTurns = 3
        };

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 3, policy);

        Assert.That(result.Messages.Count, Is.EqualTo(3));

        // Result should be the last 3 messages
        var originalLast3 = state.Messages.Skip(state.Messages.Count - 3).ToList();
        for (int i = 0; i < 3; i++)
        {
            Assert.That(result.Messages[i].Text, Is.EqualTo(originalLast3[i].Text));
        }
    }

    [Test]
    public void Truncate_PreservesSessionMetadata()
    {
        var state = new AgentConversationState
        {
            SessionId = "test-session",
            DocumentKey = "doc-key-123"
        };
        state.Messages.Add(new ConversationMessage { Role = ConversationRole.User, Text = "hello" });

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 10);

        Assert.That(result.SessionId, Is.EqualTo("test-session"));
        Assert.That(result.DocumentKey, Is.EqualTo("doc-key-123"));
        Assert.That(result.CreatedUtc, Is.EqualTo(state.CreatedUtc));
    }

    // --- Token estimation ---

    [Test]
    public void EstimateTokens_EmptyMessages_ReturnsMinimum()
    {
        int tokens = ConversationTruncator.EstimateTokens(Array.Empty<ConversationMessage>());

        // Empty yields 0 chars, estimate is Max(1, 0/4) = 1
        Assert.That(tokens, Is.EqualTo(1));
    }

    [Test]
    public void EstimateTokens_NullCollection_ReturnsZero()
    {
        int tokens = ConversationTruncator.EstimateTokens(null!);

        Assert.That(tokens, Is.EqualTo(0));
    }

    [Test]
    public void EstimateTokens_KnownText_CalculatesCorrectly()
    {
        // "Hello world" = 11 chars, (11 + 3) / 4 = 3 tokens
        var messages = new[]
        {
            new ConversationMessage { Text = "Hello world" }
        };

        int tokens = ConversationTruncator.EstimateTokens(messages);

        Assert.That(tokens, Is.EqualTo(3));
    }

    [Test]
    public void EstimateTokens_MultipleMessages_SumsAllText()
    {
        // "aaaa" = 4 chars, "bbbbbbbb" = 8 chars => total 12 chars => (12 + 3) / 4 = 3 tokens
        var messages = new[]
        {
            new ConversationMessage { Text = "aaaa" },
            new ConversationMessage { Text = "bbbbbbbb" }
        };

        int tokens = ConversationTruncator.EstimateTokens(messages);

        Assert.That(tokens, Is.EqualTo(3));
    }

    [Test]
    public void Truncate_UpdatesEstimatedTotalTokens()
    {
        AgentConversationState state = BuildConversation(12);

        AgentConversationState result = ConversationTruncator.Truncate(state, maxMessageCount: 8);

        // Token count should be recalculated and reflect the truncated messages
        int expectedTokens = ConversationTruncator.EstimateTokens(result.Messages);
        Assert.That(result.EstimatedTotalTokens, Is.EqualTo(expectedTokens));
        Assert.That(result.EstimatedTotalTokens, Is.GreaterThan(0));
    }

    [Test]
    public void EstimateTokens_NullText_TreatedAsEmpty()
    {
        var messages = new[]
        {
            new ConversationMessage { Text = null! }
        };

        int tokens = ConversationTruncator.EstimateTokens(messages);

        // 0 chars => Max(1, 0/4) = 1
        Assert.That(tokens, Is.EqualTo(1));
    }

    // --- Helpers ---

    /// <summary>
    /// Builds a conversation state with the pattern:
    /// System, User1, Assistant1, User2, Assistant2, ...
    /// Total message count equals <paramref name="count"/>.
    /// </summary>
    private static AgentConversationState BuildConversation(int count)
    {
        var state = new AgentConversationState
        {
            SessionId = "test-session",
            DocumentKey = "test-doc"
        };

        if (count <= 0) return state;

        state.Messages.Add(new ConversationMessage
        {
            Role = ConversationRole.System,
            Text = "System prompt"
        });

        int turnNumber = 1;
        for (int i = 1; i < count; i++)
        {
            if (i % 2 == 1)
            {
                state.Messages.Add(new ConversationMessage
                {
                    Role = ConversationRole.User,
                    Text = $"User message {turnNumber}"
                });
            }
            else
            {
                state.Messages.Add(new ConversationMessage
                {
                    Role = ConversationRole.Assistant,
                    Text = $"Assistant message {turnNumber}"
                });
                turnNumber++;
            }
        }

        return state;
    }
}
