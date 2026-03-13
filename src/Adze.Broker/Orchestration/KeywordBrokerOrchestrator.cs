using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Broker.Abstractions;
using Adze.Broker.Models;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Broker.Orchestration;

public sealed class KeywordBrokerOrchestrator : IBrokerOrchestrator
{
    public BrokerResponse CreateGroundingPlan(SessionContext context, string userRequest)
    {
        return CreateGroundingPlan(context, userRequest, true);
    }

    public BrokerResponse CreateGroundingPlan(SessionContext context, string userRequest, bool isApplicationConnected)
    {
        string normalized = userRequest ?? string.Empty;
        string intent = InferIntent(normalized, context);

        if (!isApplicationConnected)
        {
            return new BrokerResponse
            {
                Mode = "grounding",
                TurnStatus = "host_unavailable",
                Intent = intent,
                Confidence = 0.98,
                Summary = "SOLIDWORKS is not connected to the add-in yet.",
                AssistantMessage = "I cannot inspect the CAD session because the SOLIDWORKS host is not currently connected.",
                Blockers = new List<string>
                {
                    "The add-in does not have a live SOLIDWORKS application session."
                },
                RecoverySuggestions = new List<string>
                {
                    "Finish any 3DEXPERIENCE login or update window that is open.",
                    "Launch SOLIDWORKS from the supported desktop or platform path, then run the assistant again."
                },
                NextQuestions = new List<string>
                {
                    "Once SOLIDWORKS is open, ask for a summary of the active document."
                }
            };
        }

        if (context.Document == null)
        {
            return new BrokerResponse
            {
                Mode = "grounding",
                TurnStatus = "needs_document",
                Intent = intent,
                Confidence = 0.96,
                Summary = "SOLIDWORKS is connected, but there is no active document to inspect.",
                AssistantMessage = "I am connected to SOLIDWORKS, but no part, assembly, or drawing is open right now.",
                Blockers = new List<string>
                {
                    "No active document is available for grounding."
                },
                RecoverySuggestions = new List<string>
                {
                    "Open the part, assembly, or drawing you want to inspect.",
                    "If you expected a file to be open already, confirm the launcher opened the intended desktop session."
                },
                NextQuestions = new List<string>
                {
                    "After opening a document, ask me to summarize it or inspect a specific area."
                }
            };
        }

        var candidates = new Dictionary<string, RecommendationCandidate>(StringComparer.OrdinalIgnoreCase);
        bool requestedSpecificInspection = false;

        AddCandidate(candidates, ToolNames.GetActiveDocument, 1, "Confirms which document is active.");
        AddCandidate(candidates, ToolNames.GetDocumentSummary, 2, "Provides the document type, path, units, and active configuration.");

        if (context.Selection.Count > 0)
        {
            AddCandidate(candidates, ToolNames.GetSelectionContext, 2, "There is an active selection in the current document.");
        }

        if (ContainsAny(normalized, "selection", "selected", "highlight"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetSelectionContext, 6, "User request mentions the current selection.");
        }

        if (ContainsAny(normalized, "document", "file", "open", "what is loaded"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetActiveDocument, 6, "User request asks what document is active.");
        }

        if (ContainsAny(normalized, "summary", "metadata", "units", "overview", "summarize"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetDocumentSummary, 6, "User request asks for document details or summary.");
        }

        if (ContainsAny(normalized, "feature", "tree", "history", "featuremanager"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetFeatureTreeSlice, 7, "User request references the feature tree or feature history.");
        }

        if (ContainsAny(normalized, "dimension", "dimensions", "measure", "measurement", "size"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetDimensions, 8, "User request asks about dimensions or measurements.");
        }

        if (ContainsAny(normalized, "configuration", "configurations", "configs", "variant"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetConfigurations, 8, "User request asks about configurations.");
        }

        if (ContainsAny(normalized, "property", "properties", "custom property", "material"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetCustomProperties, 8, "User request asks about custom or document properties.");
        }

        if (ContainsAny(normalized, "mate", "mates", "coincident", "concentric", "distance mate"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetMates, 9, "User request asks about mates or assembly constraints.");
        }

        if (ContainsAny(normalized, "dependency", "dependencies", "reference", "references", "component", "components"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetReferenceGraph, 9, "User request asks about referenced files or assembly dependencies.");
        }

        if (ContainsAny(normalized, "diagnostic", "diagnostics", "warning", "warnings", "rebuild", "error"))
        {
            requestedSpecificInspection = true;
            AddCandidate(candidates, ToolNames.GetRebuildDiagnostics, 9, "User request asks for warnings or rebuild diagnostics.");
        }

        ApplyContextBoosts(candidates, context, normalized, requestedSpecificInspection);

        HashSet<string> allowedTools = new HashSet<string>(context.Policy.EnabledTools, StringComparer.OrdinalIgnoreCase);
        List<BrokerToolRecommendation> recommendations = candidates.Values
            .Where(candidate => allowedTools.Contains(candidate.ToolName))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ToolName, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select((candidate, index) => candidate.ToRecommendation(index + 1))
            .ToList();

        List<string> blockers = BuildBlockers(context);
        List<string> recoverySuggestions = BuildRecoverySuggestions(context, blockers);

        return new BrokerResponse
        {
            Mode = "grounding",
            TurnStatus = blockers.Count == 0 ? "ready" : "attention_needed",
            Intent = intent,
            Confidence = CalculateConfidence(recommendations),
            Summary = BuildSummary(context, recommendations, blockers),
            AssistantMessage = BuildAssistantMessage(context, intent, recommendations, blockers),
            Blockers = blockers,
            RecoverySuggestions = recoverySuggestions,
            NextQuestions = BuildNextQuestions(context, intent),
            RecommendedTools = recommendations
        };
    }

    private static void ApplyContextBoosts(
        IDictionary<string, RecommendationCandidate> candidates,
        SessionContext context,
        string normalizedRequest,
        bool requestedSpecificInspection)
    {
        string documentType = context.Document?.Type ?? "unknown";
        if (string.Equals(documentType, "assembly", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, ToolNames.GetReferenceGraph, 2, "Assembly context benefits from dependency grounding.");
            AddCandidate(candidates, ToolNames.GetMates, 2, "Assembly context benefits from mate inspection.");
        }
        else if (string.Equals(documentType, "part", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, ToolNames.GetDimensions, 2, "Part context often benefits from dimensions.");
            AddCandidate(candidates, ToolNames.GetFeatureTreeSlice, 2, "Part context often benefits from feature history.");
        }

        if (context.Selection.Count > 0 && !requestedSpecificInspection)
        {
            AddCandidate(candidates, ToolNames.GetSelectionContext, 3, "There is a live selection that may disambiguate a general request.");
        }

        if (context.Diagnostics.Warnings.Count > 0 || context.Diagnostics.MissingReferences.Count > 0)
        {
            AddCandidate(candidates, ToolNames.GetRebuildDiagnostics, 4, "Current context already contains warnings or missing references.");
        }

        if (context.ReferenceGraph.BrokenReferenceCount > 0)
        {
            AddCandidate(candidates, ToolNames.GetReferenceGraph, 4, "Current context has unresolved references.");
        }

        if (!requestedSpecificInspection && string.Equals(documentType, "assembly", StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, ToolNames.GetReferenceGraph, 2, "Default assembly grounding includes component structure.");
        }

        if (!requestedSpecificInspection && ContainsAny(normalizedRequest, "help", "start", "begin", "understand"))
        {
            AddCandidate(candidates, ToolNames.GetDocumentSummary, 3, "General onboarding questions benefit from document grounding.");
            AddCandidate(candidates, ToolNames.GetFeatureTreeSlice, 2, "Feature context helps orient the assistant.");
        }
    }

    private static void AddCandidate(IDictionary<string, RecommendationCandidate> candidates, string toolName, int score, string reason)
    {
        if (!candidates.TryGetValue(toolName, out RecommendationCandidate? candidate))
        {
            candidate = new RecommendationCandidate(toolName);
            candidates[toolName] = candidate;
        }

        candidate.Score += score;
        if (!candidate.Reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
        {
            candidate.Reasons.Add(reason);
        }
    }

    private static string BuildSummary(SessionContext context, IReadOnlyCollection<BrokerToolRecommendation> recommendations, IReadOnlyCollection<string> blockers)
    {
        if (recommendations.Count == 0)
        {
            return blockers.Count == 0
                ? "No allowed tools matched the current request."
                : "The broker found blockers before tool execution could begin.";
        }

        string documentType = context.Document?.Type ?? "document";
        return "Ready to ground the current " + documentType + " with " + recommendations.Count + " tool(s): " +
               string.Join(", ", recommendations.Select(recommendation => recommendation.ToolName));
    }

    private static string BuildAssistantMessage(
        SessionContext context,
        string intent,
        IReadOnlyList<BrokerToolRecommendation> recommendations,
        IReadOnlyCollection<string> blockers)
    {
        string documentType = context.Document?.Type ?? "document";
        string title = context.Document?.Title ?? "the current document";

        if (blockers.Count > 0)
        {
            return "I can inspect " + title + ", but there are issues in the current session that may limit how complete the answer is.";
        }

        if (recommendations.Count == 0)
        {
            return "I have an active " + documentType + ", but I could not find an allowed tool path for this request.";
        }

        if (string.Equals(intent, "assembly_structure", StringComparison.OrdinalIgnoreCase))
        {
            return "I will ground this answer from the active assembly structure before summarizing it.";
        }

        if (string.Equals(intent, "diagnostics_review", StringComparison.OrdinalIgnoreCase))
        {
            return "I will inspect the active document diagnostics first so the answer reflects the current warning state.";
        }

        if (string.Equals(intent, "selection_review", StringComparison.OrdinalIgnoreCase))
        {
            return "I will use the current selection as the anchor for this answer.";
        }

        return "I can ground this answer from " + title + " using " + string.Join(", ", recommendations.Select(item => item.ToolName)) + ".";
    }

    private static List<string> BuildBlockers(SessionContext context)
    {
        var blockers = new List<string>();
        if (context.Diagnostics.MissingReferences.Count > 0)
        {
            blockers.Add("The active document has missing references.");
        }

        if (context.Document != null && string.IsNullOrWhiteSpace(context.Document.Path))
        {
            blockers.Add("The active document does not have a saved path yet.");
        }

        if (context.Document?.IsReadOnly == true)
        {
            blockers.Add("The active document is read-only.");
        }

        return blockers;
    }

    private static List<string> BuildRecoverySuggestions(SessionContext context, IReadOnlyCollection<string> blockers)
    {
        var suggestions = new List<string>();
        if (blockers.Count == 0)
        {
            suggestions.Add("Run the assistant again after changing the selection or asking a more specific question if you need deeper grounding.");
            return suggestions;
        }

        if (context.Diagnostics.MissingReferences.Count > 0)
        {
            suggestions.Add("Resolve missing references and rerun the assistant if you need dependency-accurate answers.");
        }

        if (context.Document != null && string.IsNullOrWhiteSpace(context.Document.Path))
        {
            suggestions.Add("Save the active document if you want file-path-aware grounding and repeatable reports.");
        }

        if (context.Document?.IsReadOnly == true)
        {
            suggestions.Add("Open a writable copy if you plan to move beyond read-only inspection later.");
        }

        return suggestions;
    }

    private static List<string> BuildNextQuestions(SessionContext context, string intent)
    {
        var questions = new List<string>();
        if (string.Equals(context.Document?.Type, "assembly", StringComparison.OrdinalIgnoreCase))
        {
            questions.Add("Ask me which components or mates matter most in this assembly.");
        }
        else if (string.Equals(context.Document?.Type, "part", StringComparison.OrdinalIgnoreCase))
        {
            questions.Add("Ask me which dimensions, features, or custom properties matter most in this part.");
        }

        if (string.Equals(intent, "general_grounding", StringComparison.OrdinalIgnoreCase))
        {
            questions.Add("Ask a specific follow-up about dimensions, features, mates, properties, or diagnostics.");
        }

        return questions;
    }

    private static string InferIntent(string input, SessionContext context)
    {
        if (ContainsAny(input, "selection", "selected", "highlight"))
        {
            return "selection_review";
        }

        if (ContainsAny(input, "mate", "mates", "component", "components", "dependency", "dependencies", "reference", "references"))
        {
            return "assembly_structure";
        }

        if (ContainsAny(input, "dimension", "dimensions", "measure", "measurement", "size"))
        {
            return "dimension_review";
        }

        if (ContainsAny(input, "configuration", "configurations", "configs", "variant"))
        {
            return "configuration_review";
        }

        if (ContainsAny(input, "property", "properties", "custom property", "material"))
        {
            return "property_review";
        }

        if (ContainsAny(input, "diagnostic", "diagnostics", "warning", "warnings", "rebuild", "error"))
        {
            return "diagnostics_review";
        }

        return context.Document == null ? "needs_document" : "general_grounding";
    }

    private static double CalculateConfidence(IReadOnlyCollection<BrokerToolRecommendation> recommendations)
    {
        if (recommendations.Count == 0)
        {
            return 0.25;
        }

        double topScore = recommendations.Max(recommendation => recommendation.Score);
        double confidence = 0.35 + Math.Min(0.6, topScore / 12.0);
        return Math.Round(confidence, 2);
    }

    private static bool ContainsAny(string input, params string[] values)
    {
        return values.Any(value => input.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private sealed class RecommendationCandidate
    {
        public RecommendationCandidate(string toolName)
        {
            ToolName = toolName;
        }

        public string ToolName { get; }

        public int Score { get; set; }

        public List<string> Reasons { get; } = new();

        public BrokerToolRecommendation ToRecommendation(int priority)
        {
            return new BrokerToolRecommendation
            {
                ToolName = ToolName,
                Priority = priority,
                Score = Score,
                Reason = string.Join(" ", Reasons)
            };
        }
    }
}
