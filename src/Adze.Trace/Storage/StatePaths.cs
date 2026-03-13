using System.IO;

namespace Adze.Trace.Storage;

public static class StatePaths
{
    private static readonly string Root = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Adze");

    public static string RootDirectory => Root;

    public static string TraceDirectory => Path.Combine(Root, "traces");

    public static string StateDirectory => Path.Combine(Root, "state");

    public static string RecipeDirectory => Path.Combine(Root, "recipes", "candidates");

    public static string SnapshotDirectory => Path.Combine(Root, "snapshots");

    public static string ProgressionStatePath => Path.Combine(StateDirectory, "progression-state.json");

    public static string LatestGroundingSnapshotPath => Path.Combine(SnapshotDirectory, "latest-grounding-snapshot.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(TraceDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(RecipeDirectory);
        Directory.CreateDirectory(SnapshotDirectory);
    }
}
