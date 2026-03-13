using Adze.Contracts.Models;

namespace Adze.Tools.Abstractions;

public interface IReadOnlyTool<in TParameters>
{
    string ToolName { get; }

    ToolResult Execute(SessionContext context, TParameters parameters);
}
