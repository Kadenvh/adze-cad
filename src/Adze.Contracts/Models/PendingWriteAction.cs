using System.Collections.Generic;

namespace Adze.Contracts.Models;

/// <summary>
/// A write tool result awaiting user approval. Lives in HostState's pending-write
/// queue; surfaced in the sidebar as an inline approval card. The UI uses
/// <see cref="WriteId"/> to send Apply/Cancel decisions back through
/// ITaskPaneHost without exposing internal HostState identity.
/// </summary>
public sealed class PendingWriteAction
{
    /// <summary>Stable identifier for routing approval decisions back to the host.</summary>
    public string WriteId { get; set; } = string.Empty;

    /// <summary>Tool name that produced this write (e.g. <c>set_dimension_value</c>).</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>The argument map originally passed to the tool's Preview call.</summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <summary>Preview metadata: human summary, before/after change list, warnings.</summary>
    public WritePreview Preview { get; set; } = new();

    /// <summary>True once the user has approved + the host has applied it.</summary>
    public bool Applied { get; set; }

    /// <summary>True if the user dismissed without applying.</summary>
    public bool Cancelled { get; set; }

    /// <summary>Result message after Apply (success text or error message).</summary>
    public string? ResultMessage { get; set; }

    /// <summary>
    /// Class 3 / advanced writes (e.g. insert_component, create_drawing_view)
    /// trigger the elevated confirmation card styling in the sidebar.
    /// </summary>
    public bool IsElevated { get; set; }

    /// <summary>Human-readable label that matches the SOLIDWORKS Edit→Undo entry.</summary>
    public string UndoLabel { get; set; } = string.Empty;
}
