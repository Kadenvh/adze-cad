using System;
using System.Collections.Generic;
using Adze.Contracts.Models;

namespace Adze.Tools.Abstractions;

public enum ToolCapabilityClass
{
    ReadSafe,
    SoftWrite,
    HardWriteFirstWave,
    HardWriteAdvanced,
    DeferredHighRisk
}

public enum ApprovalRequirement
{
    None,
    StandardConfirmation,
    ElevatedConfirmation,
    Disallowed
}

public sealed class ToolCapabilityMetadata
{
    public ToolCapabilityClass CapabilityClass { get; set; } = ToolCapabilityClass.ReadSafe;

    public ApprovalRequirement ApprovalRequirement { get; set; } = ApprovalRequirement.None;

    public bool RequiresUiThread { get; set; }

    public bool RequiresRebuild { get; set; }

    public bool SupportsUndoGrouping { get; set; }

    public bool MustCaptureSnapshot { get; set; }

    public bool AllowedInBatchPlan { get; set; } = true;
}

public interface IToolDescriptor
{
    string Name { get; }

    string Description { get; }

    Type ParameterType { get; }

    Type ResultType { get; }

    ToolCapabilityMetadata Capability { get; }

    Dictionary<string, object?> BuildJsonSchema();
}

public interface IToolRegistry
{
    IReadOnlyList<IToolDescriptor> GetEnabledTools(SessionContext context);

    IToolDescriptor? GetByName(string toolName);
}
