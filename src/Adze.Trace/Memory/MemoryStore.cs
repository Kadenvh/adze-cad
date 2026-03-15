using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Adze.Trace.Serialization;
using Adze.Trace.Storage;

namespace Adze.Trace.Memory;

public sealed class MemoryStore
{
    public DocumentMemory? LoadDocumentMemory(string documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return null;
        }

        string key = ComputeDocumentKey(documentPath);
        string dir = GetDocumentMemoryDirectory(key);
        string filePath = Path.Combine(dir, "document-memory.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        if (!JsonFileStore.TryRead(filePath, out Dictionary<string, object> payload))
        {
            return null;
        }

        return DeserializeDocumentMemory(payload, key);
    }

    public void SaveDocumentMemory(DocumentMemory memory)
    {
        if (string.IsNullOrWhiteSpace(memory.DocumentKey))
        {
            return;
        }

        string dir = GetDocumentMemoryDirectory(memory.DocumentKey);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string filePath = Path.Combine(dir, "document-memory.json");
        memory.LastAccessedUtc = DateTimeOffset.UtcNow;

        var dict = SerializeDocumentMemory(memory);
        JsonFileStore.Write(filePath, dict);
    }

    public UserPreferenceMemory? LoadUserPreferences(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        StatePaths.Ensure();
        string filePath = Path.Combine(StatePaths.StateDirectory, "user-preferences.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        if (!JsonFileStore.TryRead(filePath, out Dictionary<string, object> payload))
        {
            return null;
        }

        return DeserializeUserPreferences(payload, userId);
    }

    public void SaveUserPreferences(UserPreferenceMemory preferences)
    {
        if (string.IsNullOrWhiteSpace(preferences.UserId))
        {
            return;
        }

        StatePaths.Ensure();
        string filePath = Path.Combine(StatePaths.StateDirectory, "user-preferences.json");
        preferences.LastUpdatedUtc = DateTimeOffset.UtcNow;

        var dict = SerializeUserPreferences(preferences);
        JsonFileStore.Write(filePath, dict);
    }

    public void RecordIntent(string documentPath, string intent)
    {
        if (string.IsNullOrWhiteSpace(documentPath) || string.IsNullOrWhiteSpace(intent))
        {
            return;
        }

        string key = ComputeDocumentKey(documentPath);
        DocumentMemory memory = LoadDocumentMemory(documentPath) ?? new DocumentMemory
        {
            DocumentKey = key,
            DocumentPath = documentPath
        };

        memory.SessionCount++;

        if (!memory.RecentIntents.Exists(i => string.Equals(i, intent, StringComparison.OrdinalIgnoreCase)))
        {
            memory.RecentIntents.Add(intent);
            if (memory.RecentIntents.Count > 20)
            {
                memory.RecentIntents.RemoveAt(0);
            }
        }

        SaveDocumentMemory(memory);
    }

    public static string ComputeDocumentKey(string documentPath)
    {
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(documentPath.ToLowerInvariant()));
        return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string GetDocumentMemoryDirectory(string key)
    {
        return Path.Combine(StatePaths.RootDirectory, "memory", key);
    }

    private static Dictionary<string, object?> SerializeDocumentMemory(DocumentMemory memory)
    {
        return new Dictionary<string, object?>
        {
            ["document_key"] = memory.DocumentKey,
            ["document_path"] = memory.DocumentPath,
            ["document_title"] = memory.DocumentTitle,
            ["last_accessed_utc"] = memory.LastAccessedUtc.ToString("o"),
            ["session_count"] = memory.SessionCount,
            ["common_workflows"] = memory.CommonWorkflows,
            ["known_issues"] = memory.KnownIssues,
            ["key_dimensions"] = memory.KeyDimensions,
            ["key_properties"] = memory.KeyProperties,
            ["recent_intents"] = memory.RecentIntents
        };
    }

    private static DocumentMemory DeserializeDocumentMemory(Dictionary<string, object> payload, string key)
    {
        var memory = new DocumentMemory { DocumentKey = key };

        if (payload.TryGetValue("document_path", out object? path) && path != null)
            memory.DocumentPath = path.ToString() ?? string.Empty;
        if (payload.TryGetValue("document_title", out object? title) && title != null)
            memory.DocumentTitle = title.ToString() ?? string.Empty;
        if (payload.TryGetValue("session_count", out object? count) && count is int c)
            memory.SessionCount = c;
        if (payload.TryGetValue("last_accessed_utc", out object? ts) && ts != null &&
            DateTimeOffset.TryParse(ts.ToString(), out DateTimeOffset parsed))
            memory.LastAccessedUtc = parsed;
        if (payload.TryGetValue("recent_intents", out object? intents) && intents is object[] intentArr)
            memory.RecentIntents = new System.Collections.Generic.List<string>(
                Array.ConvertAll(intentArr, o => o?.ToString() ?? string.Empty));

        return memory;
    }

    private static Dictionary<string, object?> SerializeUserPreferences(UserPreferenceMemory prefs)
    {
        return new Dictionary<string, object?>
        {
            ["user_id"] = prefs.UserId,
            ["preferred_answer_mode"] = prefs.PreferredAnswerMode,
            ["preferred_verbosity"] = prefs.PreferredVerbosity,
            ["focus_areas"] = prefs.FocusAreas,
            ["auto_include_diagnostics"] = prefs.AutoIncludeDiagnostics,
            ["last_updated_utc"] = prefs.LastUpdatedUtc.ToString("o")
        };
    }

    private static UserPreferenceMemory DeserializeUserPreferences(Dictionary<string, object> payload, string userId)
    {
        var prefs = new UserPreferenceMemory { UserId = userId };

        if (payload.TryGetValue("preferred_answer_mode", out object? mode) && mode != null)
            prefs.PreferredAnswerMode = mode.ToString() ?? "brief";
        if (payload.TryGetValue("preferred_verbosity", out object? verbosity) && verbosity != null)
            prefs.PreferredVerbosity = verbosity.ToString() ?? "concise";
        if (payload.TryGetValue("auto_include_diagnostics", out object? diag) && diag is bool diagBool)
            prefs.AutoIncludeDiagnostics = diagBool;
        if (payload.TryGetValue("last_updated_utc", out object? ts) && ts != null &&
            DateTimeOffset.TryParse(ts.ToString(), out DateTimeOffset parsed))
            prefs.LastUpdatedUtc = parsed;

        return prefs;
    }
}
