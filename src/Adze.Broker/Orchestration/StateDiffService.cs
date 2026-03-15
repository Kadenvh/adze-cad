using System.Collections.Generic;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Broker.Orchestration;

public sealed class StateDiffService : IStateDiffService
{
    public StateDiff Compare(StateSnapshot before, StateSnapshot after)
    {
        var diff = new StateDiff
        {
            Target = after.Target
        };

        Dictionary<string, SnapshotItem> beforeMap = BuildMap(before.Items);
        Dictionary<string, SnapshotItem> afterMap = BuildMap(after.Items);

        // Find changed and removed items
        foreach (KeyValuePair<string, SnapshotItem> kvp in beforeMap)
        {
            if (afterMap.TryGetValue(kvp.Key, out SnapshotItem? afterItem))
            {
                if (kvp.Value.Value != afterItem.Value)
                {
                    diff.Changes.Add(new StateDiffItem
                    {
                        Path = kvp.Key,
                        BeforeValue = kvp.Value.Value,
                        AfterValue = afterItem.Value
                    });
                }
            }
            else
            {
                // Item removed
                diff.Changes.Add(new StateDiffItem
                {
                    Path = kvp.Key,
                    BeforeValue = kvp.Value.Value,
                    AfterValue = string.Empty
                });
            }
        }

        // Find added items
        foreach (KeyValuePair<string, SnapshotItem> kvp in afterMap)
        {
            if (!beforeMap.ContainsKey(kvp.Key))
            {
                diff.Changes.Add(new StateDiffItem
                {
                    Path = kvp.Key,
                    BeforeValue = string.Empty,
                    AfterValue = kvp.Value.Value
                });
            }
        }

        return diff;
    }

    private static Dictionary<string, SnapshotItem> BuildMap(List<SnapshotItem> items)
    {
        var map = new Dictionary<string, SnapshotItem>();
        foreach (SnapshotItem item in items)
        {
            map[item.Path] = item;
        }

        return map;
    }
}
