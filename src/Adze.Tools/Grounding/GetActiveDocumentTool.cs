using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetActiveDocumentTool : IReadOnlyTool<EmptyParameters>
{
    public string ToolName => ToolNames.GetActiveDocument;

    public ToolResult Execute(SessionContext context, EmptyParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document."
                : "Read the active document."
        };

        if (context.Document != null)
        {
            result.Data["type"] = context.Document.Type;
            result.Data["title"] = context.Document.Title;
            result.Data["path"] = context.Document.Path;
            result.Data["active_configuration"] = context.Document.ActiveConfiguration;
            result.Data["is_dirty"] = context.Document.IsDirty;
            result.Data["is_read_only"] = context.Document.IsReadOnly;
        }

        return result;
    }
}
