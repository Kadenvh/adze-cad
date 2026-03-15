using System;
using System.Collections.Generic;
using NUnit.Framework;
using Adze.Contracts.Models;
using Adze.Broker.Orchestration;

namespace Adze.Tests.Broker;

[TestFixture]
public class StateDiffServiceTests
{
    private StateDiffService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new StateDiffService();
    }

    [Test]
    public void Compare_IdenticalSnapshots_ReturnsNoDiff()
    {
        var target = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "D1@Sketch1" };
        var before = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "value", Value = "50.0", ValueType = "double" }
            }
        };
        var after = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "value", Value = "50.0", ValueType = "double" }
            }
        };

        StateDiff diff = _service.Compare(before, after);

        Assert.IsFalse(diff.HasChanges);
        Assert.AreEqual(0, diff.Changes.Count);
    }

    [Test]
    public void Compare_ValueChanged_ReturnsSingleDiff()
    {
        var target = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "D1@Sketch1" };
        var before = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "value", Value = "50.0" }
            }
        };
        var after = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "value", Value = "60.0" }
            }
        };

        StateDiff diff = _service.Compare(before, after);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.Changes.Count);
        Assert.AreEqual("value", diff.Changes[0].Path);
        Assert.AreEqual("50.0", diff.Changes[0].BeforeValue);
        Assert.AreEqual("60.0", diff.Changes[0].AfterValue);
    }

    [Test]
    public void Compare_ItemAdded_ReturnsAddDiff()
    {
        var target = new WriteTargetDescriptor { Kind = WriteTargetKind.CustomProperty, TargetName = "Material" };
        var before = new StateSnapshot { Target = target, Items = new List<SnapshotItem>() };
        var after = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "Material", Value = "Steel" }
            }
        };

        StateDiff diff = _service.Compare(before, after);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.Changes.Count);
        Assert.AreEqual(string.Empty, diff.Changes[0].BeforeValue);
        Assert.AreEqual("Steel", diff.Changes[0].AfterValue);
    }

    [Test]
    public void Compare_ItemRemoved_ReturnsRemoveDiff()
    {
        var target = new WriteTargetDescriptor { Kind = WriteTargetKind.CustomProperty, TargetName = "Material" };
        var before = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "Material", Value = "Steel" }
            }
        };
        var after = new StateSnapshot { Target = target, Items = new List<SnapshotItem>() };

        StateDiff diff = _service.Compare(before, after);

        Assert.IsTrue(diff.HasChanges);
        Assert.AreEqual(1, diff.Changes.Count);
        Assert.AreEqual("Steel", diff.Changes[0].BeforeValue);
        Assert.AreEqual(string.Empty, diff.Changes[0].AfterValue);
    }

    [Test]
    public void Compare_MultipleItems_TracksEach()
    {
        var target = new WriteTargetDescriptor { Kind = WriteTargetKind.CustomProperty, TargetName = "props" };
        var before = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "Material", Value = "Steel" },
                new() { Path = "Weight", Value = "10.5" },
                new() { Path = "Color", Value = "Red" }
            }
        };
        var after = new StateSnapshot
        {
            Target = target,
            Items = new List<SnapshotItem>
            {
                new() { Path = "Material", Value = "Aluminum" },
                new() { Path = "Weight", Value = "10.5" },
                new() { Path = "Finish", Value = "Matte" }
            }
        };

        StateDiff diff = _service.Compare(before, after);

        Assert.IsTrue(diff.HasChanges);
        // Material changed, Color removed, Finish added = 3 changes
        Assert.AreEqual(3, diff.Changes.Count);
    }

    [Test]
    public void Compare_EmptySnapshots_ReturnsNoDiff()
    {
        var target = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "test" };
        var before = new StateSnapshot { Target = target, Items = new List<SnapshotItem>() };
        var after = new StateSnapshot { Target = target, Items = new List<SnapshotItem>() };

        StateDiff diff = _service.Compare(before, after);

        Assert.IsFalse(diff.HasChanges);
    }

    [Test]
    public void Compare_UsesAfterTarget()
    {
        var beforeTarget = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "before" };
        var afterTarget = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "after" };
        var before = new StateSnapshot { Target = beforeTarget, Items = new List<SnapshotItem>() };
        var after = new StateSnapshot { Target = afterTarget, Items = new List<SnapshotItem>() };

        StateDiff diff = _service.Compare(before, after);

        Assert.AreEqual("after", diff.Target.TargetName);
    }
}

[TestFixture]
public class DefaultVerificationPolicyTests
{
    private DefaultVerificationPolicy _policy = null!;
    private SessionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _policy = new DefaultVerificationPolicy();
        _context = Adze.Tests.Helpers.SessionContextFactory.CreateMinimal();
    }

    [Test]
    public void Evaluate_ChangeConfirmed_NoIssues_Accepts()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = true,
            RebuildSucceeded = true
        };

        VerificationDecision decision = _policy.Evaluate("set_dimension_value", verification, _context);

        Assert.AreEqual(VerificationOutcome.Accepted, decision.Outcome);
        Assert.IsFalse(decision.ShouldRollback);
    }

    [Test]
    public void Evaluate_ChangeNotConfirmed_SuggestsRollback()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = false,
            RebuildSucceeded = true
        };

        VerificationDecision decision = _policy.Evaluate("set_dimension_value", verification, _context);

        Assert.AreEqual(VerificationOutcome.SuggestRollback, decision.Outcome);
        Assert.IsTrue(decision.ShouldRollback);
        Assert.That(decision.Reason, Does.Contain("not observed"));
    }

    [Test]
    public void Evaluate_RebuildFailed_SuggestsRollback()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = true,
            RebuildSucceeded = false
        };

        VerificationDecision decision = _policy.Evaluate("set_dimension_value", verification, _context);

        Assert.AreEqual(VerificationOutcome.SuggestRollback, decision.Outcome);
        Assert.IsTrue(decision.ShouldRollback);
        Assert.That(decision.Reason, Does.Contain("Rebuild failed"));
    }

    [Test]
    public void Evaluate_UnexpectedChanges_SuggestsRollback()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = true,
            RebuildSucceeded = true,
            UnexpectedChanges = new List<StateDiffItem>
            {
                new() { Path = "D2@Sketch1", BeforeValue = "30.0", AfterValue = "25.0" }
            }
        };

        VerificationDecision decision = _policy.Evaluate("set_dimension_value", verification, _context);

        Assert.AreEqual(VerificationOutcome.SuggestRollback, decision.Outcome);
        Assert.That(decision.Reason, Does.Contain("Unexpected changes"));
        Assert.That(decision.Reason, Does.Contain("D2@Sketch1"));
    }

    [Test]
    public void Evaluate_RebuildWarnings_AcceptsWithNote()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = true,
            RebuildSucceeded = true,
            RebuildWarnings = new List<string> { "Mate1 is over-defined" }
        };

        VerificationDecision decision = _policy.Evaluate("set_dimension_value", verification, _context);

        Assert.AreEqual(VerificationOutcome.Accepted, decision.Outcome);
        Assert.That(decision.Reason, Does.Contain("Rebuild warnings"));
        Assert.That(decision.Reason, Does.Contain("Mate1"));
    }

    [Test]
    public void Evaluate_ChangeNotConfirmed_TakesPrecedenceOverWarnings()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = false,
            RebuildSucceeded = true,
            RebuildWarnings = new List<string> { "some warning" }
        };

        VerificationDecision decision = _policy.Evaluate("set_dimension_value", verification, _context);

        Assert.AreEqual(VerificationOutcome.SuggestRollback, decision.Outcome);
    }

    [Test]
    public void Evaluate_RebuildFailed_TakesPrecedenceOverUnexpected()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = true,
            RebuildSucceeded = false,
            UnexpectedChanges = new List<StateDiffItem>
            {
                new() { Path = "something", BeforeValue = "a", AfterValue = "b" }
            }
        };

        VerificationDecision decision = _policy.Evaluate("set_dimension_value", verification, _context);

        // Rebuild failure takes precedence
        Assert.AreEqual(VerificationOutcome.SuggestRollback, decision.Outcome);
        Assert.That(decision.Reason, Does.Contain("Rebuild failed"));
    }

    [Test]
    public void Evaluate_CustomProperty_NoRebuild_CleanSuccess()
    {
        var verification = new WriteVerification
        {
            ChangeConfirmed = true,
            RebuildSucceeded = true
        };

        VerificationDecision decision = _policy.Evaluate("set_custom_property", verification, _context);

        Assert.AreEqual(VerificationOutcome.Accepted, decision.Outcome);
        Assert.AreEqual("Change verified successfully.", decision.Reason);
    }
}

[TestFixture]
public class WriteContractTests
{
    [Test]
    public void WriteTargetDescriptor_DefaultsToEmptyStrings()
    {
        var descriptor = new WriteTargetDescriptor();

        Assert.AreEqual(string.Empty, descriptor.TargetName);
        Assert.IsNull(descriptor.OwnerName);
        Assert.IsNull(descriptor.ConfigurationName);
    }

    [Test]
    public void StateSnapshot_DefaultsToNow()
    {
        var snapshot = new StateSnapshot();

        Assert.That(snapshot.CapturedUtc, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(0, snapshot.Items.Count);
    }

    [Test]
    public void StateDiff_HasChanges_FalseWhenEmpty()
    {
        var diff = new StateDiff();
        Assert.IsFalse(diff.HasChanges);
    }

    [Test]
    public void StateDiff_HasChanges_TrueWhenPopulated()
    {
        var diff = new StateDiff
        {
            Changes = new List<StateDiffItem>
            {
                new() { Path = "value", BeforeValue = "a", AfterValue = "b" }
            }
        };
        Assert.IsTrue(diff.HasChanges);
    }

    [Test]
    public void WritePreview_DefaultsToEmptyCollections()
    {
        var preview = new WritePreview();

        Assert.AreEqual(0, preview.Changes.Count);
        Assert.AreEqual(0, preview.Warnings.Count);
        Assert.AreEqual(string.Empty, preview.ToolName);
    }

    [Test]
    public void WriteApplyResult_DefaultsToFalseSuccess()
    {
        var result = new WriteApplyResult();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(string.Empty, result.UndoLabel);
    }

    [Test]
    public void WriteVerification_DefaultsToClean()
    {
        var verification = new WriteVerification();

        Assert.IsFalse(verification.ChangeConfirmed);
        Assert.IsTrue(verification.RebuildSucceeded);
        Assert.AreEqual(0, verification.RebuildWarnings.Count);
        Assert.AreEqual(0, verification.ObservedChanges.Count);
        Assert.AreEqual(0, verification.UnexpectedChanges.Count);
    }

    [Test]
    public void VerificationDecision_ShouldRollback_MatchesOutcome()
    {
        var accepted = new VerificationDecision { Outcome = VerificationOutcome.Accepted };
        var rollback = new VerificationDecision { Outcome = VerificationOutcome.SuggestRollback };
        var failed = new VerificationDecision { Outcome = VerificationOutcome.Failed };

        Assert.IsFalse(accepted.ShouldRollback);
        Assert.IsTrue(rollback.ShouldRollback);
        Assert.IsFalse(failed.ShouldRollback);
    }

    [Test]
    public void WriteExecutionOutcome_DefaultsToSuccess()
    {
        var outcome = new WriteExecutionOutcome();

        Assert.AreEqual(WriteOutcomeStatus.Success, outcome.Status);
        Assert.IsNull(outcome.Preview);
        Assert.IsNull(outcome.ApplyResult);
    }

    [Test]
    public void WriteTraceRecord_DefaultsToEmptyStrings()
    {
        var record = new WriteTraceRecord();

        Assert.AreEqual(string.Empty, record.TraceId);
        Assert.AreEqual(string.Empty, record.ToolName);
        Assert.AreEqual(string.Empty, record.UserId);
    }

    [Test]
    public void WriteChangeItem_StoresBeforeAfter()
    {
        var item = new WriteChangeItem
        {
            TargetLabel = "D1@Sketch1",
            BeforeValue = "50 mm",
            AfterValue = "60 mm"
        };

        Assert.AreEqual("D1@Sketch1", item.TargetLabel);
        Assert.AreEqual("50 mm", item.BeforeValue);
        Assert.AreEqual("60 mm", item.AfterValue);
    }
}

[TestFixture]
public class WriteTraceRecordBuilderTests
{
    [Test]
    public void Build_CreatesRecordFromOutcome()
    {
        var outcome = new WriteExecutionOutcome
        {
            Status = WriteOutcomeStatus.Success,
            ToolName = "set_dimension_value",
            UndoLabel = "Adze: set D1@Sketch1 = 60mm",
            Preview = new WritePreview
            {
                ToolName = "set_dimension_value",
                Changes = new List<WriteChangeItem>
                {
                    new() { TargetLabel = "D1@Sketch1", BeforeValue = "50", AfterValue = "60" }
                }
            }
        };

        WriteTraceRecord record = Adze.Trace.Tracing.WriteTraceRecordBuilder.Build(outcome, "testuser");

        Assert.AreEqual("set_dimension_value", record.ToolName);
        Assert.AreEqual("Adze: set D1@Sketch1 = 60mm", record.UndoLabel);
        Assert.AreEqual(WriteOutcomeStatus.Success, record.Outcome);
        Assert.AreEqual("testuser", record.UserId);
        Assert.IsNotEmpty(record.TraceId);
        Assert.AreEqual(12, record.TraceId.Length);
    }

    [Test]
    public void Build_SetsTimestamp()
    {
        var outcome = new WriteExecutionOutcome
        {
            ToolName = "set_custom_property",
            UndoLabel = "Adze: set Material"
        };

        WriteTraceRecord record = Adze.Trace.Tracing.WriteTraceRecordBuilder.Build(outcome, "user1");

        Assert.That(record.TimestampUtc, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Build_IncludesSnapshotsAndDiffWhenPresent()
    {
        var beforeSnapshot = new StateSnapshot
        {
            Target = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "D1" },
            Items = new List<SnapshotItem> { new() { Path = "value", Value = "50" } }
        };

        var afterSnapshot = new StateSnapshot
        {
            Target = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "D1" },
            Items = new List<SnapshotItem> { new() { Path = "value", Value = "60" } }
        };

        var diff = new StateDiff
        {
            Target = new WriteTargetDescriptor { Kind = WriteTargetKind.Dimension, TargetName = "D1" },
            Changes = new List<StateDiffItem>
            {
                new() { Path = "value", BeforeValue = "50", AfterValue = "60" }
            }
        };

        var decision = new VerificationDecision
        {
            Outcome = VerificationOutcome.Accepted,
            Reason = "OK"
        };

        var outcome = new WriteExecutionOutcome
        {
            ToolName = "set_dimension_value",
            UndoLabel = "Adze: set D1",
            BeforeSnapshot = beforeSnapshot,
            AfterSnapshot = afterSnapshot,
            Diff = diff,
            Decision = decision
        };

        WriteTraceRecord record = Adze.Trace.Tracing.WriteTraceRecordBuilder.Build(outcome, "user");

        Assert.IsNotNull(record.BeforeSnapshot);
        Assert.IsNotNull(record.AfterSnapshot);
        Assert.IsNotNull(record.Diff);
        Assert.IsNotNull(record.VerificationDecision);
        Assert.AreEqual(VerificationOutcome.Accepted, record.VerificationDecision!.Outcome);
    }

    [Test]
    public void Build_HandlesEmptyOutcome()
    {
        var outcome = new WriteExecutionOutcome
        {
            Status = WriteOutcomeStatus.Cancelled,
            ToolName = "suppress_feature"
        };

        WriteTraceRecord record = Adze.Trace.Tracing.WriteTraceRecordBuilder.Build(outcome, "user");

        Assert.AreEqual(WriteOutcomeStatus.Cancelled, record.Outcome);
        Assert.AreEqual("suppress_feature", record.ToolName);
        Assert.IsNotNull(record.Target);
    }
}
