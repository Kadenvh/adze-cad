using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Adze.Contracts.Models;
using Adze.Tools.Write;
using Adze.Tests.Helpers;

namespace Adze.Tests.Tools;

[TestFixture]
public class SetCustomPropertyToolTests
{
    private SetCustomPropertyTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new SetCustomPropertyTool();
    }

    [Test]
    public void Preview_NewProperty_ShowsNotSetBefore()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("set_custom_property", preview.ToolName);
        Assert.AreEqual(1, preview.Changes.Count);
        Assert.AreEqual("(not set)", preview.Changes[0].BeforeValue);
        Assert.AreEqual("Steel", preview.Changes[0].AfterValue);
        Assert.That(preview.Summary, Does.Contain("Material"));
    }

    [Test]
    public void Preview_ExistingProperty_ShowsCurrentValue()
    {
        SessionContext context = SessionContextFactory.CreateWithCustomProperties();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Aluminum",
            Scope = "document"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("Steel", preview.Changes[0].BeforeValue);
        Assert.AreEqual("Aluminum", preview.Changes[0].AfterValue);
    }

    [Test]
    public void Preview_ReadOnlyDocument_AddsWarning()
    {
        SessionContext context = SessionContextFactory.CreateReadOnly();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("read-only"));
    }

    [Test]
    public void Preview_ConfigurationScope_IncludesConfigName()
    {
        SessionContext context = SessionContextFactory.CreateWithCustomProperties();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Weight",
            PropertyValue = "2.0 kg",
            Scope = "configuration",
            ConfigurationName = "Default"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual(1, preview.Changes.Count);
        Assert.That(preview.Changes[0].TargetLabel, Does.Contain("configuration"));
    }

    [Test]
    public void BuildUndoLabel_IncludesNameAndValue()
    {
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        string label = _tool.BuildUndoLabel(parameters);

        Assert.That(label, Does.Contain("Material"));
        Assert.That(label, Does.Contain("Steel"));
        Assert.That(label, Does.StartWith("Adze:"));
    }

    [Test]
    public void Verify_PropertyMatches_ConfirmsChange()
    {
        SessionContext context = SessionContextFactory.CreateWithCustomProperties();
        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = new Dictionary<string, string> { ["Material"] = "Steel" }
        };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.IsTrue(verification.ChangeConfirmed);
        Assert.IsTrue(verification.RebuildSucceeded);
    }

    [Test]
    public void Verify_FailedApply_DoesNotConfirm()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        var applyResult = new WriteApplyResult { Success = false };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.IsFalse(verification.ChangeConfirmed);
    }
}

[TestFixture]
public class SetDimensionValueToolTests
{
    private SetDimensionValueTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new SetDimensionValueTool();
    }

    [Test]
    public void Preview_ExistingDimension_ShowsCurrentValue()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        var parameters = new SetDimensionValueParameters
        {
            DimensionFullName = "D1@Sketch1",
            NewValue = 60.0
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("set_dimension_value", preview.ToolName);
        Assert.AreEqual(1, preview.Changes.Count);
        Assert.AreEqual("50", preview.Changes[0].BeforeValue);
        Assert.AreEqual("60", preview.Changes[0].AfterValue);
        Assert.AreEqual(0, preview.Warnings.Count);
    }

    [Test]
    public void Preview_MissingDimension_AddsWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        var parameters = new SetDimensionValueParameters
        {
            DimensionFullName = "D99@Sketch99",
            NewValue = 100.0
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("not found"));
        Assert.AreEqual("(unknown)", preview.Changes[0].BeforeValue);
    }

    [Test]
    public void Preview_ReadOnlyDocument_AddsWarning()
    {
        SessionContext context = SessionContextFactory.CreateReadOnly();
        context.Dimensions = new DimensionsInfo
        {
            Count = 1,
            Items = new List<DimensionNode>
            {
                new() { Name = "D1", FullName = "D1@Sketch1", Value = 50.0 }
            }
        };
        var parameters = new SetDimensionValueParameters
        {
            DimensionFullName = "D1@Sketch1",
            NewValue = 60.0
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("read-only"));
    }

    [Test]
    public void BuildUndoLabel_IncludesDimensionAndValue()
    {
        var parameters = new SetDimensionValueParameters
        {
            DimensionFullName = "D1@Sketch1",
            NewValue = 60.0
        };

        string label = _tool.BuildUndoLabel(parameters);

        Assert.That(label, Does.Contain("D1@Sketch1"));
        Assert.That(label, Does.Contain("60"));
    }

    [Test]
    public void Verify_DimensionMatches_ConfirmsChange()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        // Simulate dimension was updated to 60
        context.Dimensions.Items[0].Value = 60.0;

        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = new Dictionary<string, string> { ["D1@Sketch1"] = "60" }
        };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.IsTrue(verification.ChangeConfirmed);
    }

    [Test]
    public void Verify_DimensionMismatch_ReportsUnexpected()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        // Dimension stayed at 50 — change didn't stick
        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = new Dictionary<string, string> { ["D1@Sketch1"] = "60" }
        };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.IsFalse(verification.ChangeConfirmed);
        Assert.AreEqual(1, verification.UnexpectedChanges.Count);
    }

    [Test]
    public void Verify_RebuildWarnings_Included()
    {
        SessionContext context = SessionContextFactory.CreateWithDiagnosticIssues();
        context.Dimensions = new DimensionsInfo
        {
            Count = 1,
            Items = new List<DimensionNode>
            {
                new() { Name = "D1", FullName = "D1@Sketch1", Value = 60.0 }
            }
        };

        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = new Dictionary<string, string> { ["D1@Sketch1"] = "60" }
        };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.That(verification.RebuildWarnings.Count, Is.GreaterThan(0));
    }
}

[TestFixture]
public class SuppressFeatureToolTests
{
    private SuppressFeatureTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new SuppressFeatureTool();
    }

    [Test]
    public void Preview_ExistingFeature_ShowsStateChange()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new SuppressFeatureParameters { FeatureName = "Boss-Extrude1" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("suppress_feature", preview.ToolName);
        Assert.AreEqual(1, preview.Changes.Count);
        Assert.AreEqual("active", preview.Changes[0].BeforeValue);
        Assert.AreEqual("suppressed", preview.Changes[0].AfterValue);
    }

    [Test]
    public void Preview_AlreadySuppressed_AddsWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new SuppressFeatureParameters { FeatureName = "Fillet1" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("already suppressed"));
    }

    [Test]
    public void Preview_FeatureWithDependents_AddsCascadeWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        // Suppress Sketch1 which has subsequent active features
        var parameters = new SuppressFeatureParameters { FeatureName = "Sketch1" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("subsequent feature"));
    }

    [Test]
    public void Preview_MissingFeature_AddsWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new SuppressFeatureParameters { FeatureName = "NonExistent" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("not found"));
    }

    [Test]
    public void BuildUndoLabel_IncludesFeatureName()
    {
        var parameters = new SuppressFeatureParameters { FeatureName = "Boss-Extrude1" };

        string label = _tool.BuildUndoLabel(parameters);

        Assert.That(label, Does.Contain("Boss-Extrude1"));
        Assert.That(label, Does.Contain("suppress"));
    }

    [Test]
    public void Verify_FeatureSuppressed_ConfirmsChange()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        // Simulate feature was suppressed
        context.FeatureTree.Features.First(f => f.Name == "Boss-Extrude1").State = "suppressed";

        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = new Dictionary<string, string> { ["Boss-Extrude1"] = "suppressed" }
        };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.IsTrue(verification.ChangeConfirmed);
    }

    [Test]
    public void Verify_FeatureStillActive_DoesNotConfirm()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = new Dictionary<string, string> { ["Boss-Extrude1"] = "suppressed" }
        };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.IsFalse(verification.ChangeConfirmed);
    }
}

[TestFixture]
public class UnsuppressFeatureToolTests
{
    private UnsuppressFeatureTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new UnsuppressFeatureTool();
    }

    [Test]
    public void Preview_SuppressedFeature_ShowsStateChange()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new UnsuppressFeatureParameters { FeatureName = "Fillet1" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("unsuppress_feature", preview.ToolName);
        Assert.AreEqual(1, preview.Changes.Count);
        Assert.AreEqual("suppressed", preview.Changes[0].BeforeValue);
        Assert.AreEqual("active", preview.Changes[0].AfterValue);
    }

    [Test]
    public void Preview_AlreadyActive_AddsWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new UnsuppressFeatureParameters { FeatureName = "Boss-Extrude1" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("already active"));
    }

    [Test]
    public void BuildUndoLabel_IncludesFeatureName()
    {
        var parameters = new UnsuppressFeatureParameters { FeatureName = "Fillet1" };

        string label = _tool.BuildUndoLabel(parameters);

        Assert.That(label, Does.Contain("Fillet1"));
        Assert.That(label, Does.Contain("unsuppress"));
    }

    [Test]
    public void Verify_FeatureNowActive_ConfirmsChange()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        // Simulate feature was unsuppressed
        context.FeatureTree.Features.First(f => f.Name == "Fillet1").State = "active";

        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = new Dictionary<string, string> { ["Fillet1"] = "active" }
        };

        WriteVerification verification = _tool.Verify(context, applyResult);

        Assert.IsTrue(verification.ChangeConfirmed);
    }
}

[TestFixture]
public class WriteToolDefinitionBuilderTests
{
    [Test]
    public void BuildWriteToolDefinitions_Returns4Tools()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildWriteToolDefinitions();

        Assert.AreEqual(4, definitions.Count);
    }

    [Test]
    public void BuildWriteToolDefinitions_HasRequiredParameters()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildWriteToolDefinitions();

        var setProperty = definitions.First(d => d.Name == "set_custom_property");
        var required = setProperty.ParameterSchema["required"] as List<string>;
        Assert.IsNotNull(required);
        Assert.That(required, Does.Contain("property_name"));
        Assert.That(required, Does.Contain("property_value"));
    }

    [Test]
    public void BuildAllToolDefinitions_Returns14Tools()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildAllToolDefinitions();

        Assert.AreEqual(14, definitions.Count);
    }

    [Test]
    public void BuildWriteToolDefinitions_SuppressFeature_HasRequiredFeatureName()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildWriteToolDefinitions();

        var suppressTool = definitions.First(d => d.Name == "suppress_feature");
        var required = suppressTool.ParameterSchema["required"] as List<string>;
        Assert.IsNotNull(required);
        Assert.That(required, Does.Contain("feature_name"));
    }
}
