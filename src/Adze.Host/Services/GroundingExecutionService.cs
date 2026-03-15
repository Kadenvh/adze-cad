using System;
using System.Collections.Generic;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools;

namespace Adze.Host.Services;

internal sealed class GroundingExecutionReport
{
    public DateTimeOffset ExecutedUtc { get; set; }

    public bool IsApplicationConnected { get; set; }

    public string Request { get; set; } = string.Empty;

    public BrokerPrompt Prompt { get; set; } = new();

    public BrokerResponse Response { get; set; } = new();

    public List<ToolResult> ToolResults { get; set; } = new();

    public int SuccessfulToolCount { get; set; }

    public int FailedToolCount { get; set; }
}

internal static class GroundingExecutionService
{
    public const string DefaultRequest = "Summarize the active document and tell me which grounding tools should run first.";

    public static string NormalizeRequest(string? userRequest)
    {
        return string.IsNullOrWhiteSpace(userRequest)
            ? DefaultRequest
            : userRequest?.Trim() ?? DefaultRequest;
    }

    private static readonly HybridBrokerOrchestrator Orchestrator = new();
    private static readonly GroundingToolCatalog GroundingTools = ToolCatalog.CreateGroundingCatalog();

    public static GroundingExecutionReport Execute(SessionContext context, string? userRequest, bool isApplicationConnected = true)
    {
        string request = NormalizeRequest(userRequest);
        BrokerPrompt prompt = ContextPromptComposer.Compose(context, request);
        BrokerResponse response = Orchestrator.CreateGroundingPlan(context, request, isApplicationConnected);
        List<ToolResult> toolResults = ExecuteRecommendedTools(context, response);

        int successfulToolCount = 0;
        foreach (ToolResult result in toolResults)
        {
            if (result.Success)
            {
                successfulToolCount++;
            }
        }

        return new GroundingExecutionReport
        {
            ExecutedUtc = context.Session.TimestampUtc,
            IsApplicationConnected = isApplicationConnected,
            Request = request,
            Prompt = prompt,
            Response = response,
            ToolResults = toolResults,
            SuccessfulToolCount = successfulToolCount,
            FailedToolCount = toolResults.Count - successfulToolCount
        };
    }

    private static List<ToolResult> ExecuteRecommendedTools(SessionContext context, BrokerResponse response)
    {
        var results = new List<ToolResult>();
        foreach (BrokerToolRecommendation recommendation in response.RecommendedTools)
        {
            ToolResult? result = recommendation.ToolName switch
            {
                ToolNames.GetActiveDocument => GroundingTools.ActiveDocument.Execute(context, new EmptyParameters()),
                ToolNames.GetDocumentSummary => GroundingTools.DocumentSummary.Execute(
                    context,
                    new GetDocumentSummaryParameters
                    {
                        IncludeDiagnostics = true,
                        IncludeProperties = true
                    }),
                ToolNames.GetSelectionContext => GroundingTools.SelectionContext.Execute(
                    context,
                    new GetSelectionContextParameters
                    {
                        IncludeEntityDetails = true
                    }),
                ToolNames.GetFeatureTreeSlice => GroundingTools.FeatureTreeSlice.Execute(
                    context,
                    new GetFeatureTreeSliceParameters
                    {
                        AnchorName = context.FeatureTree.Anchor,
                        Radius = 6
                    }),
                ToolNames.GetDimensions => GroundingTools.Dimensions.Execute(
                    context,
                    new GetDimensionsParameters
                    {
                        Scope = "document",
                        IncludeDriven = true
                    }),
                ToolNames.GetConfigurations => GroundingTools.Configurations.Execute(
                    context,
                    new GetConfigurationsParameters
                    {
                        IncludeSuppressionState = true
                    }),
                ToolNames.GetCustomProperties => GroundingTools.CustomProperties.Execute(
                    context,
                    new GetCustomPropertiesParameters
                    {
                        Scope = "both",
                        ConfigurationName = context.Configurations.ActiveName
                    }),
                ToolNames.GetMates => GroundingTools.Mates.Execute(
                    context,
                    new GetMatesParameters
                    {
                        Scope = "document",
                        Limit = 25
                    }),
                ToolNames.GetReferenceGraph => GroundingTools.ReferenceGraph.Execute(
                    context,
                    new GetReferenceGraphParameters
                    {
                        Depth = context.Document?.Type == "assembly" ? 2 : 1,
                        IncludeExternalReferences = true
                    }),
                ToolNames.GetRebuildDiagnostics => GroundingTools.RebuildDiagnostics.Execute(
                    context,
                    new GetRebuildDiagnosticsParameters
                    {
                        IncludeMissingReferences = true,
                        IncludeWarnings = true
                    }),
                _ => null
            };

            if (result != null)
            {
                results.Add(result);
            }
        }

        return results;
    }
}
