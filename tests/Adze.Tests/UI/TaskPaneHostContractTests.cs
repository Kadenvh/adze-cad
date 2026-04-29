using System;
using System.Collections.Generic;
using System.Threading;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using NUnit.Framework;

namespace Adze.Tests.UI;

/// <summary>
/// Sanity tests for the new <see cref="ITaskPaneHost"/> contract added in the
/// v1.1 sidebar rebuild. We don't spin up a real host (that would need
/// SOLIDWORKS COM); we exercise the contract surface with an in-test fake to
/// verify event wiring and contract semantics survive the project boundary.
/// </summary>
[TestFixture]
public sealed class TaskPaneHostContractTests
{
    [Test]
    public void StreamChunkEventArgs_StoresChunkAndFinalFlag()
    {
        var args = new StreamChunkEventArgs("hello", isFinal: true);
        Assert.That(args.Chunk, Is.EqualTo("hello"));
        Assert.That(args.IsFinal, Is.True);

        var partial = new StreamChunkEventArgs("partial", isFinal: false);
        Assert.That(partial.Chunk, Is.EqualTo("partial"));
        Assert.That(partial.IsFinal, Is.False);
    }

    [Test]
    public void StreamChunkEventArgs_NullChunk_NormalisedToEmpty()
    {
        var args = new StreamChunkEventArgs(null!, isFinal: false);
        Assert.That(args.Chunk, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ToolProgressEventArgs_AllFieldsSurfaced()
    {
        var args = new ToolProgressEventArgs(
            step: 3,
            toolName: "get_dimensions",
            status: "completed",
            description: "fetched 12 dimensions",
            durationMs: 412);

        Assert.That(args.Step, Is.EqualTo(3));
        Assert.That(args.ToolName, Is.EqualTo("get_dimensions"));
        Assert.That(args.Status, Is.EqualTo("completed"));
        Assert.That(args.Description, Is.EqualTo("fetched 12 dimensions"));
        Assert.That(args.DurationMs, Is.EqualTo(412));
    }

    [Test]
    public void ToolProgressEventArgs_NullToolName_PreservedForThinkingTurns()
    {
        var args = new ToolProgressEventArgs(1, null, "thinking", "Model is reasoning");
        Assert.That(args.ToolName, Is.Null);
        Assert.That(args.Status, Is.EqualTo("thinking"));
        Assert.That(args.DurationMs, Is.Null);
    }

    [Test]
    public void FakeHost_FireStateChanged_ReachesSubscriber()
    {
        var host = new FakeTaskPaneHost();
        int fireCount = 0;
        host.StateChanged += (_, _) => fireCount++;

        host.RaiseStateChanged();
        host.RaiseStateChanged();

        Assert.That(fireCount, Is.EqualTo(2));
    }

    [Test]
    public void FakeHost_StreamChunkArgs_ReachSubscriber()
    {
        var host = new FakeTaskPaneHost();
        var seen = new List<StreamChunkEventArgs>();
        host.StreamChunkReceived += (_, args) => seen.Add(args);

        host.RaiseStreamChunk("hi", isFinal: false);
        host.RaiseStreamChunk("done", isFinal: true);

        Assert.That(seen, Has.Count.EqualTo(2));
        Assert.That(seen[0].Chunk, Is.EqualTo("hi"));
        Assert.That(seen[1].IsFinal, Is.True);
    }

    [Test]
    public void FakeHost_PendingWriteApproval_RoutesByWriteId()
    {
        var host = new FakeTaskPaneHost();
        var write = new PendingWriteAction { WriteId = "w-001", ToolName = "set_dimension_value" };
        host.AddPendingWrite(write);

        host.ApplyPendingWrite("w-001");

        Assert.That(host.AppliedIds, Has.Member("w-001"));
    }

    [Test]
    public void FakeHost_ChatHistorySnapshot_IsAppendOnly()
    {
        var host = new FakeTaskPaneHost();
        host.AddChat(new ChatEntry { UserMessage = "q1", AssistantMessage = "a1" });
        host.AddChat(new ChatEntry { UserMessage = "q2", AssistantMessage = "a2" });

        Assert.That(host.ChatHistory, Has.Count.EqualTo(2));
        Assert.That(host.ChatHistory[0].UserMessage, Is.EqualTo("q1"));
        Assert.That(host.ChatHistory[1].UserMessage, Is.EqualTo("q2"));
    }

    // ─── Test fake ─────────────────────────────────────────────────────────

    private sealed class FakeTaskPaneHost : ITaskPaneHost
    {
        private readonly List<ChatEntry> _chat = new();
        private readonly List<PendingWriteAction> _writes = new();

        public List<string> AppliedIds { get; } = new();
        public List<string> CancelledIds { get; } = new();
        public List<string> Submissions { get; } = new();

        public SessionContext? CurrentContext => null;
        public IReadOnlyList<ChatEntry> ChatHistory => _chat;
        public IReadOnlyList<PendingWriteAction> PendingWrites => _writes;
        public string DocumentSummary => "(fake)";
        public string SourceLabel => "(fake)";

        public event EventHandler? StateChanged;
        public event EventHandler<StreamChunkEventArgs>? StreamChunkReceived;
        public event EventHandler<ToolProgressEventArgs>? ToolProgress;

        public void SubmitUserQuery(string query, CancellationToken cancellation) => Submissions.Add(query);
        public void CancelCurrentRun() { /* no-op */ }
        public void ApplyPendingWrite(string writeId) => AppliedIds.Add(writeId);
        public void CancelPendingWrite(string writeId) => CancelledIds.Add(writeId);

        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
        public void RaiseStreamChunk(string chunk, bool isFinal)
            => StreamChunkReceived?.Invoke(this, new StreamChunkEventArgs(chunk, isFinal));
        public void RaiseToolProgress(int step, string? tool, string status, string desc, long? ms = null)
            => ToolProgress?.Invoke(this, new ToolProgressEventArgs(step, tool, status, desc, ms));

        public void AddChat(ChatEntry entry) => _chat.Add(entry);
        public void AddPendingWrite(PendingWriteAction action) => _writes.Add(action);
    }
}
