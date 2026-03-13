using System.Collections.Generic;

namespace Adze.Contracts.Models;

public sealed class StarterCorpusManifest
{
    public string Version { get; set; } = string.Empty;

    public string CreatedUtc { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<CorpusSource> Sources { get; set; } = new();
}

public sealed class CorpusSource
{
    public string Id { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public List<string> TargetDocumentTypes { get; set; } = new();

    public List<string> TargetTasks { get; set; } = new();
}

public sealed class GroundingTaskDefinition
{
    public string TaskId { get; set; } = string.Empty;

    public string CorpusSourceId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DocumentType { get; set; } = string.Empty;

    public string? EntryPath { get; set; }

    public string Question { get; set; } = string.Empty;

    public string ExpectedTool { get; set; } = string.Empty;

    public List<string> ExpectedAssertions { get; set; } = new();

    public string Status { get; set; } = string.Empty;
}
