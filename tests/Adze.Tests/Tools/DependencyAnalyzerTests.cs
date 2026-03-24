using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Adze.Contracts.Models;
using Adze.Tools.Write;
using Adze.Tests.Helpers;

namespace Adze.Tests.Tools;

[TestFixture]
public class DependencyAnalyzerTests
{
    private static SessionContext CreateWithFeatureTree(params (string name, string kind, string state)[] features)
    {
        var context = SessionContextFactory.CreateWithPart();
        context.FeatureTree = new FeatureTreeInfo
        {
            Features = features.Select(f => new FeatureNode
            {
                Name = f.name,
                Kind = f.kind,
                State = f.state
            }).ToList()
        };
        return context;
    }

    [Test]
    public void AnalyzeSuppression_FeatureNotFound_ReturnsWarning()
    {
        var context = CreateWithFeatureTree(("Sketch1", "sketch", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "NonExistent");

        Assert.That(result.Warnings, Has.Some.Contains("not found"));
    }

    [Test]
    public void AnalyzeSuppression_NoSubsequentFeatures_NoCascade()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Sketch1");

        Assert.AreEqual(CascadeRisk.None, result.CascadeRisk);
        Assert.AreEqual(0, result.AffectedFeatures.Count);
    }

    [Test]
    public void AnalyzeSuppression_EarlyFeature_HighCascadeRisk()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"),
            ("Boss-Extrude1", "extrusion", "active"),
            ("Fillet1", "fillet", "active"),
            ("Shell1", "shell", "active"),
            ("Cut1", "cut", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Sketch1");

        Assert.AreEqual(CascadeRisk.High, result.CascadeRisk);
        Assert.That(result.Warnings, Has.Some.Contains("subsequent feature"));
    }

    [Test]
    public void AnalyzeSuppression_SketchWithExtrusions_HighRisk()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"),
            ("Boss-Extrude1", "extrusion", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Sketch1");

        Assert.AreEqual(CascadeRisk.High, result.CascadeRisk);
        Assert.That(result.Warnings, Has.Some.Contains("sketch"));
    }

    [Test]
    public void AnalyzeSuppression_ExtrusionWithFillet_DetectsDependency()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"),
            ("Boss-Extrude1", "extrusion", "active"),
            ("Fillet1", "fillet", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Boss-Extrude1");

        Assert.That(result.AffectedFeatures.Any(f => f.Name == "Fillet1"), Is.True);
        Assert.That(result.AffectedFeatures[0].Relationship, Does.Contain("fillet"));
    }

    [Test]
    public void AnalyzeSuppression_WithDimensionReferences()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"),
            ("Boss-Extrude1", "extrusion", "active"));
        context.Dimensions = new DimensionsInfo
        {
            Count = 2,
            Items = new List<DimensionNode>
            {
                new DimensionNode { Name = "D1", FullName = "D1@Sketch1", Value = 50.0 },
                new DimensionNode { Name = "D3", FullName = "D3@Boss-Extrude1", Value = 10.0 }
            }
        };

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Sketch1");

        Assert.AreEqual(1, result.AffectedDimensions.Count);
        Assert.That(result.Warnings, Has.Some.Contains("dimension"));
    }

    [Test]
    public void AnalyzeSuppression_WithMateReferences()
    {
        var context = CreateWithFeatureTree(
            ("Part1", "component", "active"),
            ("Part2", "component", "active"));
        context.Mates = new MatesInfo
        {
            Count = 1,
            Items = new List<MateNode>
            {
                new MateNode { Name = "Coincident1", Kind = "coincident", Components = new List<string> { "Part1", "Part2" } }
            }
        };

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Part1");

        Assert.AreEqual(1, result.AffectedMates.Count);
        Assert.AreEqual(CascadeRisk.High, result.CascadeRisk);
        Assert.That(result.Warnings, Has.Some.Contains("mate"));
    }

    [Test]
    public void AnalyzeSuppression_SuppressedFeaturesExcluded()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"),
            ("Boss-Extrude1", "extrusion", "suppressed"),
            ("Fillet1", "fillet", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Sketch1");

        // Boss-Extrude1 is already suppressed, so only Fillet1 should appear
        Assert.IsFalse(result.AffectedFeatures.Any(f => f.Name == "Boss-Extrude1"));
    }

    [Test]
    public void AnalyzeDimensionChange_ParsesFeatureName()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"),
            ("Boss-Extrude1", "extrusion", "active"),
            ("Fillet1", "fillet", "active"),
            ("Fillet2", "fillet", "active"),
            ("Shell1", "shell", "active"),
            ("Cut1", "cut", "active"),
            ("Mirror1", "mirror", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeDimensionChange(context, "D1@Sketch1");

        Assert.AreEqual(CascadeRisk.Medium, result.CascadeRisk);
        Assert.That(result.Warnings, Has.Some.Contains("rebuild"));
    }

    [Test]
    public void AnalyzeDimensionChange_NoDimensionFeature_LowRisk()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeDimensionChange(context, "D1@NonExistent");

        Assert.AreEqual(CascadeRisk.Low, result.CascadeRisk);
    }

    [Test]
    public void AnalyzeDimensionChange_InvalidFormat_LowRisk()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"));

        DependencyPreview result = DependencyAnalyzer.AnalyzeDimensionChange(context, "badformat");

        Assert.AreEqual(CascadeRisk.Low, result.CascadeRisk);
    }

    [Test]
    public void CascadeRisk_None_ForIsolatedFeature()
    {
        var context = CreateWithFeatureTree(
            ("Sketch1", "sketch", "active"),
            ("Boss-Extrude1", "extrusion", "active"),
            ("Fillet1", "fillet", "active"));

        // Fillet at the end has no dependents
        DependencyPreview result = DependencyAnalyzer.AnalyzeSuppression(context, "Fillet1");

        Assert.AreEqual(CascadeRisk.None, result.CascadeRisk);
    }
}
