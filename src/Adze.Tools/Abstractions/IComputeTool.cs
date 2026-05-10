using Adze.Contracts.Models;

namespace Adze.Tools.Abstractions;

/// <summary>
/// A tool that does not modify SOLIDWORKS state but needs live COM access
/// (mass properties, measurement, geometry queries) — beyond what's captured
/// in the <see cref="SessionContext"/> snapshot. Compute tools sit between
/// pure-snapshot read tools and write tools: they're safe (Class 1, Read)
/// but require an <c>ISldWorks</c> reference to do meaningful work.
///
/// When the application reference is null (running outside SW, unit tests),
/// implementations should return a <see cref="ToolResult"/> with
/// <c>Success = false</c> and a clear summary.
/// </summary>
public interface IComputeTool<in TParameters>
{
    string ToolName { get; }

    ToolResult Execute(SessionContext context, object? application, TParameters parameters);
}
