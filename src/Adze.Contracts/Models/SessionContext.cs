using System;
using System.Collections.Generic;
using Adze.Contracts.Enums;

namespace Adze.Contracts.Models;

public sealed class SessionContext
{
    public SessionInfo Session { get; set; } = new();

    public EnvironmentInfo Environment { get; set; } = new();

    public DocumentInfo? Document { get; set; }

    public SelectionInfo Selection { get; set; } = new();

    public FeatureTreeInfo FeatureTree { get; set; } = new();

    public ConfigurationsInfo Configurations { get; set; } = new();

    public DimensionsInfo Dimensions { get; set; } = new();

    public MatesInfo Mates { get; set; } = new();

    public ReferenceGraphInfo ReferenceGraph { get; set; } = new();

    public Dictionary<string, object?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DiagnosticsInfo Diagnostics { get; set; } = new();

    public PolicyInfo Policy { get; set; } = new();
}

public sealed class SessionInfo
{
    public string RequestId { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; }

    public ApprovalState ApprovalState { get; set; }

    public string UserMode { get; set; } = "interactive";
}

public sealed class EnvironmentInfo
{
    public string SolidWorksVersion { get; set; } = string.Empty;

    public string AddInVersion { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public bool DocumentManagerAvailable { get; set; }

    public bool DiagnosticsMode { get; set; }
}

public sealed class DocumentInfo
{
    public string Type { get; set; } = "none";

    public string Title { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string ActiveConfiguration { get; set; } = string.Empty;

    public string Units { get; set; } = string.Empty;

    public bool IsDirty { get; set; }

    public bool IsReadOnly { get; set; }
}

public sealed class SelectionInfo
{
    public int Count { get; set; }

    public List<SelectionItem> Items { get; set; } = new();
}

public sealed class SelectionItem
{
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;
}

public sealed class FeatureTreeInfo
{
    public string? Anchor { get; set; }

    public int Radius { get; set; }

    public List<FeatureNode> Features { get; set; } = new();
}

public sealed class FeatureNode
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;
}

public sealed class ConfigurationsInfo
{
    public string ActiveName { get; set; } = string.Empty;

    public int Count { get; set; }

    public List<ConfigurationItem> Items { get; set; } = new();
}

public sealed class ConfigurationItem
{
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class ReferenceGraphInfo
{
    public int DirectCount { get; set; }

    public int TransitiveCount { get; set; }

    public int BrokenReferenceCount { get; set; }

    public List<ReferenceNode> DirectItems { get; set; } = new();

    public List<ReferenceNode> TransitiveItems { get; set; } = new();
}

public sealed class MatesInfo
{
    public int Count { get; set; }

    public List<MateNode> Items { get; set; } = new();
}

public sealed class DimensionsInfo
{
    public int Count { get; set; }

    public List<DimensionNode> Items { get; set; } = new();
}

public sealed class DimensionNode
{
    public string Name { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public double Value { get; set; }

    public string UnitSource { get; set; } = "document";
}

public sealed class MateNode
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public int EntityCount { get; set; }

    public List<string> Components { get; set; } = new();
}

public sealed class ReferenceNode
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? ImportedPath { get; set; }

    public bool IsReadOnly { get; set; }

    public bool ExistsOnDisk { get; set; }

    public bool IsBroken { get; set; }
}

public sealed class DiagnosticsInfo
{
    public string RebuildState { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new();

    public List<string> MissingReferences { get; set; } = new();
}

public sealed class PolicyInfo
{
    public List<string> EnabledTools { get; set; } = new();

    public ToolUnlockTier ToolUnlockTier { get; set; }

    public double ExplorationPercent { get; set; }
}
