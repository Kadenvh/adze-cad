using System;
using Adze.Contracts.Models;

namespace Adze.Trace.Tracing;

public static class WriteTraceRecordBuilder
{
    public static WriteTraceRecord Build(WriteExecutionOutcome outcome, string userId)
    {
        return new WriteTraceRecord
        {
            TraceId = Guid.NewGuid().ToString("N").Substring(0, 12),
            ToolName = outcome.ToolName,
            UndoLabel = outcome.UndoLabel,
            TimestampUtc = DateTimeOffset.UtcNow,
            Outcome = outcome.Status,
            Target = outcome.Preview?.Changes.Count > 0
                ? ExtractTarget(outcome)
                : new WriteTargetDescriptor(),
            BeforeSnapshot = outcome.BeforeSnapshot,
            AfterSnapshot = outcome.AfterSnapshot,
            Diff = outcome.Diff,
            VerificationDecision = outcome.Decision,
            UserId = userId
        };
    }

    public static void Persist(WriteTraceRecord record)
    {
        string json = SerializeRecord(record);
        string tracesDir = Storage.StatePaths.TraceDirectory;
        string fileName = "write-" + record.TraceId + ".json";
        string path = System.IO.Path.Combine(tracesDir, fileName);

        if (!System.IO.Directory.Exists(tracesDir))
        {
            System.IO.Directory.CreateDirectory(tracesDir);
        }

        System.IO.File.WriteAllText(path, json);
    }

    private static WriteTargetDescriptor ExtractTarget(WriteExecutionOutcome outcome)
    {
        if (outcome.Diff?.Target != null)
        {
            return outcome.Diff.Target;
        }

        if (outcome.BeforeSnapshot?.Target != null)
        {
            return outcome.BeforeSnapshot.Target;
        }

        return new WriteTargetDescriptor();
    }

    private static string SerializeRecord(WriteTraceRecord record)
    {
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue
        };

        var dict = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["trace_id"] = record.TraceId,
            ["tool_name"] = record.ToolName,
            ["undo_label"] = record.UndoLabel,
            ["timestamp_utc"] = record.TimestampUtc.ToString("o"),
            ["outcome"] = record.Outcome.ToString(),
            ["target_kind"] = record.Target.Kind.ToString(),
            ["target_name"] = record.Target.TargetName,
            ["user_id"] = record.UserId
        };

        if (record.VerificationDecision != null)
        {
            dict["verification_outcome"] = record.VerificationDecision.Outcome.ToString();
            dict["verification_reason"] = record.VerificationDecision.Reason;
        }

        if (record.Diff != null)
        {
            dict["changes_count"] = record.Diff.Changes.Count;
        }

        return serializer.Serialize(dict);
    }
}
