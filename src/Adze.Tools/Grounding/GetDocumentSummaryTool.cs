using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetDocumentSummaryTool : IReadOnlyTool<GetDocumentSummaryParameters>
{
    public string ToolName => ToolNames.GetDocumentSummary;

    public ToolResult Execute(SessionContext context, GetDocumentSummaryParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document to summarize."
                : "Document summary generated."
        };

        if (context.Document == null)
        {
            return result;
        }

        result.Data["title"] = context.Document.Title;
        result.Data["path"] = context.Document.Path;
        result.Data["type"] = context.Document.Type;
        result.Data["active_configuration"] = context.Document.ActiveConfiguration;
        result.Data["units"] = context.Document.Units;
        result.Data["selection_count"] = context.Selection.Count;

        if (parameters.IncludeProperties)
        {
            result.Data["properties"] = context.Properties;
        }

        if (parameters.IncludeDiagnostics)
        {
            result.Data["rebuild_state"] = context.Diagnostics.RebuildState;
            result.Data["warnings"] = context.Diagnostics.Warnings;
            result.Data["missing_references"] = context.Diagnostics.MissingReferences;
        }

        return result;
    }
}
