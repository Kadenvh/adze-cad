using System;
using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Host.Infrastructure;
using NUnit.Framework;
using HostChatEntry = Adze.Host.Infrastructure.ChatEntry;
using HostPendingWrite = Adze.Host.Infrastructure.PendingWriteAction;
using PublicChatEntry = Adze.Contracts.Models.ChatEntry;
using PublicPendingWrite = Adze.Contracts.Models.PendingWriteAction;

namespace Adze.Tests.Host;

/// <summary>
/// Verifies the host-internal → public type mapping that <see cref="HostStateAdapter"/>
/// performs at the <see cref="Adze.Contracts.Abstractions.ITaskPaneHost"/>
/// boundary. The host-internal records (HostState.ChatEntry,
/// HostState.PendingWriteAction) are intentionally separate from the public
/// surface contracts so HostState's existing API stays bit-for-bit identical
/// during the v1.1 cutover.
///
/// These tests guard against:
///   - Field drift (rename or removal silently dropping a value at the boundary)
///   - WriteId routing failure (sidebar Apply/Cancel relies on a stable round-trip)
///   - Default-value regressions (null → empty-string preserved on every field)
/// </summary>
[TestFixture]
public sealed class HostStateAdapterMappingTests
{
    [Test]
    public void ToPublicChatEntry_CopiesEveryField_Lossless()
    {
        var stamp = DateTimeOffset.UtcNow;
        var host = new HostChatEntry
        {
            UserMessage = "list dimensions",
            AssistantMessage = "Here are the dimensions...",
            Source = "model_openai",
            Footer = "Tokens: 42",
            TimestampUtc = stamp
        };

        PublicChatEntry pub = HostStateAdapter.ToPublicChatEntry(host);

        Assert.That(pub.UserMessage, Is.EqualTo("list dimensions"));
        Assert.That(pub.AssistantMessage, Is.EqualTo("Here are the dimensions..."));
        Assert.That(pub.Source, Is.EqualTo("model_openai"));
        Assert.That(pub.Footer, Is.EqualTo("Tokens: 42"));
        Assert.That(pub.TimestampUtc, Is.EqualTo(stamp));
    }

    [Test]
    public void ToPublicChatEntry_NullStringsBecomeEmpty_NeverNull()
    {
        // Defensive: host-internal allows null on string fields via property
        // initializers, but the public contract is non-nullable. Mapping must
        // not propagate nulls to the UI binding layer.
        var host = new HostChatEntry
        {
            UserMessage = null!,
            AssistantMessage = null!,
            Source = null!,
            Footer = null!
        };

        PublicChatEntry pub = HostStateAdapter.ToPublicChatEntry(host);

        Assert.That(pub.UserMessage, Is.EqualTo(string.Empty));
        Assert.That(pub.AssistantMessage, Is.EqualTo(string.Empty));
        Assert.That(pub.Source, Is.EqualTo(string.Empty));
        Assert.That(pub.Footer, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ToPublicPendingWrite_CopiesEveryField_AndAssignsStableWriteId()
    {
        var args = new Dictionary<string, object?>
        {
            ["dimension_full_name"] = "D1@Sketch1",
            ["new_value"] = 42.0
        };
        var preview = new WritePreview { ToolName = "set_dimension_value", Summary = "D1 → 42mm" };
        preview.Changes.Add(new WriteChangeItem { TargetLabel = "D1", BeforeValue = "10", AfterValue = "42" });
        preview.Warnings.Add("Cascade risk: low");

        var host = new HostPendingWrite
        {
            ToolName = "set_dimension_value",
            Arguments = args,
            Preview = preview,
            Applied = false,
            Cancelled = false,
            ResultMessage = null,
            IsElevated = false,
            UndoLabel = "Modify D1@Sketch1"
        };

        PublicPendingWrite pub = HostStateAdapter.ToPublicPendingWrite(host, index: 3);

        Assert.That(pub.WriteId, Is.EqualTo("pw-3"),
            "WriteId is the only routing handle the sidebar has — must be stable per index.");
        Assert.That(pub.ToolName, Is.EqualTo("set_dimension_value"));
        Assert.That(pub.Arguments, Is.SameAs(args), "Arguments map should be passed by reference, not deep-copied.");
        Assert.That(pub.Preview, Is.SameAs(preview));
        Assert.That(pub.Preview.Changes, Has.Count.EqualTo(1));
        Assert.That(pub.Preview.Warnings, Has.Count.EqualTo(1));
        Assert.That(pub.Applied, Is.False);
        Assert.That(pub.Cancelled, Is.False);
        Assert.That(pub.IsElevated, Is.False);
        Assert.That(pub.UndoLabel, Is.EqualTo("Modify D1@Sketch1"));
    }

    [Test]
    public void ToPublicPendingWrite_PreservesElevationAndAppliedState()
    {
        // Class 3 (elevated) writes drive distinct UI styling — mapping must
        // surface the IsElevated flag so the sidebar renders the orange
        // "Elevated Change" border. Applied + ResultMessage round-trip too.
        var host = new HostPendingWrite
        {
            ToolName = "insert_component",
            Preview = new WritePreview { Summary = "Insert bracket-asm.SLDASM" },
            Applied = true,
            Cancelled = false,
            ResultMessage = "Component 'bracket-1' inserted.",
            IsElevated = true,
            UndoLabel = "Insert Component"
        };

        PublicPendingWrite pub = HostStateAdapter.ToPublicPendingWrite(host, index: 0);

        Assert.That(pub.WriteId, Is.EqualTo("pw-0"));
        Assert.That(pub.IsElevated, Is.True, "Elevated flag must round-trip — drives confirmation card styling.");
        Assert.That(pub.Applied, Is.True);
        Assert.That(pub.ResultMessage, Is.EqualTo("Component 'bracket-1' inserted."));
        Assert.That(pub.UndoLabel, Is.EqualTo("Insert Component"));
    }

    [Test]
    public void ToPublicPendingWrite_NullCollections_NormalisedToEmpty()
    {
        // Defensive: a malformed host-internal entry (e.g. failure path that
        // never populated Arguments/Preview) must still produce a valid public
        // contract instance — UI rendering paths assume non-null collections.
        var host = new HostPendingWrite
        {
            ToolName = "set_custom_property",
            Arguments = null!,
            Preview = null!,
            UndoLabel = null!
        };

        PublicPendingWrite pub = HostStateAdapter.ToPublicPendingWrite(host, index: 7);

        Assert.That(pub.WriteId, Is.EqualTo("pw-7"));
        Assert.That(pub.Arguments, Is.Not.Null);
        Assert.That(pub.Preview, Is.Not.Null);
        Assert.That(pub.UndoLabel, Is.EqualTo(string.Empty));
    }
}
