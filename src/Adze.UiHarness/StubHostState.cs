using System;
using System.Collections.Generic;
using System.Threading;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.UiHarness;

/// <summary>
/// Test-only stand-in for Adze.Host.Infrastructure.HostState. Holds a typed
/// SessionContext loaded from disk plus the auxiliary surfaces UI needs
/// (chat history, pending writes). No COM, no SOLIDWORKS, no live broker
/// traffic — ever.
///
/// Implements <see cref="ITaskPaneHost"/> so the new
/// <c>NativeTaskPaneControl</c> mounts directly against it. SubmitUserQuery,
/// CancelCurrentRun and the write decisions are no-ops that just append a
/// "stubbed" assistant reply to the chat log so we can visually verify the
/// thread-rendering path without spinning up a broker.
/// </summary>
internal sealed class StubHostState : ITaskPaneHost
{
    private readonly List<ChatEntry> _chatHistory = new();
    private readonly List<PendingWriteAction> _pendingWrites = new();

    public SessionContext? CurrentContext { get; private set; }
    public string SourceLabel { get; private set; } = "(no snapshot loaded)";

    public IReadOnlyList<ChatEntry> ChatHistory => _chatHistory;
    public IReadOnlyList<PendingWriteAction> PendingWrites => _pendingWrites;

    public event EventHandler? StateChanged;
    public event EventHandler<StreamChunkEventArgs>? StreamChunkReceived;
    public event EventHandler<ToolProgressEventArgs>? ToolProgress;

    public void LoadContext(SessionContext context, string label)
    {
        CurrentContext = context;
        SourceLabel = label;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        CurrentContext = null;
        SourceLabel = "(cleared)";
        _chatHistory.Clear();
        _pendingWrites.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AppendChat(string user, string assistant, string source = "stub")
    {
        _chatHistory.Add(new ChatEntry
        {
            UserMessage = user,
            AssistantMessage = assistant,
            Source = source,
            Footer = "(stub) " + source,
            TimestampUtc = DateTimeOffset.UtcNow
        });
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string DocumentSummary
    {
        get
        {
            if (CurrentContext?.Document == null) return "(no document)";
            string type = CurrentContext.Document.Type ?? "?";
            string title = CurrentContext.Document.Title ?? "(unnamed)";
            return type + " - " + title;
        }
    }

    // ── ITaskPaneHost surface ─────────────────────────────────────────────

    public void SubmitUserQuery(string query, CancellationToken cancellation)
    {
        // Stub: echo the user prompt back and synthesize a canned assistant
        // reply so the UI thread can verify rendering without a real broker.
        _chatHistory.Add(new ChatEntry
        {
            UserMessage = query,
            AssistantMessage = BuildStubAnswer(query),
            Source = "stub_harness",
            Footer = "(harness) stub_harness",
            TimestampUtc = DateTimeOffset.UtcNow
        });
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelCurrentRun()
    {
        // Stub: nothing in flight, but log a system note so the test path is exercised.
        Console.WriteLine("[StubHostState] CancelCurrentRun called.");
    }

    public void ApplyPendingWrite(string writeId)
    {
        PendingWriteAction? match = _pendingWrites.Find(p => p.WriteId == writeId);
        if (match == null) return;
        match.Applied = true;
        match.ResultMessage = "(stub) applied";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelPendingWrite(string writeId)
    {
        PendingWriteAction? match = _pendingWrites.Find(p => p.WriteId == writeId);
        if (match == null) return;
        match.Cancelled = true;
        match.ResultMessage = "(stub) cancelled";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Inject a fake pending write so the harness can render a card.</summary>
    public void InjectPendingWrite(PendingWriteAction action)
    {
        _pendingWrites.Add(action);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fire a stream chunk event for harness preview testing.</summary>
    public void EmitStreamChunk(string chunk, bool isFinal)
    {
        StreamChunkReceived?.Invoke(this, new StreamChunkEventArgs(chunk, isFinal));
    }

    /// <summary>Fire a tool-progress event for harness preview testing.</summary>
    public void EmitToolProgress(int step, string? toolName, string status, string desc, long? durationMs = null)
    {
        ToolProgress?.Invoke(this, new ToolProgressEventArgs(step, toolName, status, desc, durationMs));
    }

    private string BuildStubAnswer(string query)
    {
        if (CurrentContext == null)
        {
            return "**No context loaded.**\n\nLoad a snapshot above so the stub harness can pretend to answer:\n\n- " +
                   query;
        }
        string title = CurrentContext.Document?.Title ?? "(unnamed)";
        return "## Stub answer\n\n" +
               "Received prompt: `" + query + "`\n\n" +
               "Active document: **" + title + "**\n\n" +
               "Real assistant runs against this snapshot would use the broker — this harness is stub-only.";
    }
}
