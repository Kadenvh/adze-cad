using System.Collections.Generic;
using System.Web.Script.Serialization;
using Adze.Contracts.Models;
using Adze.Tests.Helpers;
using Adze.Trace.Serialization;
using NUnit.Framework;

namespace Adze.Tests.Trace;

/// <summary>
/// Round-trip coverage for SessionContext: serialize via <see cref="ModelJsonMapper.ToJson(SessionContext)"/>,
/// run through <see cref="JavaScriptSerializer"/> (the same serializer that writes/reads snapshot files), and
/// rehydrate via <see cref="ModelJsonMapper.ToSessionContext"/>. Asserts every field the SessionContextFactory
/// fixtures populate survives the round trip.
/// </summary>
[TestFixture]
public sealed class ModelJsonMapperRoundTripTests
{
    private static SessionContext RoundTrip(SessionContext original)
    {
        Dictionary<string, object?> jsonShape = ModelJsonMapper.ToJson(original);
        var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        string serialized = serializer.Serialize(jsonShape);
        return ModelJsonMapper.DeserializeSessionContextJson(serialized);
    }

    [Test]
    public void RoundTrip_Minimal_PreservesSessionAndPolicy()
    {
        SessionContext original = SessionContextFactory.CreateMinimal();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Session.RequestId, Is.EqualTo(original.Session.RequestId));
        Assert.That(restored.Session.UserMode, Is.EqualTo(original.Session.UserMode));
        Assert.That(restored.Document, Is.Null);
        Assert.That(restored.Policy.EnabledTools, Is.EquivalentTo(original.Policy.EnabledTools));
        Assert.That(restored.Policy.ToolUnlockTier, Is.EqualTo(original.Policy.ToolUnlockTier));
    }

    [Test]
    public void RoundTrip_Part_PreservesDocumentAndConfigurations()
    {
        SessionContext original = SessionContextFactory.CreateWithPart("PartA", @"C:\models\PartA.SLDPRT");

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Document, Is.Not.Null);
        Assert.That(restored.Document!.Type, Is.EqualTo("part"));
        Assert.That(restored.Document.Title, Is.EqualTo("PartA"));
        Assert.That(restored.Document.Path, Is.EqualTo(@"C:\models\PartA.SLDPRT"));
        Assert.That(restored.Document.ActiveConfiguration, Is.EqualTo("Default"));
        Assert.That(restored.Document.Units, Is.EqualTo("mm"));
        Assert.That(restored.Document.IsDirty, Is.False);
        Assert.That(restored.Document.IsReadOnly, Is.False);

        Assert.That(restored.Configurations.ActiveName, Is.EqualTo("Default"));
        Assert.That(restored.Configurations.Count, Is.EqualTo(1));
        Assert.That(restored.Configurations.Items, Has.Count.EqualTo(1));
        Assert.That(restored.Configurations.Items[0].Name, Is.EqualTo("Default"));
        Assert.That(restored.Configurations.Items[0].IsActive, Is.True);

        Assert.That(restored.Diagnostics.RebuildState, Is.EqualTo("clean"));
    }

    [Test]
    public void RoundTrip_ReadOnlyPart_PreservesIsReadOnly()
    {
        SessionContext original = SessionContextFactory.CreateReadOnly();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Document, Is.Not.Null);
        Assert.That(restored.Document!.IsReadOnly, Is.True);
    }

    [Test]
    public void RoundTrip_Assembly_PreservesMatesAndReferenceGraph()
    {
        SessionContext original = SessionContextFactory.CreateWithAssembly("AssyA", @"C:\models\AssyA.SLDASM");

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Document, Is.Not.Null);
        Assert.That(restored.Document!.Type, Is.EqualTo("assembly"));

        Assert.That(restored.Mates.Count, Is.EqualTo(2));
        Assert.That(restored.Mates.Items, Has.Count.EqualTo(2));
        Assert.That(restored.Mates.Items[0].Name, Is.EqualTo("Coincident1"));
        Assert.That(restored.Mates.Items[0].Kind, Is.EqualTo("Coincident"));
        Assert.That(restored.Mates.Items[0].EntityCount, Is.EqualTo(2));
        Assert.That(restored.Mates.Items[0].Components, Is.EquivalentTo(new[] { "Part1-1", "Part2-1" }));

        Assert.That(restored.ReferenceGraph.DirectCount, Is.EqualTo(2));
        Assert.That(restored.ReferenceGraph.TransitiveCount, Is.EqualTo(3));
        Assert.That(restored.ReferenceGraph.DirectItems, Has.Count.EqualTo(2));
        Assert.That(restored.ReferenceGraph.DirectItems[0].Name, Is.EqualTo("Part1.SLDPRT"));
        Assert.That(restored.ReferenceGraph.DirectItems[0].Path, Is.EqualTo(@"C:\test\Part1.SLDPRT"));
        Assert.That(restored.ReferenceGraph.DirectItems[0].ExistsOnDisk, Is.True);
    }

    [Test]
    public void RoundTrip_Drawing_PreservesDocumentTypeAndDirectReferences()
    {
        SessionContext original = SessionContextFactory.CreateWithDrawing("DrwA", @"C:\models\DrwA.SLDDRW");

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Document, Is.Not.Null);
        Assert.That(restored.Document!.Type, Is.EqualTo("drawing"));
        Assert.That(restored.Document.Title, Is.EqualTo("DrwA"));
        Assert.That(restored.ReferenceGraph.DirectCount, Is.EqualTo(1));
        Assert.That(restored.ReferenceGraph.DirectItems, Has.Count.EqualTo(1));
        Assert.That(restored.ReferenceGraph.DirectItems[0].Name, Is.EqualTo("Part1.SLDPRT"));
    }

    [Test]
    public void RoundTrip_FeatureTree_PreservesAnchorRadiusAndFeatureNodes()
    {
        SessionContext original = SessionContextFactory.CreateWithFeatures();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.FeatureTree.Anchor, Is.EqualTo("Boss-Extrude1"));
        Assert.That(restored.FeatureTree.Radius, Is.EqualTo(8));
        Assert.That(restored.FeatureTree.Features, Has.Count.EqualTo(original.FeatureTree.Features.Count));
        for (int i = 0; i < original.FeatureTree.Features.Count; i++)
        {
            Assert.That(restored.FeatureTree.Features[i].Name, Is.EqualTo(original.FeatureTree.Features[i].Name));
            Assert.That(restored.FeatureTree.Features[i].Kind, Is.EqualTo(original.FeatureTree.Features[i].Kind));
            Assert.That(restored.FeatureTree.Features[i].State, Is.EqualTo(original.FeatureTree.Features[i].State));
        }
    }

    [Test]
    public void RoundTrip_Dimensions_PreservesValuesAndUnitSource()
    {
        SessionContext original = SessionContextFactory.CreateWithDimensions();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Dimensions.Count, Is.EqualTo(3));
        Assert.That(restored.Dimensions.Items, Has.Count.EqualTo(3));
        Assert.That(restored.Dimensions.Items[0].Name, Is.EqualTo("D1"));
        Assert.That(restored.Dimensions.Items[0].FullName, Is.EqualTo("D1@Sketch1"));
        Assert.That(restored.Dimensions.Items[0].Value, Is.EqualTo(50.0));
        Assert.That(restored.Dimensions.Items[0].UnitSource, Is.EqualTo("document"));
        Assert.That(restored.Dimensions.Items[2].Value, Is.EqualTo(10.0));
    }

    [Test]
    public void RoundTrip_Selection_PreservesItems()
    {
        SessionContext original = SessionContextFactory.CreateWithSelection();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Selection.Count, Is.EqualTo(2));
        Assert.That(restored.Selection.Items, Has.Count.EqualTo(2));
        Assert.That(restored.Selection.Items[0].Kind, Is.EqualTo("Face"));
        Assert.That(restored.Selection.Items[0].Name, Is.EqualTo("Face1"));
        Assert.That(restored.Selection.Items[0].Owner, Is.EqualTo("Boss-Extrude1"));
        Assert.That(restored.Selection.Items[1].Kind, Is.EqualTo("Edge"));
    }

    [Test]
    public void RoundTrip_DiagnosticIssues_PreservesWarningsAndMissingReferences()
    {
        SessionContext original = SessionContextFactory.CreateWithDiagnosticIssues();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Diagnostics.RebuildState, Is.EqualTo("needs_rebuild"));
        Assert.That(restored.Diagnostics.Warnings, Is.EquivalentTo(original.Diagnostics.Warnings));
        Assert.That(restored.Diagnostics.MissingReferences, Is.EquivalentTo(original.Diagnostics.MissingReferences));
    }

    [Test]
    public void RoundTrip_CustomProperties_PreservesKeysAndStringValues()
    {
        SessionContext original = SessionContextFactory.CreateWithCustomProperties();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Properties, Has.Count.EqualTo(original.Properties.Count));
        foreach (KeyValuePair<string, object?> kvp in original.Properties)
        {
            Assert.That(restored.Properties.ContainsKey(kvp.Key), Is.True, $"missing key {kvp.Key}");
            Assert.That(restored.Properties[kvp.Key]?.ToString(), Is.EqualTo(kvp.Value?.ToString()));
        }
    }

    [Test]
    public void RoundTrip_BrokenReferences_PreservesIsBrokenAndExistsOnDisk()
    {
        SessionContext original = SessionContextFactory.CreateWithBrokenReferences();

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.ReferenceGraph.BrokenReferenceCount, Is.EqualTo(1));
        Assert.That(restored.ReferenceGraph.DirectItems, Has.Count.EqualTo(original.ReferenceGraph.DirectItems.Count));
        ReferenceNode? broken = restored.ReferenceGraph.DirectItems.Find(r => r.IsBroken);
        Assert.That(broken, Is.Not.Null);
        Assert.That(broken!.Name, Is.EqualTo("MissingPart.SLDPRT"));
        Assert.That(broken.ExistsOnDisk, Is.False);
    }

    [Test]
    public void RoundTrip_LargeAssembly_PreservesAllDimensionsAndMates()
    {
        SessionContext original = SessionContextFactory.CreateLargeAssembly(dimensionCount: 250, mateCount: 100, referenceCount: 75);

        SessionContext restored = RoundTrip(original);

        Assert.That(restored.Dimensions.Count, Is.EqualTo(250));
        Assert.That(restored.Dimensions.Items, Has.Count.EqualTo(250));
        Assert.That(restored.Mates.Count, Is.EqualTo(100));
        Assert.That(restored.Mates.Items, Has.Count.EqualTo(100));
        Assert.That(restored.ReferenceGraph.DirectCount, Is.EqualTo(75));
        Assert.That(restored.ReferenceGraph.DirectItems, Has.Count.EqualTo(75));
        Assert.That(restored.Dimensions.Items[249].Name, Is.EqualTo("D250"));
    }

    [Test]
    public void DeserializeSessionContextJson_EmptyJson_ReturnsEmptyContext()
    {
        SessionContext restored = ModelJsonMapper.DeserializeSessionContextJson(string.Empty);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored.Document, Is.Null);
        Assert.That(restored.Session.UserMode, Is.EqualTo("interactive"));
    }

    [Test]
    public void ToSessionContext_MissingAllSlices_UsesDefaults()
    {
        var payload = new Dictionary<string, object>();

        SessionContext restored = ModelJsonMapper.ToSessionContext(payload);

        Assert.That(restored.Session.UserMode, Is.EqualTo("interactive"));
        Assert.That(restored.Document, Is.Null);
        Assert.That(restored.Selection.Items, Is.Empty);
        Assert.That(restored.FeatureTree.Features, Is.Empty);
        Assert.That(restored.Configurations.Items, Is.Empty);
        Assert.That(restored.Dimensions.Items, Is.Empty);
        Assert.That(restored.Mates.Items, Is.Empty);
        Assert.That(restored.ReferenceGraph.DirectItems, Is.Empty);
        Assert.That(restored.Properties, Is.Empty);
        Assert.That(restored.Diagnostics.Warnings, Is.Empty);
        Assert.That(restored.Policy.EnabledTools, Is.Empty);
    }
}
