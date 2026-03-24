using System.Collections.Generic;
using NUnit.Framework;
using Adze.Contracts.Models;
using Adze.Tools.Abstractions;

namespace Adze.Tests.Broker;

[TestFixture]
public class ElevatedConfirmationTests
{
    [Test]
    public void ToolCapabilityClass_HasAllExpectedValues()
    {
        Assert.AreEqual(5, System.Enum.GetValues(typeof(ToolCapabilityClass)).Length);
    }

    [Test]
    public void ApprovalRequirement_HasElevatedConfirmation()
    {
        Assert.IsTrue(System.Enum.IsDefined(typeof(ApprovalRequirement), ApprovalRequirement.ElevatedConfirmation));
    }

    [Test]
    public void ToolCapabilityMetadata_DefaultsToReadSafe()
    {
        var metadata = new ToolCapabilityMetadata();

        Assert.AreEqual(ToolCapabilityClass.ReadSafe, metadata.CapabilityClass);
        Assert.AreEqual(ApprovalRequirement.None, metadata.ApprovalRequirement);
    }

    [Test]
    public void ToolCapabilityMetadata_HardWriteAdvanced_HasElevatedConfirmation()
    {
        var metadata = new ToolCapabilityMetadata
        {
            CapabilityClass = ToolCapabilityClass.HardWriteAdvanced,
            ApprovalRequirement = ApprovalRequirement.ElevatedConfirmation,
            RequiresUiThread = true,
            RequiresRebuild = true,
            SupportsUndoGrouping = true,
            MustCaptureSnapshot = true
        };

        Assert.AreEqual(ToolCapabilityClass.HardWriteAdvanced, metadata.CapabilityClass);
        Assert.AreEqual(ApprovalRequirement.ElevatedConfirmation, metadata.ApprovalRequirement);
        Assert.IsTrue(metadata.RequiresUiThread);
    }

    [Test]
    public void ToolCapabilityMetadata_AllowedInBatchPlan_DefaultsTrue()
    {
        var metadata = new ToolCapabilityMetadata();
        Assert.IsTrue(metadata.AllowedInBatchPlan);
    }

    [Test]
    public void WriteTargetKind_HasObjectRename()
    {
        Assert.IsTrue(System.Enum.IsDefined(typeof(WriteTargetKind), WriteTargetKind.ObjectRename));
    }
}
