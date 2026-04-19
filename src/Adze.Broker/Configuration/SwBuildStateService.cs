using System;
using System.IO;

namespace Adze.Broker.Configuration;

/// <summary>
/// Persists the SOLIDWORKS build version Adze was last verified-compatible against,
/// at <c>%LOCALAPPDATA%\Adze\state\sw-build.txt</c>. When the current SW build at
/// <c>ConnectToSW</c> differs from the persisted value, <see cref="Adze.Host"/>
/// forces a fresh run of <c>CompatibilityProbe</c> before registering any mutable
/// surface (context menu, ribbon) — the pattern that prevents a repeat of the
/// 2026-04-19 R2026x crash where an unannounced interop signature change left a
/// previously-working install crashing SW on enable.
///
/// File format: a single line containing the SW revision string (e.g.
/// <c>34.1.0.0140</c>). Leading/trailing whitespace ignored on read. Unreadable
/// or missing files are treated as "never verified" — the probe runs every
/// launch until a verified build is successfully persisted.
/// </summary>
public static class SwBuildStateService
{
    private const string StateDirName = "Adze";
    private const string StateSubDirName = "state";
    private const string BuildFileName = "sw-build.txt";

    public static string GetStatePath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(root, StateDirName, StateSubDirName);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, BuildFileName);
    }

    /// <summary>
    /// Returns the persisted SW build string, or empty when no verified build
    /// has ever been recorded (first launch, corrupted file, etc.). Never throws.
    /// </summary>
    public static string GetLastVerifiedBuild()
    {
        try
        {
            string path = GetStatePath();
            if (!File.Exists(path)) return string.Empty;
            string content = File.ReadAllText(path);
            return (content ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Persists the given SW build string as the last-verified compatible version.
    /// Atomic via temp-then-move. Never throws on normal filesystem issues.
    /// </summary>
    public static void SaveLastVerifiedBuild(string buildVersion)
    {
        if (string.IsNullOrWhiteSpace(buildVersion)) return;
        try
        {
            string path = GetStatePath();
            string temp = path + ".tmp";
            File.WriteAllText(temp, buildVersion.Trim());
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
        catch
        {
            // Non-fatal: on next launch we'll just run the probe again.
        }
    }

    /// <summary>
    /// Clears the persisted build. Call when the installer detects an SW
    /// update in progress, or when a previous probe failed and we want to
    /// force re-verification on next launch.
    /// </summary>
    public static void ClearLastVerifiedBuild()
    {
        try
        {
            string path = GetStatePath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// True when the given current build differs from the persisted value
    /// (including the "no persisted value" case). Callers use this to decide
    /// whether to force a compatibility probe re-run.
    /// </summary>
    public static bool HasBuildChangedSinceLastVerification(string currentBuild)
    {
        if (string.IsNullOrWhiteSpace(currentBuild)) return true;
        string persisted = GetLastVerifiedBuild();
        if (string.IsNullOrWhiteSpace(persisted)) return true;
        return !string.Equals(persisted, currentBuild.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
