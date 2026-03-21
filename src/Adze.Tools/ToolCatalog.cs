using Adze.Contracts.Models;
using Adze.Tools.Abstractions;
using Adze.Tools.Grounding;

namespace Adze.Tools;

public sealed class GroundingToolCatalog
{
    public GroundingToolCatalog()
    {
        ActiveDocument = new GetActiveDocumentTool();
        DocumentSummary = new GetDocumentSummaryTool();
        SelectionContext = new GetSelectionContextTool();
        FeatureTreeSlice = new GetFeatureTreeSliceTool();
        Dimensions = new GetDimensionsTool();
        Configurations = new GetConfigurationsTool();
        CustomProperties = new GetCustomPropertiesTool();
        Mates = new GetMatesTool();
        RebuildDiagnostics = new GetRebuildDiagnosticsTool();
        ReferenceGraph = new GetReferenceGraphTool();
        SearchProjectFiles = new SearchProjectFilesTool();
    }

    public IReadOnlyTool<EmptyParameters> ActiveDocument { get; }

    public IReadOnlyTool<GetDocumentSummaryParameters> DocumentSummary { get; }

    public IReadOnlyTool<GetSelectionContextParameters> SelectionContext { get; }

    public IReadOnlyTool<GetFeatureTreeSliceParameters> FeatureTreeSlice { get; }

    public IReadOnlyTool<GetDimensionsParameters> Dimensions { get; }

    public IReadOnlyTool<GetConfigurationsParameters> Configurations { get; }

    public IReadOnlyTool<GetCustomPropertiesParameters> CustomProperties { get; }

    public IReadOnlyTool<GetMatesParameters> Mates { get; }

    public IReadOnlyTool<GetRebuildDiagnosticsParameters> RebuildDiagnostics { get; }

    public IReadOnlyTool<GetReferenceGraphParameters> ReferenceGraph { get; }

    public IReadOnlyTool<SearchProjectFilesParameters> SearchProjectFiles { get; }
}

public static class ToolCatalog
{
    public static GroundingToolCatalog CreateGroundingCatalog() => new();
}
