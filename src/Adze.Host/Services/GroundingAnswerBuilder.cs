using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adze.Contracts.Models;

namespace Adze.Host.Services;

internal static class GroundingAnswerBuilder
{
    public static string Build(GroundingExecutionReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Assistant response");
        sb.AppendLine("------------------");
        sb.AppendLine("Request: " + report.Request);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(report.Response.AssistantMessage))
        {
            sb.AppendLine(report.Response.AssistantMessage);
            sb.AppendLine();
        }

        List<ToolResult> successfulResults = report.ToolResults.Where(result => result.Success).ToList();
        List<ToolResult> failedResults = report.ToolResults.Where(result => !result.Success).ToList();

        if (string.Equals(report.Response.TurnStatus, "host_unavailable", System.StringComparison.OrdinalIgnoreCase))
        {
            AppendBulletedSection(sb, "Current blockers", report.Response.Blockers);
            AppendBulletedSection(sb, "What to do next", report.Response.RecoverySuggestions);
            return sb.ToString();
        }

        if (string.Equals(report.Response.TurnStatus, "needs_document", System.StringComparison.OrdinalIgnoreCase))
        {
            AppendBulletedSection(sb, "Current blockers", report.Response.Blockers);
            AppendBulletedSection(sb, "What to do next", report.Response.RecoverySuggestions);
            return sb.ToString();
        }

        if (successfulResults.Count == 0)
        {
            sb.AppendLine("I could not ground an answer from the current SOLIDWORKS state.");
            AppendBulletedSection(sb, "Current blockers", report.Response.Blockers);
            if (failedResults.Count > 0)
            {
                sb.AppendLine("Blocked tools: " + string.Join(", ", failedResults.Select(result => result.ToolName)));
            }

            AppendBulletedSection(sb, "Recovery suggestions", report.Response.RecoverySuggestions);
            return sb.ToString();
        }

        sb.AppendLine("Grounded findings:");
        foreach (ToolResult result in successfulResults.Take(4))
        {
            sb.AppendLine("- " + result.Summary);
        }

        if (successfulResults.Count > 4)
        {
            sb.AppendLine("- Additional grounded results: " + (successfulResults.Count - 4));
        }

        if (failedResults.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Unavailable during this run: " + string.Join(", ", failedResults.Select(result => result.ToolName)));
        }

        AppendBulletedSection(sb, "Current blockers", report.Response.Blockers);

        List<string> warnings = report.ToolResults
            .SelectMany(result => result.Warnings)
            .Distinct()
            .ToList();
        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Warnings:");
            foreach (string warning in warnings)
            {
                sb.AppendLine("- " + warning);
            }
        }

        AppendBulletedSection(sb, "Suggested next step", report.Response.RecoverySuggestions.Take(1));
        AppendBulletedSection(sb, "Suggested follow-up", report.Response.NextQuestions.Take(2));

        return sb.ToString();
    }

    private static void AppendBulletedSection(StringBuilder sb, string title, IEnumerable<string> values)
    {
        List<string> items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine(title + ":");
        foreach (string item in items)
        {
            sb.AppendLine("- " + item);
        }

        sb.AppendLine();
    }
}
