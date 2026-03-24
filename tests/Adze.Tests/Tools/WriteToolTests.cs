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
    public void Preview_WithConfiguration_MentionsConfigInSummary()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        var parameters = new SetDimensionValueParameters
        {
            DimensionFullName = "D1@Sketch1",
            NewValue = 60.0,
            ConfigurationName = "Variant-A"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Summary, Does.Contain("Variant-A"));
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

        Assert.That(preview.Warnings.Count, Is.GreaterThan(0), "Suppress on a sketch with dependents should produce cascade warnings");
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
    public void Preview_WithConfiguration_MentionsConfigInSummary()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new SuppressFeatureParameters
        {
            FeatureName = "Boss-Extrude1",
            ConfigurationName = "Variant-B"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Summary, Does.Contain("Variant-B"));
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
    public void BuildWriteToolDefinitions_Returns7Tools()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildWriteToolDefinitions();

        Assert.AreEqual(7, definitions.Count);
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
    public void BuildAllToolDefinitions_Returns17Tools()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildAllToolDefinitions();

        Assert.AreEqual(17, definitions.Count);
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

[TestFixture]
public class RenameObjectToolTests
{
    private RenameObjectTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new RenameObjectTool();
    }

    [Test]
    public void Preview_ExistingFeature_ShowsNameChange()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new RenameObjectParameters
        {
            ObjectType = "feature",
            CurrentName = "Boss-Extrude1",
            NewName = "MainBody"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("rename_object", preview.ToolName);
        Assert.AreEqual(1, preview.Changes.Count);
        Assert.AreEqual("Boss-Extrude1", preview.Changes[0].BeforeValue);
        Assert.AreEqual("MainBody", preview.Changes[0].AfterValue);
    }

    [Test]
    public void Preview_MissingFeature_AddsWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new RenameObjectParameters
        {
            ObjectType = "feature",
            CurrentName = "NonExistent",
            NewName = "NewName"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("not found"));
    }

    [Test]
    public void Preview_SameNameWarns()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new RenameObjectParameters
        {
            ObjectType = "feature",
            CurrentName = "Boss-Extrude1",
            NewName = "Boss-Extrude1"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("same as the current name"));
    }

    [Test]
    public void Preview_NameCollision_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new RenameObjectParameters
        {
            ObjectType = "feature",
            CurrentName = "Boss-Extrude1",
            NewName = "Cut-Extrude1" // Already exists
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("already exists"));
    }

    [Test]
    public void Preview_EmptyCurrentName_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new RenameObjectParameters
        {
            ObjectType = "feature",
            CurrentName = "",
            NewName = "NewName"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("empty"));
    }

    [Test]
    public void Preview_EmptyNewName_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new RenameObjectParameters
        {
            ObjectType = "feature",
            CurrentName = "Boss-Extrude1",
            NewName = ""
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("empty"));
    }

    [Test]
    public void Preview_DimensionType_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var parameters = new RenameObjectParameters
        {
            ObjectType = "dimension",
            CurrentName = "D1",
            NewName = "Width"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("not supported"));
    }

    [Test]
    public void Preview_WithDimensionReferences_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        context.FeatureTree = new FeatureTreeInfo
        {
            Features = new List<FeatureNode>
            {
                new FeatureNode { Name = "Sketch1", Kind = "sketch", State = "active" },
                new FeatureNode { Name = "Boss-Extrude1", Kind = "extrusion", State = "active" }
            }
        };
        var parameters = new RenameObjectParameters
        {
            ObjectType = "feature",
            CurrentName = "Sketch1",
            NewName = "BaseSketch"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("dimension"));
    }

    [Test]
    public void BuildUndoLabel_ContainsNames()
    {
        var parameters = new RenameObjectParameters
        {
            CurrentName = "OldName",
            NewName = "NewName"
        };

        string label = _tool.BuildUndoLabel(parameters);

        Assert.That(label, Does.Contain("OldName"));
        Assert.That(label, Does.Contain("NewName"));
    }

    [Test]
    public void Verify_FeatureRenamed_Confirmed()
    {
        SessionContext refreshed = SessionContextFactory.CreateWithFeatures();
        // Add "MainBody" to the feature tree as if the rename succeeded
        refreshed.FeatureTree.Features.Add(new FeatureNode { Name = "MainBody", Kind = "Extrusion", State = "active" });

        var applyResult = new WriteApplyResult
        {
            Success = true,
            UndoLabel = "test",
            AppliedValues = new Dictionary<string, string> { ["Boss-Extrude1"] = "MainBody" }
        };

        WriteVerification verification = _tool.Verify(refreshed, applyResult);

        Assert.IsTrue(verification.ChangeConfirmed);
    }

    [Test]
    public void ToolDefinitionBuilder_IncludesRenameObject()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildWriteToolDefinitions();

        var renameTool = definitions.FirstOrDefault(d => d.Name == "rename_object");
        Assert.IsNotNull(renameTool);
        Assert.That(renameTool!.Description, Does.Contain("Renames"));
    }

    [Test]
    public void AgentToolDispatcher_RenameObject_ReturnsPreview()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        var dispatcher = new Adze.Broker.Orchestration.AgentToolDispatcher();
        var execContext = new Adze.Broker.Abstractions.ToolExecutionContext
        {
            SessionContext = context
        };

        var args = new Dictionary<string, object?>
        {
            ["current_name"] = "Boss-Extrude1",
            ["new_name"] = "MainBody",
            ["object_type"] = "feature"
        };

        var result = dispatcher.Execute("rename_object", args, execContext);

        Assert.IsFalse(result.IsError);
        Assert.That(result.OutputJson, Does.Contain("rename_object"));
        Assert.That(result.OutputJson, Does.Contain("preview"));
    }
}

[TestFixture]
public class InsertComponentToolTests
{
    private InsertComponentTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new InsertComponentTool();
    }

    [Test]
    public void Preview_ValidAssembly_ShowsInsertion()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        var parameters = new InsertComponentParameters
        {
            ComponentPath = @"C:\test\NewPart.SLDPRT",
            X = 100, Y = 0, Z = 50
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("insert_component", preview.ToolName);
        Assert.That(preview.Summary, Does.Contain("NewPart.SLDPRT"));
        Assert.AreEqual(1, preview.Changes.Count);
        Assert.That(preview.Changes[0].AfterValue, Does.Contain("100"));
    }

    [Test]
    public void Preview_NotAnAssembly_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        var parameters = new InsertComponentParameters
        {
            ComponentPath = @"C:\test\Part1.SLDPRT"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("assembly"));
    }

    [Test]
    public void Preview_EmptyPath_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        var parameters = new InsertComponentParameters { ComponentPath = "" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("empty"));
    }

    [Test]
    public void Preview_WrongExtension_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        var parameters = new InsertComponentParameters
        {
            ComponentPath = @"C:\test\document.txt"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains(".SLDPRT"));
    }

    [Test]
    public void Preview_DuplicateComponent_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        var parameters = new InsertComponentParameters
        {
            ComponentPath = @"C:\test\Part1.SLDPRT" // Already in reference graph
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("already in the assembly"));
    }

    [Test]
    public void Preview_AlwaysShowsElevatedWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        var parameters = new InsertComponentParameters
        {
            ComponentPath = @"C:\test\NewPart.SLDPRT"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("elevated"));
    }

    [Test]
    public void Preview_WithConfiguration_ShowsInChanges()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        var parameters = new InsertComponentParameters
        {
            ComponentPath = @"C:\test\NewPart.SLDPRT",
            ConfigurationName = "Variant-A"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual(2, preview.Changes.Count);
        Assert.That(preview.Changes[1].AfterValue, Does.Contain("Variant-A"));
    }

    [Test]
    public void BuildUndoLabel_ContainsFileName()
    {
        var parameters = new InsertComponentParameters
        {
            ComponentPath = @"C:\test\Bracket.SLDPRT"
        };

        string label = _tool.BuildUndoLabel(parameters);

        Assert.That(label, Does.Contain("Bracket.SLDPRT"));
    }

    [Test]
    public void Verify_ComponentFound_Confirmed()
    {
        SessionContext refreshed = SessionContextFactory.CreateWithAssembly();
        refreshed.ReferenceGraph.DirectItems.Add(new ReferenceNode
        {
            Name = "NewPart.SLDPRT",
            Path = @"C:\test\NewPart.SLDPRT",
            ExistsOnDisk = true
        });

        var applyResult = new WriteApplyResult
        {
            Success = true,
            UndoLabel = "test",
            AppliedValues = new Dictionary<string, string> { ["NewPart.SLDPRT"] = "NewPart-1" }
        };

        WriteVerification verification = _tool.Verify(refreshed, applyResult);

        Assert.IsTrue(verification.ChangeConfirmed);
    }

    [Test]
    public void ToolDefinitionBuilder_IncludesInsertComponent()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildWriteToolDefinitions();

        var insertTool = definitions.FirstOrDefault(d => d.Name == "insert_component");
        Assert.IsNotNull(insertTool);
        Assert.That(insertTool!.Description, Does.Contain("assembly"));
    }

    [Test]
    public void AgentToolDispatcher_InsertComponent_ReturnsPreview()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        var dispatcher = new Adze.Broker.Orchestration.AgentToolDispatcher();
        var execContext = new Adze.Broker.Abstractions.ToolExecutionContext
        {
            SessionContext = context
        };

        var args = new Dictionary<string, object?>
        {
            ["component_path"] = @"C:\test\NewPart.SLDPRT",
            ["x"] = 0,
            ["y"] = 0,
            ["z"] = 0
        };

        var result = dispatcher.Execute("insert_component", args, execContext);

        Assert.IsFalse(result.IsError);
        Assert.That(result.OutputJson, Does.Contain("insert_component"));
        Assert.That(result.OutputJson, Does.Contain("preview"));
    }
}

[TestFixture]
public class CreateDrawingViewToolTests
{
    private CreateDrawingViewTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _tool = new CreateDrawingViewTool();
    }

    [Test]
    public void Preview_ValidDrawing_ShowsViewCreation()
    {
        SessionContext context = SessionContextFactory.CreateWithDrawing();
        var parameters = new CreateDrawingViewParameters
        {
            ViewType = "front",
            X = 0.15, Y = 0.15, Scale = 2.0
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual("create_drawing_view", preview.ToolName);
        Assert.That(preview.Summary, Does.Contain("front"));
        Assert.AreEqual(1, preview.Changes.Count);
        Assert.That(preview.Changes[0].AfterValue, Does.Contain("2"));
    }

    [Test]
    public void Preview_NotADrawing_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        var parameters = new CreateDrawingViewParameters { ViewType = "front" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("drawing"));
    }

    [Test]
    public void Preview_InvalidViewType_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithDrawing();
        var parameters = new CreateDrawingViewParameters { ViewType = "diagonal" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("Unknown view type"));
    }

    [Test]
    public void Preview_InvalidScale_Warns()
    {
        SessionContext context = SessionContextFactory.CreateWithDrawing();
        var parameters = new CreateDrawingViewParameters { ViewType = "front", Scale = -1.0 };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("outside the expected range"));
    }

    [Test]
    public void Preview_WithModelPath_ShowsInChanges()
    {
        SessionContext context = SessionContextFactory.CreateWithDrawing();
        var parameters = new CreateDrawingViewParameters
        {
            ViewType = "isometric",
            ModelPath = @"C:\test\Part1.SLDPRT"
        };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.AreEqual(2, preview.Changes.Count);
        Assert.That(preview.Changes[1].AfterValue, Does.Contain("Part1.SLDPRT"));
    }

    [Test]
    public void Preview_AlwaysShowsElevatedWarning()
    {
        SessionContext context = SessionContextFactory.CreateWithDrawing();
        var parameters = new CreateDrawingViewParameters { ViewType = "top" };

        WritePreview preview = _tool.Preview(context, parameters);

        Assert.That(preview.Warnings, Has.Some.Contains("elevated"));
    }

    [Test]
    public void Preview_AllViewTypesValid()
    {
        SessionContext context = SessionContextFactory.CreateWithDrawing();
        string[] validTypes = { "front", "back", "top", "bottom", "left", "right", "isometric", "trimetric", "dimetric" };

        foreach (string viewType in validTypes)
        {
            var parameters = new CreateDrawingViewParameters { ViewType = viewType };
            WritePreview preview = _tool.Preview(context, parameters);

            // Should not have "Unknown view type" warning
            Assert.That(preview.Warnings, Has.None.Contains("Unknown view type"),
                "View type '" + viewType + "' should be valid");
        }
    }

    [Test]
    public void BuildUndoLabel_ContainsViewType()
    {
        var parameters = new CreateDrawingViewParameters { ViewType = "isometric" };

        string label = _tool.BuildUndoLabel(parameters);

        Assert.That(label, Does.Contain("isometric"));
    }

    [Test]
    public void Verify_ViewCreated_Confirmed()
    {
        SessionContext refreshed = SessionContextFactory.CreateWithDrawing();
        var applyResult = new WriteApplyResult
        {
            Success = true,
            UndoLabel = "test",
            AppliedValues = new Dictionary<string, string> { ["front"] = "Drawing View1" }
        };

        WriteVerification verification = _tool.Verify(refreshed, applyResult);

        Assert.IsTrue(verification.ChangeConfirmed);
    }

    [Test]
    public void ToolDefinitionBuilder_IncludesCreateDrawingView()
    {
        var definitions = Adze.Broker.Formatting.ToolDefinitionBuilder.BuildWriteToolDefinitions();

        var drawingTool = definitions.FirstOrDefault(d => d.Name == "create_drawing_view");
        Assert.IsNotNull(drawingTool);
        Assert.That(drawingTool!.Description, Does.Contain("drawing"));
    }

    [Test]
    public void AgentToolDispatcher_CreateDrawingView_ReturnsPreview()
    {
        SessionContext context = SessionContextFactory.CreateWithDrawing();
        var dispatcher = new Adze.Broker.Orchestration.AgentToolDispatcher();
        var execContext = new Adze.Broker.Abstractions.ToolExecutionContext
        {
            SessionContext = context
        };

        var args = new Dictionary<string, object?>
        {
            ["view_type"] = "front",
            ["x"] = 0.15,
            ["y"] = 0.15
        };

        var result = dispatcher.Execute("create_drawing_view", args, execContext);

        Assert.IsFalse(result.IsError);
        Assert.That(result.OutputJson, Does.Contain("create_drawing_view"));
        Assert.That(result.OutputJson, Does.Contain("preview"));
    }
}
