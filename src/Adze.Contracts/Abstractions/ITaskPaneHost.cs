using System;
using System.Collections.Generic;
using System.Threading;
using Adze.Contracts.Models;

namespace Adze.Contracts.Abstractions;

/// <summary>
/// Minimal surface the v1.1 native sidebar (Adze.UI.V2.NativeTaskPaneControl)
/// needs from its host. Implemented by:
///   - <c>Adze.Host.Infrastructure.HostState</c> — production COM-loaded path (future chunk)
///   - <c>Adze.UiHarness.StubHostState</c> — out-of-SOLIDWORKS dev harness (this chunk)
///
/// Lives in Adze.Contracts so both implementers and the UI control can reference
/// it without circular project dependencies. The UI mounts via constructor
/// injection; no static singletons.
/// </summary>
public interface ITaskPaneHost
{
    /// <summary>The currently captured live SOLIDWORKS session, or null.</summary>
    SessionContext? CurrentContext { get; }

    /// <summary>Append-only conversation log for the active document.</summary>
    IReadOnlyList<ChatEntry> ChatHistory { get; }

    /// <summary>Writes awaiting user approval. Empty when there are none.</summary>
    IReadOnlyList<PendingWriteAction> PendingWrites { get; }

    /// <summary>One-line summary of the active document for the sidebar header.</summary>
    string DocumentSummary { get; }

    /// <summary>Provenance label for the current state (e.g. snapshot path or "live").</summary>
    string SourceLabel { get; }

    /// <summary>Raised whenever any of the above state has materially changed.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised as final-answer text streams in token-by-token.</summary>
    event EventHandler<StreamChunkEventArgs>? StreamChunkReceived;

    /// <summary>Raised as the agent loop or single-turn path enters/exits tool calls.</summary>
    event EventHandler<ToolProgressEventArgs>? ToolProgress;

    /// <summary>Submit a new user prompt. Cancellation aborts the run mid-flight.</summary>
    void SubmitUserQuery(string query, CancellationToken cancellation);

    /// <summary>Cancel whatever run is currently in flight (no-op if idle).</summary>
    void CancelCurrentRun();

    /// <summary>Approve and apply a pending write by its <see cref="PendingWriteAction.WriteId"/>.</summary>
    void ApplyPendingWrite(string writeId);

    /// <summary>Dismiss a pending write without applying it.</summary>
    void CancelPendingWrite(string writeId);
}

/// <summary>Payload for <see cref="ITaskPaneHost.StreamChunkReceived"/>.</summary>
public sealed class StreamChunkEventArgs : EventArgs
{
    /// <summary>The text chunk received in this SSE delta.</summary>
    public string Chunk { get; }

    /// <summary>True when this is the last chunk for the in-flight run.</summary>
    public bool IsFinal { get; }

    public StreamChunkEventArgs(string chunk, bool isFinal)
    {
        Chunk = chunk ?? string.Empty;
        IsFinal = isFinal;
    }
}

/// <summary>Payload for <see cref="ITaskPaneHost.ToolProgress"/>.</summary>
public sealed class ToolProgressEventArgs : EventArgs
{
    /// <summary>Step number in the agent loop (1-based).</summary>
    public int Step { get; }

    /// <summary>Tool name being invoked, or null while the model is "thinking".</summary>
    public string? ToolName { get; }

    /// <summary>Status label: <c>started</c>, <c>completed</c>, <c>failed</c>, <c>thinking</c>.</summary>
    public string Status { get; }

    /// <summary>Human-readable description of the current step.</summary>
    public string Description { get; }

    /// <summary>Tool execution duration in milliseconds (null while still running).</summary>
    public long? DurationMs { get; }

    public ToolProgressEventArgs(int step, string? toolName, string status, string description, long? durationMs = null)
    {
        Step = step;
        ToolName = toolName;
        Status = status ?? string.Empty;
        Description = description ?? string.Empty;
        DurationMs = durationMs;
    }
}
