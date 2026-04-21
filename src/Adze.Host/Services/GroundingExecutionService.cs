using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Host.Infrastructure;
using Adze.Tools;

namespace Adze.Host.Services;

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
    private static readonly KeywordBrokerOrchestrator KeywordFallbackOrchestrator = new();
    private static readonly GroundingToolCatalog GroundingTools = ToolCatalog.CreateGroundingCatalog();

    public static GroundingExecutionReport Execute(SessionContext context, string? userRequest, bool isApplicationConnected = true)
    {
        string request = NormalizeRequest(userRequest);
        BrokerPrompt prompt = ContextPromptComposer.Compose(context, request);
        BrokerResponse response = Orchestrator.CreateGroundingPlan(context, request, isApplicationConnected);

        // Surface the planning-phase model outcome — success, skip, or failure reason.
        // Populated by HybridBrokerOrchestrator during CreateGroundingPlan. This makes
        // auth / endpoint / timeout issues diagnosable from host.log alone.
        string planningOutcome = HybridBrokerOrchestrator.LastPlanningOutcome;
        if (!string.IsNullOrWhiteSpace(planningOutcome))
        {
            FileLogger.Info("Plan model outcome: " + planningOutcome);
        }

        // Fallback guard — if hybrid orchestrator (model-enhanced planning) returned zero
        // recommended tools, re-run pure keyword broker so tools still fire even when the
        // model path fails silently. Prevents the "tool_count=0 but status=success" failure
        // mode uncovered 2026-04-21 R5.7 testing.
        if (response.RecommendedTools.Count == 0 && isApplicationConnected)
        {
            BrokerResponse keywordOnly = KeywordFallbackOrchestrator.CreateGroundingPlan(context, request, isApplicationConnected);
            if (keywordOnly.RecommendedTools.Count > 0)
            {
                FileLogger.Info(
                    "Broker fallback engaged: hybrid orchestrator returned 0 tools; keyword broker picked " +
                    keywordOnly.RecommendedTools.Count + " (" +
                    string.Join(", ", keywordOnly.RecommendedTools.Select(t => t.ToolName)) + "). " +
                    "Check host.log for preceding model-path failure reason.");
                response = keywordOnly;
            }
        }

        // Plan event — one INFO line per assistant run at planning stage.
        string plannedList = response.RecommendedTools.Count > 0
            ? string.Join(", ", response.RecommendedTools.Select(t => t.ToolName))
            : "(none)";
        FileLogger.Info(
            "Plan: intent=" + response.Intent +
            " turn_status=" + response.TurnStatus +
            " source=" + response.Source +
            " planned_tools=" + response.RecommendedTools.Count +
            " [" + plannedList + "]");

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
            Stopwatch watch = Stopwatch.StartNew();
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
            watch.Stop();

            if (result != null)
            {
                results.Add(result);

                // Tools Log event — one INFO line per tool dispatch. Mirrors the Task Pane
                // Tools Log section so operators can reconstruct tool-by-tool execution
                // from host.log alone without opening per-trace JSON files.
                string outcome = result.Success ? "ok" : "fail";
                string warningText = result.Warnings.Count > 0
                    ? " warnings=" + result.Warnings.Count
                    : string.Empty;
                FileLogger.Info(
                    "Tool " + recommendation.ToolName + " " + outcome +
                    " duration_ms=" + watch.ElapsedMilliseconds +
                    warningText);
            }
            else
            {
                FileLogger.Info(
                    "Tool " + recommendation.ToolName + " skipped (unknown tool name)" +
                    " duration_ms=" + watch.ElapsedMilliseconds);
            }
        }

        return results;
    }
}
