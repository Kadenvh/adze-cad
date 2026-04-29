using System;
using System.IO;
using Adze.Contracts.Models;
using Adze.Trace.Serialization;

namespace Adze.UiHarness;

/// <summary>
/// Loads SessionContext snapshots from %LOCALAPPDATA%\Adze\snapshots\ — the
/// JSON files Adze.Trace writes during real SOLIDWORKS sessions. With
/// ModelJsonMapper.DeserializeSessionContextJson now in place, we hydrate
/// real typed SessionContext objects so UI surfaces can mount against them
/// without a live SW process.
/// </summary>
internal static class SnapshotLoader
{
    public static string DefaultSnapshotDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Adze",
            "snapshots");

    public static string[] EnumerateRecent(int max = 25)
    {
        if (!Directory.Exists(DefaultSnapshotDir))
        {
            return Array.Empty<string>();
        }

        DirectoryInfo dir = new(DefaultSnapshotDir);
        FileInfo[] files = dir.GetFiles("*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

        int take = Math.Min(max, files.Length);
        string[] paths = new string[take];
        for (int i = 0; i < take; i++)
        {
            paths[i] = files[i].FullName;
        }
        return paths;
    }

    public static SessionContext? LoadFromFile(string path, out string? error)
    {
        error = null;
        try
        {
            string json = File.ReadAllText(path);
            return ModelJsonMapper.DeserializeSessionContextJson(json);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
