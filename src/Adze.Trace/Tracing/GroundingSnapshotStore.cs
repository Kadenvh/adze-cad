using Adze.Contracts.Models;
using Adze.Trace.Serialization;
using Adze.Trace.Storage;

namespace Adze.Trace.Tracing;

public static class GroundingSnapshotStore
{
    public static void WriteLatest(GroundingSnapshotRecord snapshot)
    {
        StatePaths.Ensure();
        JsonFileStore.Write(StatePaths.LatestGroundingSnapshotPath, ModelJsonMapper.ToJson(snapshot));
    }
}
