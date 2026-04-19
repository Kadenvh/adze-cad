using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetRebuildDiagnosticsTool : IReadOnlyTool<GetRebuildDiagnosticsParameters>
{
    public string ToolName => ToolNames.GetRebuildDiagnostics;

    public ToolResult Execute(SessionContext context, GetRebuildDiagnosticsParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document for diagnostics inspection."
                : "Checked rebuild diagnostics."
        };

        if (context.Document == null)
        {
            return result;
        }

        result.Data["rebuild_state"] = context.Diagnostics.RebuildState;

        if (parameters.IncludeWarnings)
        {
            result.Data["warnings"] = context.Diagnostics.Warnings;
        }

        if (parameters.IncludeMissingReferences)
        {
            result.Data["missing_references"] = context.Diagnostics.MissingReferences;
        }

        return result;
    }
}
