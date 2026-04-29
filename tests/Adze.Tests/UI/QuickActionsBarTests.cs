using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.UI.V2;
using NUnit.Framework;

namespace Adze.Tests.UI;

/// <summary>
/// Tests for the v1.1 quick-actions chip row that sits beneath the prompt
/// input on the Chat tab. We verify:
///   - All 5 default chips are present and labeled correctly
///   - Each chip dispatches the expected query through ITaskPaneHost
///   - The trailing "+" placeholder chip is rendered (so total = 6 buttons)
///   - InvokeQuickAction safely no-ops on empty input
///
/// We don't show the WinForms control, just construct it on the test thread.
/// Click events are simulated via <see cref="Button.PerformClick"/>.
/// </summary>
[TestFixture]
public sealed class QuickActionsBarTests
{
    [Test]
    public void DefaultActions_ProduceFiveLabeledChips()
    {
        var host = new RecordingHost();
        using var bar = new QuickActionsBar(host);

        Assert.That(bar.Chips, Has.Count.EqualTo(5),
            "Expected 5 built-in quick-action chips (Diagnose / Mates / Dimensions / Properties / Explain).");

        string[] labels = bar.Chips.Select(c => c.Text).ToArray();
        Assert.That(labels, Is.EqualTo(new[]
        {
            "Diagnose",
            "Mates",
            "Dimensions",
            "Properties",
            "Explain"
        }));
    }

    [Test]
    public void ChipClick_DispatchesExpectedQueryThroughHost()
    {
        var host = new RecordingHost();
        using var bar = new QuickActionsBar(host);

        Button diagnose = bar.Chips.First(c => c.Text == "Diagnose");
        diagnose.PerformClick();

        Assert.That(host.Submissions, Has.Count.EqualTo(1));
        Assert.That(host.Submissions[0], Is.EqualTo("Diagnose this model"));
    }

    [Test]
    public void EachDefaultChip_DispatchesItsOwnDistinctQuery()
    {
        var host = new RecordingHost();
        using var bar = new QuickActionsBar(host);

        foreach (Button chip in bar.Chips)
        {
            chip.PerformClick();
        }

        // 5 chips, 5 dispatches, all distinct strings, none empty.
        Assert.That(host.Submissions, Has.Count.EqualTo(5));
        Assert.That(host.Submissions.Distinct().Count(), Is.EqualTo(5));
        Assert.That(host.Submissions.All(s => !string.IsNullOrWhiteSpace(s)), Is.True);
    }

    [Test]
    public void ChipQueryWiring_MatchesPublicDefaultActionsTable()
    {
        // Source-of-truth check: chip labels and the queries they fire match
        // QuickActionsBar.DefaultActions exactly. If a future PR rearranges
        // either side, this test catches the drift.
        var host = new RecordingHost();
        using var bar = new QuickActionsBar(host);

        for (int i = 0; i < QuickActionsBar.DefaultActions.Length; i++)
        {
            QuickActionsBar.QuickAction expected = QuickActionsBar.DefaultActions[i];
            Button chip = bar.Chips[i];
            Assert.That(chip.Text, Is.EqualTo(expected.Label),
                $"Chip {i} label mismatch.");

            host.Submissions.Clear();
            chip.PerformClick();
            Assert.That(host.Submissions, Has.Count.EqualTo(1));
            Assert.That(host.Submissions[0], Is.EqualTo(expected.Query),
                $"Chip {i} query mismatch.");
        }
    }

    [Test]
    public void InvokeQuickAction_EmptyOrWhitespaceQuery_IsNoOp()
    {
        var host = new RecordingHost();
        using var bar = new QuickActionsBar(host);

        bar.InvokeQuickAction(string.Empty);
        bar.InvokeQuickAction("   ");
        bar.InvokeQuickAction(null!);

        Assert.That(host.Submissions, Is.Empty,
            "Empty / whitespace / null queries must NOT dispatch to the host.");
    }

    [Test]
    public void Constructor_NullHost_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => new QuickActionsBar(null!));
    }

    [Test]
    public void CustomActions_AreHonored_OverridingDefaults()
    {
        var host = new RecordingHost();
        var custom = new[]
        {
            new QuickActionsBar.QuickAction("Custom-A", "alpha query"),
            new QuickActionsBar.QuickAction("Custom-B", "beta query")
        };

        using var bar = new QuickActionsBar(host, custom);

        Assert.That(bar.Chips, Has.Count.EqualTo(2));
        Assert.That(bar.Chips[0].Text, Is.EqualTo("Custom-A"));
        bar.Chips[1].PerformClick();
        Assert.That(host.Submissions, Has.Count.EqualTo(1));
        Assert.That(host.Submissions[0], Is.EqualTo("beta query"));
    }

    [Test]
    public void TrailingPlusButton_IsPresent_BeyondTheChips()
    {
        var host = new RecordingHost();
        using var bar = new QuickActionsBar(host);

        // Total controls = chips + 1 trailing "+" placeholder.
        Assert.That(bar.Controls.Count, Is.EqualTo(bar.Chips.Count + 1));

        // Last control is the "+" placeholder.
        Control last = bar.Controls[bar.Controls.Count - 1];
        Assert.That(last, Is.TypeOf<Button>());
        Assert.That(((Button)last).Text, Is.EqualTo("+"));
    }

    [Test]
    public void Bar_WrapsContents_SoNarrowSidebarsRenderTwoRows()
    {
        var host = new RecordingHost();
        using var bar = new QuickActionsBar(host);

        Assert.That(bar.WrapContents, Is.True,
            "Quick-actions row must wrap on narrow sidebars per design brief.");
        Assert.That(bar.FlowDirection, Is.EqualTo(FlowDirection.LeftToRight));
    }

    // ─── Test fake ─────────────────────────────────────────────────────────

    /// <summary>Captures every <c>SubmitUserQuery</c> call in order.</summary>
    private sealed class RecordingHost : ITaskPaneHost
    {
        public List<string> Submissions { get; } = new();

        public SessionContext? CurrentContext => null;
        public IReadOnlyList<ChatEntry> ChatHistory => System.Array.Empty<ChatEntry>();
        public IReadOnlyList<PendingWriteAction> PendingWrites => System.Array.Empty<PendingWriteAction>();
        public string DocumentSummary => "(test)";
        public string SourceLabel => "(test)";

        public event System.EventHandler? StateChanged
        {
            add { /* no-op */ }
            remove { /* no-op */ }
        }
        public event System.EventHandler<StreamChunkEventArgs>? StreamChunkReceived
        {
            add { /* no-op */ }
            remove { /* no-op */ }
        }
        public event System.EventHandler<ToolProgressEventArgs>? ToolProgress
        {
            add { /* no-op */ }
            remove { /* no-op */ }
        }

        public void SubmitUserQuery(string query, CancellationToken cancellation)
            => Submissions.Add(query);
        public void CancelCurrentRun() { /* no-op */ }
        public void ApplyPendingWrite(string writeId) { /* no-op */ }
        public void CancelPendingWrite(string writeId) { /* no-op */ }
    }
}
