using System;
using System.Collections.Generic;

namespace Adze.Contracts.Models;

public sealed class ClosedFileRecord
{
    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTimeOffset LastWriteUtc { get; set; }

    public string? Author { get; set; }

    public string? Title { get; set; }

    public string? Subject { get; set; }

    public string? Keywords { get; set; }

    public string? Comments { get; set; }

    public string? Company { get; set; }

    public Dictionary<string, string> CustomProperties { get; set; } = new();

    public DateTimeOffset IndexedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClosedFileSearchQuery
{
    public string? FileType { get; set; }

    public string? PathPattern { get; set; }

    public string? PropertyName { get; set; }

    public string? PropertyValue { get; set; }

    public string? Keyword { get; set; }

    public int MaxResults { get; set; } = 50;
}

public sealed class ClosedFileSearchResult
{
    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;

    public string MatchReason { get; set; } = string.Empty;

    public Dictionary<string, string> MatchedProperties { get; set; } = new();
}

public sealed class IndexRunResult
{
    public int FilesScanned { get; set; }

    public int FilesIndexed { get; set; }

    public int Errors { get; set; }

    public List<string> ErrorMessages { get; set; } = new();

    public TimeSpan Duration { get; set; }
}
