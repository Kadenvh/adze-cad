using System.Text;
using Adze.Broker.Models;
using Adze.Contracts.Models;

namespace Adze.Host.Services;

internal static class GroundingPlanBuilder
{
    public const string DefaultRequest = GroundingExecutionService.DefaultRequest;

    public static string Build(SessionContext context, string? userRequest, bool isApplicationConnected = true)
    {
        GroundingExecutionReport report = GroundingExecutionService.Execute(context, userRequest, isApplicationConnected);
        return Build(report);
    }

    public static string Build(GroundingExecutionReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Broker plan");
        sb.AppendLine("-----------");
        sb.AppendLine("Request: " + report.Request);
        sb.AppendLine("Mode: " + report.Response.Mode);
        sb.AppendLine("Source: " + report.Response.Source);
        if (!string.IsNullOrWhiteSpace(report.Response.ModelId))
        {
            sb.AppendLine("Model: " + report.Response.ModelId);
        }
        sb.AppendLine("Turn status: " + report.Response.TurnStatus);
        sb.AppendLine("Intent: " + report.Response.Intent);
        sb.AppendLine("Confidence: " + report.Response.Confidence.ToString("0.00"));
        sb.AppendLine("Summary: " + report.Response.Summary);
        sb.AppendLine("Assistant guidance: " + report.Response.AssistantMessage);
        sb.AppendLine("Allowed tools: " + string.Join(", ", report.Prompt.AllowedTools));
        sb.AppendLine();

        if (report.Response.Blockers.Count > 0)
        {
            sb.AppendLine("Blockers:");
            foreach (string blocker in report.Response.Blockers)
            {
                sb.AppendLine("- " + blocker);
            }
            sb.AppendLine();
        }

        if (report.Response.RecoverySuggestions.Count > 0)
        {
            sb.AppendLine("Recovery suggestions:");
            foreach (string suggestion in report.Response.RecoverySuggestions)
            {
                sb.AppendLine("- " + suggestion);
            }
            sb.AppendLine();
        }

        if (report.Response.RecommendedTools.Count == 0)
        {
            sb.AppendLine("No recommendations.");
            return sb.ToString();
        }

        sb.AppendLine("Recommended tools:");
        for (int index = 0; index < report.Response.RecommendedTools.Count; index++)
        {
            var recommendation = report.Response.RecommendedTools[index];
            sb.AppendLine((index + 1) + ". " + recommendation.ToolName + " [priority=" + recommendation.Priority + ", score=" + recommendation.Score.ToString("0.0") + "]");
            sb.AppendLine("   reason: " + recommendation.Reason);
        }

        sb.AppendLine();
        sb.AppendLine("Execution summary:");
        sb.AppendLine("Successful tools: " + report.SuccessfulToolCount);
        sb.AppendLine("Failed tools: " + report.FailedToolCount);
        sb.AppendLine();
        sb.AppendLine("Preview results:");
        if (report.ToolResults.Count == 0)
        {
            sb.AppendLine("No executable preview results for the recommended tools.");
        }
        else
        {
            foreach (ToolResult result in report.ToolResults)
            {
                sb.AppendLine("- " + result.ToolName + " [" + (result.Success ? "ok" : "failed") + "]: " + result.Summary);
                if (result.Warnings.Count > 0)
                {
                    sb.AppendLine("  warnings: " + string.Join(" | ", result.Warnings));
                }
            }
        }

        if (report.Response.NextQuestions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Suggested follow-up:");
            foreach (string question in report.Response.NextQuestions)
            {
                sb.AppendLine("- " + question);
            }
        }

        return sb.ToString();
    }
}
