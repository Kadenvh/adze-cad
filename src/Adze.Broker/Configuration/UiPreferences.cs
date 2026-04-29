using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Adze.Broker.Configuration;

/// <summary>
/// User-facing UI preferences (Chunk 3 of v1.1 UI rebuild).
///
/// Why a separate file from <see cref="FeatureGateConfigService"/>? Feature
/// gates are bool-only by design (their on-disk parser only understands
/// <c>true</c>/<c>false</c>). UI prefs need string and string-list values
/// (theme mode, clarification panel state). Mixing schemas in one file would
/// either complicate the gate parser or silently drop unknown keys. A dedicated
/// prefs file with a real JSON serializer (<see cref="JavaScriptSerializer"/>,
/// already a dependency via <c>System.Web.Extensions</c>) keeps both clean.
///
/// Persistence path: <c>%LOCALAPPDATA%\Adze\ui-prefs.json</c>.
/// Corruption / missing file → defaults (Light mode, empty clarification state).
/// </summary>
public sealed class UiPreferences
{
    private const string FileName = "ui-prefs.json";
    private const string DirName = "Adze";

    /// <summary>Theme mode: "light", "dark", or "system".</summary>
    public string UiMode { get; set; } = "light";

    /// <summary>Last selected clarification intent: "none", "diagnostic", "modify", "inspect", "plan".</summary>
    public string ClarificationIntent { get; set; } = "none";

    /// <summary>Last selected clarification scopes — saved labels (e.g. "active_doc").</summary>
    public List<string> ClarificationScopes { get; set; } = new();

    /// <summary>Last selected output format: "default", "concise", "step", "bullet", "table".</summary>
    public string ClarificationOutput { get; set; } = "default";

    /// <summary>Last selected diagnostics toggles — saved labels (e.g. "tool_calls").</summary>
    public List<string> ClarificationDiagnostics { get; set; } = new();

    /// <summary>Whether the refinement panel is expanded by default.</summary>
    public bool ClarificationExpanded { get; set; } = false;

    /// <summary>Resolved absolute path to the prefs file. Creates the parent dir on demand.</summary>
    public static string GetPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(root, DirName);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    /// <summary>
    /// Loads from disk. Missing file → defaults. Corrupted file → defaults
    /// (the next Save will overwrite the bad file).
    /// </summary>
    public static UiPreferences Load()
    {
        try
        {
            string path = GetPath();
            if (!File.Exists(path)) return new UiPreferences();
            string content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content)) return new UiPreferences();

            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<UiPreferences>(content);
            if (parsed == null) return new UiPreferences();
            // Defensive defaults for any missing list fields after deserialization.
            parsed.UiMode = NormalizeMode(parsed.UiMode);
            parsed.ClarificationIntent ??= "none";
            parsed.ClarificationOutput ??= "default";
            parsed.ClarificationScopes ??= new List<string>();
            parsed.ClarificationDiagnostics ??= new List<string>();
            return parsed;
        }
        catch
        {
            return new UiPreferences();
        }
    }

    /// <summary>Saves atomically (temp → move) so a half-written file never replaces a good one.</summary>
    public void Save()
    {
        try
        {
            string path = GetPath();
            UiMode = NormalizeMode(UiMode);
            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(this);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
        catch
        {
            // Save failure is non-fatal — prefs are best-effort.
        }
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "light";
        string m = mode!.Trim().ToLowerInvariant();
        return m switch
        {
            "light" => "light",
            "dark" => "dark",
            "system" => "system",
            _ => "light"
        };
    }
}
