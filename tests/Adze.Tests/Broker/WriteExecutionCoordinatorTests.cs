using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Broker.Orchestration;
using Adze.Tools.Write;
using Adze.Tests.Helpers;

namespace Adze.Tests.Broker;

[TestFixture]
public class WriteExecutionCoordinatorTests
{
    private WriteExecutionCoordinator _coordinator = null!;
    private StateDiffService _diffService = null!;
    private DefaultVerificationPolicy _verificationPolicy = null!;

    [SetUp]
    public void SetUp()
    {
        _diffService = new StateDiffService();
        _verificationPolicy = new DefaultVerificationPolicy();
        _coordinator = new WriteExecutionCoordinator(_diffService, _verificationPolicy);
    }

    [Test]
    public void Execute_UserApproves_RunsFullLifecycle()
    {
        var tool = new SetCustomPropertyTool();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        SessionContext context = SessionContextFactory.CreateWithCustomProperties();
        var beforeSnapshot = new StateSnapshot
        {
            Target = new WriteTargetDescriptor { Kind = WriteTargetKind.CustomProperty, TargetName = "Material" },
            Items = new List<SnapshotItem> { new() { Path = "Material", Value = "" } }
        };
        var afterSnapshot = new StateSnapshot
        {
            Target = new WriteTargetDescriptor { Kind = WriteTargetKind.CustomProperty, TargetName = "Material" },
            Items = new List<SnapshotItem> { new() { Path = "Material", Value = "Steel" } }
        };

        // Simulate successful apply
        SessionContext refreshedContext = SessionContextFactory.CreateWithCustomProperties();
        refreshedContext.Properties["document_custom.Material"] = "Steel";

        WriteExecutionOutcome outcome = _coordinator.Execute(
            tool,
            parameters,
            context,
            captureBefore: _ => beforeSnapshot,
            captureAfter: _ => afterSnapshot,
            applyOnUiThread: (_, p) => new WriteApplyResult
            {
                Success = true,
                UndoLabel = tool.BuildUndoLabel(p),
                AppliedValues = new Dictionary<string, string> { ["Material"] = "Steel" }
            },
            refreshContext: () => refreshedContext,
            requestApproval: (_, __) => ApprovalDecision.Apply,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(WriteOutcomeStatus.Success, outcome.Status);
        Assert.AreEqual("set_custom_property", outcome.ToolName);
        Assert.IsNotNull(outcome.Preview);
        Assert.IsNotNull(outcome.ApplyResult);
        Assert.IsNotNull(outcome.Verification);
        Assert.IsNotNull(outcome.Decision);
        Assert.IsNotNull(outcome.Diff);
    }

    [Test]
    public void Execute_UserCancels_ReturnsCancelled()
    {
        var tool = new SetCustomPropertyTool();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        SessionContext context = SessionContextFactory.CreateWithPart();

        WriteExecutionOutcome outcome = _coordinator.Execute(
            tool,
            parameters,
            context,
            captureBefore: _ => new StateSnapshot(),
            captureAfter: _ => new StateSnapshot(),
            applyOnUiThread: (_, __) => throw new InvalidOperationException("Should not reach apply"),
            refreshContext: () => context,
            requestApproval: (_, __) => ApprovalDecision.Cancel,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(WriteOutcomeStatus.Cancelled, outcome.Status);
        Assert.IsNotNull(outcome.Preview);
        Assert.IsNull(outcome.ApplyResult);
    }

    [Test]
    public void Execute_ApplyFails_ReturnsApplyFailed()
    {
        var tool = new SetDimensionValueTool();
        var parameters = new SetDimensionValueParameters
        {
            DimensionFullName = "D1@Sketch1",
            NewValue = 60.0
        };

        SessionContext context = SessionContextFactory.CreateWithDimensions();

        WriteExecutionOutcome outcome = _coordinator.Execute(
            tool,
            parameters,
            context,
            captureBefore: _ => new StateSnapshot(),
            captureAfter: _ => new StateSnapshot(),
            applyOnUiThread: (_, __) => new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Dimension is driven."
            },
            refreshContext: () => context,
            requestApproval: (_, __) => ApprovalDecision.Apply,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(WriteOutcomeStatus.ApplyFailed, outcome.Status);
        Assert.That(outcome.ErrorMessage, Does.Contain("Dimension is driven"));
    }

    [Test]
    public void Execute_CancellationBeforeApproval_ReturnsCancelled()
    {
        var tool = new SetCustomPropertyTool();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        SessionContext context = SessionContextFactory.CreateWithPart();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        WriteExecutionOutcome outcome = _coordinator.Execute(
            tool,
            parameters,
            context,
            captureBefore: _ => new StateSnapshot(),
            captureAfter: _ => new StateSnapshot(),
            applyOnUiThread: (_, __) => throw new InvalidOperationException("Should not reach"),
            refreshContext: () => context,
            requestApproval: (_, __) => throw new InvalidOperationException("Should not reach"),
            cancellationToken: cts.Token);

        Assert.AreEqual(WriteOutcomeStatus.Cancelled, outcome.Status);
    }

    [Test]
    public void Execute_VerificationFails_ReturnsVerificationFailed()
    {
        var tool = new SetDimensionValueTool();
        var parameters = new SetDimensionValueParameters
        {
            DimensionFullName = "D1@Sketch1",
            NewValue = 60.0
        };

        SessionContext context = SessionContextFactory.CreateWithDimensions();
        // Refreshed context still has old value — verification will fail
        SessionContext refreshedContext = SessionContextFactory.CreateWithDimensions();

        WriteExecutionOutcome outcome = _coordinator.Execute(
            tool,
            parameters,
            context,
            captureBefore: _ => new StateSnapshot(),
            captureAfter: _ => new StateSnapshot(),
            applyOnUiThread: (_, __) => new WriteApplyResult
            {
                Success = true,
                UndoLabel = "Adze: set D1@Sketch1 = 60",
                AppliedValues = new Dictionary<string, string> { ["D1@Sketch1"] = "60" }
            },
            refreshContext: () => refreshedContext,
            requestApproval: (_, __) => ApprovalDecision.Apply,
            cancellationToken: CancellationToken.None);

        // Dimension is still 50 in refreshed context, so verification fails
        Assert.AreEqual(WriteOutcomeStatus.VerificationFailed, outcome.Status);
        Assert.IsNotNull(outcome.Decision);
        Assert.IsTrue(outcome.Decision!.ShouldRollback);
    }

    [Test]
    public void Execute_UserRequestsModify_ReturnsCancelledWithMessage()
    {
        var tool = new SetCustomPropertyTool();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        SessionContext context = SessionContextFactory.CreateWithPart();

        WriteExecutionOutcome outcome = _coordinator.Execute(
            tool,
            parameters,
            context,
            captureBefore: _ => new StateSnapshot(),
            captureAfter: _ => new StateSnapshot(),
            applyOnUiThread: (_, __) => throw new InvalidOperationException("Should not reach"),
            refreshContext: () => context,
            requestApproval: (_, __) => ApprovalDecision.Modify,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(WriteOutcomeStatus.Cancelled, outcome.Status);
        Assert.That(outcome.ErrorMessage, Does.Contain("modification"));
    }

    [Test]
    public void Execute_SnapshotFailsGracefully_StillCompletes()
    {
        var tool = new SetCustomPropertyTool();
        var parameters = new SetCustomPropertyParameters
        {
            PropertyName = "Material",
            PropertyValue = "Steel"
        };

        SessionContext context = SessionContextFactory.CreateWithCustomProperties();
        SessionContext refreshedContext = SessionContextFactory.CreateWithCustomProperties();
        refreshedContext.Properties["document_custom.Material"] = "Steel";

        WriteExecutionOutcome outcome = _coordinator.Execute(
            tool,
            parameters,
            context,
            captureBefore: _ => throw new Exception("Snapshot service not available"),
            captureAfter: _ => throw new Exception("Snapshot service not available"),
            applyOnUiThread: (_, p) => new WriteApplyResult
            {
                Success = true,
                UndoLabel = "test",
                AppliedValues = new Dictionary<string, string> { ["Material"] = "Steel" }
            },
            refreshContext: () => refreshedContext,
            requestApproval: (_, __) => ApprovalDecision.Apply,
            cancellationToken: CancellationToken.None);

        // Should still succeed even without snapshots
        Assert.AreEqual(WriteOutcomeStatus.Success, outcome.Status);
        Assert.IsNull(outcome.BeforeSnapshot);
        Assert.IsNull(outcome.AfterSnapshot);
        Assert.IsNull(outcome.Diff);
    }
}
