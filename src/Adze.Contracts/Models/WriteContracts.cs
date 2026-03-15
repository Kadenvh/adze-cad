using System;
using System.Collections.Generic;

namespace Adze.Contracts.Models;

// ── Target description ──

public enum WriteTargetKind
{
    Dimension,
    CustomProperty,
    FeatureSuppression
}

public sealed class WriteTargetDescriptor
{
    public WriteTargetKind Kind { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public string? OwnerName { get; set; }

    public string? ConfigurationName { get; set; }
}

// ── Snapshots ──

public sealed class SnapshotItem
{
    public string Path { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string ValueType { get; set; } = "string";
}

public sealed class StateSnapshot
{
    public WriteTargetDescriptor Target { get; set; } = new();

    public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<SnapshotItem> Items { get; set; } = new();
}

// ── Diffs ──

public sealed class StateDiffItem
{
    public string Path { get; set; } = string.Empty;

    public string BeforeValue { get; set; } = string.Empty;

    public string AfterValue { get; set; } = string.Empty;
}

public sealed class StateDiff
{
    public WriteTargetDescriptor Target { get; set; } = new();

    public List<StateDiffItem> Changes { get; set; } = new();

    public bool HasChanges => Changes.Count > 0;
}

// ── Write preview ──

public sealed class WriteChangeItem
{
    public string TargetLabel { get; set; } = string.Empty;

    public string BeforeValue { get; set; } = string.Empty;

    public string AfterValue { get; set; } = string.Empty;
}

public sealed class WritePreview
{
    public string ToolName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<WriteChangeItem> Changes { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

// ── Write apply/verify ──

public sealed class WriteApplyResult
{
    public bool Success { get; set; }

    public string UndoLabel { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public Dictionary<string, string> AppliedValues { get; set; } = new();
}

public sealed class WriteVerification
{
    public bool ChangeConfirmed { get; set; }

    public bool RebuildSucceeded { get; set; } = true;

    public List<string> RebuildWarnings { get; set; } = new();

    public List<StateDiffItem> ObservedChanges { get; set; } = new();

    public List<StateDiffItem> UnexpectedChanges { get; set; } = new();
}

// ── Verification decision ──

public enum VerificationOutcome
{
    Accepted,
    SuggestRollback,
    Failed
}

public sealed class VerificationDecision
{
    public VerificationOutcome Outcome { get; set; } = VerificationOutcome.Accepted;

    public string Reason { get; set; } = string.Empty;

    public bool ShouldRollback => Outcome == VerificationOutcome.SuggestRollback;
}

// ── Approval ──

public enum ApprovalDecision
{
    Apply,
    Cancel,
    Modify
}

// ── Full execution outcome ──

public enum WriteOutcomeStatus
{
    Success,
    Cancelled,
    ApplyFailed,
    VerificationFailed,
    RolledBack,
    PolicyBlocked
}

public sealed class WriteExecutionOutcome
{
    public WriteOutcomeStatus Status { get; set; } = WriteOutcomeStatus.Success;

    public string ToolName { get; set; } = string.Empty;

    public string UndoLabel { get; set; } = string.Empty;

    public WritePreview? Preview { get; set; }

    public WriteApplyResult? ApplyResult { get; set; }

    public WriteVerification? Verification { get; set; }

    public VerificationDecision? Decision { get; set; }

    public StateSnapshot? BeforeSnapshot { get; set; }

    public StateSnapshot? AfterSnapshot { get; set; }

    public StateDiff? Diff { get; set; }

    public string? ErrorMessage { get; set; }
}

// ── Write trace record ──

public sealed class WriteTraceRecord
{
    public string TraceId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string UndoLabel { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public WriteOutcomeStatus Outcome { get; set; }

    public WriteTargetDescriptor Target { get; set; } = new();

    public StateSnapshot? BeforeSnapshot { get; set; }

    public StateSnapshot? AfterSnapshot { get; set; }

    public StateDiff? Diff { get; set; }

    public VerificationDecision? VerificationDecision { get; set; }

    public string UserId { get; set; } = string.Empty;
}
