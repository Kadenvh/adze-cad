using System;
using System.Collections.Generic;

namespace Adze.Trace.Memory;

public sealed class DocumentMemory
{
    public string DocumentKey { get; set; } = string.Empty;

    public string DocumentPath { get; set; } = string.Empty;

    public string DocumentTitle { get; set; } = string.Empty;

    public DateTimeOffset LastAccessedUtc { get; set; } = DateTimeOffset.UtcNow;

    public int SessionCount { get; set; }

    public List<string> CommonWorkflows { get; set; } = new();

    public List<string> KnownIssues { get; set; } = new();

    public Dictionary<string, string> KeyDimensions { get; set; } = new();

    public Dictionary<string, string> KeyProperties { get; set; } = new();

    public List<string> RecentIntents { get; set; } = new();
}

public sealed class UserPreferenceMemory
{
    public string UserId { get; set; } = string.Empty;

    public string PreferredAnswerMode { get; set; } = "brief";

    public string PreferredVerbosity { get; set; } = "concise";

    public List<string> FocusAreas { get; set; } = new();

    public bool AutoIncludeDiagnostics { get; set; }

    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
