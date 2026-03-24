using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Adze.Broker.Models;

/// <summary>
/// Accumulates telemetry counters for the current add-in session.
/// Thread-safe — all mutations go through the lock in HostState.
/// This class itself is not thread-safe; callers must synchronize externally.
/// </summary>
public sealed class SessionTelemetry
{
    private readonly Dictionary<string, int> _toolCallCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _toolErrorCounts = new(StringComparer.OrdinalIgnoreCase);

    // Run outcomes
    public int RunsTotal { get; private set; }
    public int RunsSuccess { get; private set; }
    public int RunsCancelled { get; private set; }
    public int RunsFailed { get; private set; }
    public int RunsFellBack { get; private set; }

    // Cancellation detail
    public int CancelledDuringToolExecution { get; private set; }
    public int CancelledDuringApiCall { get; private set; }
    public int CancelledByUser { get; private set; }

    // Write tracking
    public int WritesProposed { get; private set; }
    public int WritesApplied { get; private set; }
    public int WritesCancelled { get; private set; }
    public int WritesFailed { get; private set; }
    public int WritePlanBatchesApplied { get; private set; }

    // Recipe tracking
    public int RecipesCaptureCandidates { get; private set; }
    public int RecipesPromoted { get; private set; }

    // Path tracking
    public int AgenticRuns { get; private set; }
    public int ClassicRuns { get; private set; }

    public void RecordToolCall(string toolName, bool isError)
    {
        if (string.IsNullOrEmpty(toolName)) return;

        if (!_toolCallCounts.ContainsKey(toolName))
            _toolCallCounts[toolName] = 0;
        _toolCallCounts[toolName]++;

        if (isError)
        {
            if (!_toolErrorCounts.ContainsKey(toolName))
                _toolErrorCounts[toolName] = 0;
            _toolErrorCounts[toolName]++;
        }
    }

    public void RecordRunOutcome(AgentRunOutcome outcome)
    {
        RunsTotal++;
        switch (outcome)
        {
            case AgentRunOutcome.Success:
                RunsSuccess++;
                break;
            case AgentRunOutcome.Cancelled:
                RunsCancelled++;
                break;
            case AgentRunOutcome.Failed:
                RunsFailed++;
                break;
            case AgentRunOutcome.FellBack:
            case AgentRunOutcome.BlockedByPolicy:
                RunsFellBack++;
                break;
        }
    }

    public void RecordClassicRun(bool success)
    {
        RunsTotal++;
        ClassicRuns++;
        if (success)
            RunsSuccess++;
        else
            RunsFellBack++;
    }

    public void RecordAgenticRun() => AgenticRuns++;

    public void RecordCancellation(string phase)
    {
        CancelledByUser++;
        if (string.Equals(phase, "tool_execution", StringComparison.OrdinalIgnoreCase))
            CancelledDuringToolExecution++;
        else if (string.Equals(phase, "api_call", StringComparison.OrdinalIgnoreCase))
            CancelledDuringApiCall++;
    }

    public void RecordWriteProposed() => WritesProposed++;
    public void RecordWriteApplied() => WritesApplied++;
    public void RecordWriteCancelled() => WritesCancelled++;
    public void RecordWriteFailed() => WritesFailed++;
    public void RecordWritePlanBatchApplied() => WritePlanBatchesApplied++;

    public void RecordRecipeCaptured() => RecipesCaptureCandidates++;
    public void RecordRecipePromoted() => RecipesPromoted++;

    public IReadOnlyDictionary<string, int> GetToolCallCounts() => _toolCallCounts;
    public IReadOnlyDictionary<string, int> GetToolErrorCounts() => _toolErrorCounts;

    /// <summary>
    /// Returns tool names sorted by call count (descending).
    /// </summary>
    public List<KeyValuePair<string, int>> GetToolCallRanking()
    {
        var sorted = _toolCallCounts.ToList();
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        return sorted;
    }

    public double SuccessRate => RunsTotal > 0 ? (double)RunsSuccess / RunsTotal : 0.0;
    public double CancellationRate => RunsTotal > 0 ? (double)RunsCancelled / RunsTotal : 0.0;
    public double WriteApplyRate => WritesProposed > 0 ? (double)WritesApplied / WritesProposed : 0.0;

    public string FormatSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Session Telemetry");
        sb.AppendLine("=================");
        sb.AppendLine();

        // Run stats
        sb.AppendLine("Runs: " + RunsTotal + " total (" + AgenticRuns + " agentic, " + ClassicRuns + " classic)");
        sb.AppendLine("  Success: " + RunsSuccess + " (" + (SuccessRate * 100).ToString("0") + "%)");
        sb.AppendLine("  Cancelled: " + RunsCancelled);
        sb.AppendLine("  Failed: " + RunsFailed);
        sb.AppendLine("  Fell back: " + RunsFellBack);

        // Tool usage
        var ranking = GetToolCallRanking();
        if (ranking.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Tool calls:");
            foreach (var kv in ranking)
            {
                int errors = _toolErrorCounts.ContainsKey(kv.Key) ? _toolErrorCounts[kv.Key] : 0;
                string errorSuffix = errors > 0 ? " (" + errors + " errors)" : "";
                sb.AppendLine("  " + kv.Key + ": " + kv.Value + errorSuffix);
            }
        }

        // Writes
        if (WritesProposed > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Writes: " + WritesProposed + " proposed, " + WritesApplied + " applied (" + (WriteApplyRate * 100).ToString("0") + "%), " + WritesCancelled + " cancelled, " + WritesFailed + " failed");
            if (WritePlanBatchesApplied > 0)
                sb.AppendLine("  Batch applies: " + WritePlanBatchesApplied);
        }

        // Cancellation detail
        if (CancelledByUser > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Cancellations: " + CancelledByUser + " total");
            if (CancelledDuringApiCall > 0)
                sb.AppendLine("  During API call: " + CancelledDuringApiCall);
            if (CancelledDuringToolExecution > 0)
                sb.AppendLine("  During tool execution: " + CancelledDuringToolExecution);
        }

        // Recipes
        if (RecipesCaptureCandidates > 0 || RecipesPromoted > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recipes: " + RecipesCaptureCandidates + " captured, " + RecipesPromoted + " promoted");
        }

        return sb.ToString();
    }
}
