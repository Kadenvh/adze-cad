using System.Linq;
using System.Text;
using Adze.Broker.Models;
using Adze.Contracts.Models;

namespace Adze.Broker.Formatting;

public static class ContextPromptComposer
{
    public static BrokerPrompt Compose(SessionContext context, string userRequest)
    {
        var prompt = new BrokerPrompt
        {
            SystemPrompt = BuildSystemPrompt(),
            UserPrompt = BuildUserPrompt(context, userRequest),
            AllowedTools = new System.Collections.Generic.List<string>(context.Policy.EnabledTools)
        };

        return prompt;
    }

    public static AssistantSynthesisPrompt ComposeSynthesisPrompt(
        string userRequest,
        BrokerResponse response,
        string contextJson,
        string toolResultsJson)
    {
        return new AssistantSynthesisPrompt
        {
            SystemPrompt = BuildSynthesisSystemPrompt(),
            UserPrompt = BuildSynthesisUserPrompt(userRequest, response, contextJson, toolResultsJson)
        };
    }

    private static string BuildUserPrompt(SessionContext context, string userRequest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("User request: " + userRequest);
        sb.AppendLine("Allowed tools: " + string.Join(", ", context.Policy.EnabledTools));
        sb.AppendLine("SOLIDWORKS version: " + context.Environment.SolidWorksVersion);
        sb.AppendLine("Tool tier: " + context.Policy.ToolUnlockTier);
        sb.AppendLine("Exploration percent: " + context.Policy.ExplorationPercent.ToString("0.0"));

        if (context.Document == null)
        {
            sb.AppendLine("Active document: none");
        }
        else
        {
            sb.AppendLine("Active document type: " + context.Document.Type);
            sb.AppendLine("Active document title: " + context.Document.Title);
            sb.AppendLine("Active document path: " + context.Document.Path);
            sb.AppendLine("Active configuration: " + context.Document.ActiveConfiguration);
            sb.AppendLine("Document units: " + context.Document.Units);
            sb.AppendLine("Document dirty: " + context.Document.IsDirty);
            sb.AppendLine("Document read-only: " + context.Document.IsReadOnly);
            sb.AppendLine("Known configurations: " + context.Configurations.Count);
            sb.AppendLine("Feature preview count: " + context.FeatureTree.Features.Count);
            sb.AppendLine("Dimension count: " + context.Dimensions.Count);
            sb.AppendLine("Mate count: " + context.Mates.Count);
            sb.AppendLine("Reference counts: direct=" + context.ReferenceGraph.DirectCount + ", transitive=" + context.ReferenceGraph.TransitiveCount + ", broken=" + context.ReferenceGraph.BrokenReferenceCount);
            sb.AppendLine("Diagnostics state: " + context.Diagnostics.RebuildState);
        }

        sb.AppendLine("Selection count: " + context.Selection.Count);
        if (context.Selection.Items.Count > 0)
        {
            sb.AppendLine("Selection preview: " + string.Join(" | ", context.Selection.Items.ConvertAll(item => item.Kind + ":" + item.Name)));
        }

        if (context.Diagnostics.Warnings.Count > 0)
        {
            sb.AppendLine("Diagnostics warnings: " + string.Join(" | ", context.Diagnostics.Warnings));
        }

        if (context.Diagnostics.MissingReferences.Count > 0)
        {
            sb.AppendLine("Missing references: " + string.Join(" | ", context.Diagnostics.MissingReferences));
        }

        return sb.ToString();
    }

    private static string BuildSystemPrompt()
    {
        return
            "You are a grounded SOLIDWORKS assistant planner. " +
            "Do not claim to have executed tools. Use only the provided context and allowed tools. " +
            "Return only JSON with this exact shape: " +
            "{\"turn_status\":\"ready|attention_needed|host_unavailable|needs_document\",\"intent\":\"short_intent_label\",\"confidence\":0.0," +
            "\"summary\":\"short summary\",\"assistant_message\":\"user-facing grounded message\",\"blockers\":[\"...\"]," +
            "\"recovery_suggestions\":[\"...\"],\"next_questions\":[\"...\"]," +
            "\"recommended_tools\":[{\"tool_name\":\"allowed_tool_name\",\"reason\":\"why\",\"priority\":1,\"score\":10.0}]}. " +
            "Recommend at most 4 tools and only from the allowed tools list.";
    }

    private static string BuildSynthesisSystemPrompt()
    {
        return
            "You are a grounded SOLIDWORKS assistant. " +
            "Write a concise plain-text answer for the user using only the provided session context, broker guidance, and executed tool results. " +
            "Do not invent geometry, dimensions, mates, properties, references, or failure causes that are not present in the provided data. " +
            "If the turn is blocked or no document is open, explain the blocker clearly and use the provided recovery suggestions. " +
            "If the available results are partial, say so directly. Return plain text only and do not use markdown fences or JSON.";
    }

    private static string BuildSynthesisUserPrompt(
        string userRequest,
        BrokerResponse response,
        string contextJson,
        string toolResultsJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine("User request: " + userRequest);
        sb.AppendLine("Broker turn status: " + response.TurnStatus);
        sb.AppendLine("Broker intent: " + response.Intent);
        sb.AppendLine("Broker confidence: " + response.Confidence.ToString("0.00"));
        sb.AppendLine("Broker summary: " + response.Summary);
        sb.AppendLine("Broker guidance: " + response.AssistantMessage);
        sb.AppendLine("Broker blockers: " + JoinValues(response.Blockers));
        sb.AppendLine("Broker recovery suggestions: " + JoinValues(response.RecoverySuggestions));
        sb.AppendLine("Broker follow-up questions: " + JoinValues(response.NextQuestions));
        sb.AppendLine("Broker recommended tools: " + JoinRecommendations(response.RecommendedTools));
        sb.AppendLine();
        sb.AppendLine("Session context JSON:");
        sb.AppendLine(contextJson);
        sb.AppendLine();
        sb.AppendLine("Executed tool results JSON:");
        sb.AppendLine(toolResultsJson);
        return sb.ToString();
    }

    private static string JoinValues(System.Collections.Generic.IEnumerable<string> values)
    {
        string joined = string.Join(" | ", values);
        return string.IsNullOrWhiteSpace(joined) ? "none" : joined;
    }

    private static string JoinRecommendations(System.Collections.Generic.IEnumerable<BrokerToolRecommendation> values)
    {
        string joined = string.Join(
            " | ",
            values.Select(item => item.ToolName + ":" + item.Reason));
        return string.IsNullOrWhiteSpace(joined) ? "none" : joined;
    }
}
