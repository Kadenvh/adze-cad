using System;

namespace Adze.Contracts.Models;

/// <summary>
/// One round-trip exchange between the user and the assistant. Persisted in the
/// host's chat history and replayed in the sidebar's conversation thread.
/// Mirrored by the host-internal ChatEntry record in HostState; this public
/// contract is the surface UI projects bind against (Adze.UI sidebar control,
/// Adze.UiHarness preview).
/// </summary>
public sealed class ChatEntry
{
    /// <summary>The user's submitted prompt text (verbatim).</summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>The assistant's rendered final answer (markdown body).</summary>
    public string AssistantMessage { get; set; } = string.Empty;

    /// <summary>
    /// Where the assistant answer originated:
    /// <c>model_openai</c>, <c>model_anthropic</c>, <c>deterministic_fallback</c>, etc.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>One-line per-message footer (token count, model, source label).</summary>
    public string Footer { get; set; } = string.Empty;

    /// <summary>UTC timestamp of when the entry was finalized.</summary>
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
