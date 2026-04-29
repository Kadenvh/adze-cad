using System;
using System.Collections.Generic;
using System.Threading;
using Adze.Broker.Configuration;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using ContractsChatEntry = Adze.Contracts.Models.ChatEntry;
using ContractsPendingWrite = Adze.Contracts.Models.PendingWriteAction;
using HostChatEntry = Adze.Host.Infrastructure.ChatEntry;
using HostPendingWrite = Adze.Host.Infrastructure.PendingWriteAction;

namespace Adze.Host.Infrastructure;

/// <summary>
/// Instance adapter that bridges the static <see cref="HostState"/> (production
/// COM-loaded path) to the <see cref="ITaskPaneHost"/> contract consumed by
/// <c>Adze.UI.V2.NativeTaskPaneControl</c>.
///
/// HostState is intentionally static (single-process COM add-in lifecycle), so
/// the adapter is the seam where the v1.1 sidebar gets a per-instance handle
/// without forcing a cross-cutting refactor of HostState's static API. One
/// adapter per add-in session — see <see cref="HostState.GetTaskPaneHost"/>.
///
/// Mapping rules:
///   - Host-internal <see cref="HostChatEntry"/> → public <see cref="ContractsChatEntry"/>
///     via <see cref="ToPublicChatEntry"/>. Keeps host-internal record unchanged.
///   - Host-internal <see cref="HostPendingWrite"/> → public <see cref="ContractsPendingWrite"/>
///     via <see cref="ToPublicPendingWrite"/>. Synthesises a stable WriteId
///     from list index since the host-internal type doesn't carry one.
///   - SubmitUserQuery / Cancel / Apply / Cancel route to the existing static
///     entry points (<see cref="HostState.RunAssistant"/>, <see cref="HostState.CancelRun"/>,
///     <see cref="HostState.ApplyPendingWrite(int)"/>, <see cref="HostState.CancelPendingWrite(int)"/>).
///
/// Event semantics:
///   - <see cref="StateChanged"/> fires after chat-history append, pending-write
///     mutation, and context refresh (callers call <see cref="RaiseStateChanged"/>).
///   - <see cref="StreamChunkReceived"/> fires from the existing streaming
///     callback wired through <see cref="HostState.CompleteAssistantRun"/>.
///   - <see cref="ToolProgress"/> fires from the existing
///     <see cref="AgentProgressUpdate"/> callback.
/// </summary>
internal sealed class HostStateAdapter : ITaskPaneHost
{
    private readonly object _sync = new();
    private CancellationTokenSource? _runCts;

    public event EventHandler? StateChanged;
    public event EventHandler<StreamChunkEventArgs>? StreamChunkReceived;
    public event EventHandler<ToolProgressEventArgs>? ToolProgress;

    public SessionContext? CurrentContext
    {
        get
        {
            try
            {
                return HostState.PrepareAssistantRun(string.Empty).Context;
            }
            catch
            {
                return null;
            }
        }
    }

    public IReadOnlyList<ContractsChatEntry> ChatHistory
    {
        get
        {
            List<HostChatEntry> raw = HostState.GetChatHistory();
            var result = new List<ContractsChatEntry>(raw.Count);
            foreach (HostChatEntry e in raw)
            {
                result.Add(ToPublicChatEntry(e));
            }
            return result;
        }
    }

    public IReadOnlyList<ContractsPendingWrite> PendingWrites
    {
        get
        {
            List<HostPendingWrite> raw = HostState.GetPendingWrites();
            var result = new List<ContractsPendingWrite>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                result.Add(ToPublicPendingWrite(raw[i], i));
            }
            return result;
        }
    }

    public string DocumentSummary
    {
        get
        {
            SessionContext? ctx = CurrentContext;
            if (ctx?.Document == null) return "(no document)";
            string type = ctx.Document.Type ?? "?";
            string title = ctx.Document.Title ?? "(unnamed)";
            return type + " - " + title;
        }
    }

    public string SourceLabel
    {
        get
        {
            SessionContext? ctx = CurrentContext;
            string? path = ctx?.Document?.Path;
            return string.IsNullOrWhiteSpace(path) ? "live" : path!;
        }
    }

    /// <summary>
    /// Submits a user prompt by routing to the existing
    /// <see cref="HostState.CompleteAssistantRun(AssistantRunPreparation, Action{string}, Action{AgentProgressUpdate})"/>
    /// entry point on a background thread. Stream chunks and progress callbacks
    /// re-enter this adapter via <see cref="OnStreamChunk"/> / <see cref="OnAgentProgress"/>
    /// which raise the public ITaskPaneHost events.
    /// </summary>
    public void SubmitUserQuery(string query, CancellationToken cancellation)
    {
        AssistantRunPreparation prep;
        try
        {
            HostState.BeginRun();
            prep = HostState.PrepareAssistantRun(query);
        }
        catch (Exception ex)
        {
            FileLogger.Error("Sidebar: PrepareAssistantRun failed.", ex);
            HostState.EndRun();
            RaiseStateChanged();
            return;
        }

        lock (_sync)
        {
            _runCts?.Dispose();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        }

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Action<string> streamCallback = chunk => OnStreamChunk(chunk, isFinal: false);
                Action<AgentProgressUpdate> progressCallback = OnAgentProgress;

                AssistantRunSnapshot snapshot = HostState.CompleteAssistantRun(prep, streamCallback, progressCallback);
                // Atomic finalize: send the rendered final markdown as the
                // last chunk with isFinal=true. NativeTaskPaneControl swaps
                // the in-flight raw text for this rendered version before
                // StateChanged repaints the thread from ChatHistory — no flash.
                OnStreamChunk(snapshot?.AnswerText ?? string.Empty, isFinal: true);
            }
            catch (OperationCanceledException)
            {
                FileLogger.Info("Sidebar: run cancelled mid-flight.");
            }
            catch (Exception ex)
            {
                FileLogger.Error("Sidebar: run failed.", ex);
            }
            finally
            {
                HostState.EndRun();
                RaiseStateChanged();
            }
        });
    }

    public void CancelCurrentRun()
    {
        lock (_sync)
        {
            try { _runCts?.Cancel(); } catch { /* shutting down */ }
        }
        try { HostState.CancelRun(); } catch { /* idempotent */ }
        RaiseStateChanged();
    }

    public void ApplyPendingWrite(string writeId)
    {
        int index = ResolveWriteIndex(writeId);
        if (index < 0) return;
        try
        {
            HostState.ApplyPendingWrite(index);
        }
        catch (Exception ex)
        {
            FileLogger.Error("Sidebar: ApplyPendingWrite failed.", ex);
        }
        RaiseStateChanged();
    }

    public void CancelPendingWrite(string writeId)
    {
        int index = ResolveWriteIndex(writeId);
        if (index < 0) return;
        try
        {
            HostState.CancelPendingWrite(index);
        }
        catch (Exception ex)
        {
            FileLogger.Error("Sidebar: CancelPendingWrite failed.", ex);
        }
        RaiseStateChanged();
    }

    /// <summary>
    /// Public hook for HostState/AddIn to nudge the sidebar after document
    /// changes, snapshot writes, or other state mutations not driven by a run.
    /// </summary>
    public void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hook the streaming-text callback. <paramref name="isFinal"/> is set true
    /// when the run completes so the sidebar can swap from raw streaming text
    /// to the markdown-rendered final answer.
    /// </summary>
    public void OnStreamChunk(string chunk, bool isFinal)
    {
        StreamChunkReceived?.Invoke(this, new StreamChunkEventArgs(chunk ?? string.Empty, isFinal));
    }

    /// <summary>
    /// Hook the agent-progress callback. Maps <see cref="AgentProgressUpdate"/>
    /// from the broker's agent loop into the sidebar-facing <see cref="ToolProgressEventArgs"/>.
    /// </summary>
    public void OnAgentProgress(AgentProgressUpdate progress)
    {
        if (progress == null) return;
        string status = progress.Kind.ToString().ToLowerInvariant();
        ToolProgress?.Invoke(this, new ToolProgressEventArgs(
            step: progress.Iteration,
            toolName: progress.ToolName,
            status: status,
            description: progress.Message ?? string.Empty,
            durationMs: null));
    }

    // ─── Mapping helpers (host-internal → public contract) ───

    /// <summary>
    /// Converts the host-internal <see cref="HostChatEntry"/> record to the
    /// public <see cref="ContractsChatEntry"/> the sidebar binds against.
    /// Lossless field-by-field copy — host-internal record stays unchanged.
    /// </summary>
    internal static ContractsChatEntry ToPublicChatEntry(HostChatEntry host)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        return new ContractsChatEntry
        {
            UserMessage = host.UserMessage ?? string.Empty,
            AssistantMessage = host.AssistantMessage ?? string.Empty,
            Source = host.Source ?? string.Empty,
            Footer = host.Footer ?? string.Empty,
            TimestampUtc = host.TimestampUtc
        };
    }

    /// <summary>
    /// Converts the host-internal <see cref="HostPendingWrite"/> to the public
    /// <see cref="ContractsPendingWrite"/>. Synthesises a stable WriteId from
    /// the list index — host-internal type doesn't carry one and changing it
    /// would ripple through write-tracking and confirmation paths. The index
    /// is unstable across reorderings but stable within a single render pass,
    /// which is enough for the sidebar's Apply/Cancel routing.
    /// </summary>
    internal static ContractsPendingWrite ToPublicPendingWrite(HostPendingWrite host, int index)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        return new ContractsPendingWrite
        {
            WriteId = "pw-" + index.ToString(),
            ToolName = host.ToolName ?? string.Empty,
            Arguments = host.Arguments ?? new Dictionary<string, object?>(),
            Preview = host.Preview ?? new WritePreview(),
            Applied = host.Applied,
            Cancelled = host.Cancelled,
            ResultMessage = host.ResultMessage,
            IsElevated = host.IsElevated,
            UndoLabel = host.UndoLabel ?? string.Empty
        };
    }

    private static int ResolveWriteIndex(string writeId)
    {
        if (string.IsNullOrWhiteSpace(writeId)) return -1;
        // WriteId format: "pw-<index>" — see ToPublicPendingWrite. Defensive parse.
        const string prefix = "pw-";
        if (!writeId.StartsWith(prefix, StringComparison.Ordinal)) return -1;
        return int.TryParse(writeId.Substring(prefix.Length), out int idx) ? idx : -1;
    }
}
