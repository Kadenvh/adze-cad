using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Adze.Contracts.Models;
using Adze.Tools;

namespace Adze.Host.Services;

internal sealed class GroundingDashboardSnapshot
{
    public string Text { get; set; } = string.Empty;

    public SessionContext Context { get; set; } = new();

    public List<ToolResult> ToolResults { get; set; } = new();

    public int AchievementCount { get; set; }

    public int ReviewReadyCandidateCount { get; set; }

    public string? LatestAchievementTitle { get; set; }
}

internal static class GroundingDashboardBuilder
{
    private static readonly GroundingToolCatalog GroundingTools = ToolCatalog.CreateGroundingCatalog();

    public static GroundingDashboardSnapshot BuildSnapshot(
        bool isConnected,
        SessionContext context,
        ProgressionState progressionState,
        int reviewReadyCandidateCount)
    {
        var snapshot = new GroundingDashboardSnapshot();
        var sb = new StringBuilder();
        List<ToolResult> toolResults = BuildToolResults(context);
        sb.AppendLine("Host status");
        sb.AppendLine("-----------");
        sb.AppendLine("Updated: " + context.Session.TimestampUtc.ToString("u"));
        sb.AppendLine("Application: " + (isConnected ? "connected" : "unavailable"));
        sb.AppendLine("SOLIDWORKS version: " + context.Environment.SolidWorksVersion);
        sb.AppendLine("Add-in version: " + context.Environment.AddInVersion);
        sb.AppendLine("Machine: " + context.Environment.MachineName);
        sb.AppendLine("Tool tier: " + context.Policy.ToolUnlockTier);
        sb.AppendLine("Exploration percent: " + context.Policy.ExplorationPercent.ToString("0.0"));
        sb.AppendLine("Achievements unlocked: " + progressionState.Achievements.Count);
        sb.AppendLine("Review-ready recipes: " + reviewReadyCandidateCount);
        sb.AppendLine("Session guidance: " + BuildSessionGuidance(isConnected, context));

        AchievementState? latestAchievement = progressionState.Achievements.Count == 0
            ? null
            : progressionState.Achievements[progressionState.Achievements.Count - 1];
        if (latestAchievement != null)
        {
            sb.AppendLine("Latest achievement: " + latestAchievement.Title);
        }

        sb.AppendLine();

        foreach (ToolResult result in toolResults)
        {
            AppendToolResult(sb, result);
        }

        snapshot.Text = sb.ToString();
        snapshot.Context = context;
        snapshot.ToolResults = toolResults;
        snapshot.AchievementCount = progressionState.Achievements.Count;
        snapshot.ReviewReadyCandidateCount = reviewReadyCandidateCount;
        snapshot.LatestAchievementTitle = latestAchievement?.Title;
        return snapshot;
    }

    private static string BuildSessionGuidance(bool isConnected, SessionContext context)
    {
        if (!isConnected)
        {
            return "Finish any 3DEXPERIENCE login or update window, then relaunch SOLIDWORKS from the supported path.";
        }

        if (context.Document == null)
        {
            return "Open a part, assembly, or drawing, then press 'Run assistant' to ground the next answer.";
        }

        if (context.Diagnostics.MissingReferences.Count > 0)
        {
            return "The active document has missing references; answers may be incomplete until those references are resolved.";
        }

        if (context.Document.IsReadOnly)
        {
            return "The active document is read-only. Inspection is available, but later write-capable flows will need a writable copy.";
        }

        if (string.IsNullOrWhiteSpace(context.Document.Path))
        {
            return "The active document has not been saved yet. Save it if you want path-aware grounding and repeatable reports.";
        }

        return "Press 'Run assistant' to inspect the active document with the grounded read-only tool set.";
    }

    private static List<ToolResult> BuildToolResults(SessionContext context)
    {
        return new List<ToolResult>
        {
            GroundingTools.ActiveDocument.Execute(context, new EmptyParameters()),
            GroundingTools.DocumentSummary.Execute(
                context,
                new GetDocumentSummaryParameters
                {
                    IncludeDiagnostics = true,
                    IncludeProperties = true
                }),
            GroundingTools.SelectionContext.Execute(
                context,
                new GetSelectionContextParameters
                {
                    IncludeEntityDetails = true
                }),
            GroundingTools.FeatureTreeSlice.Execute(
                context,
                new GetFeatureTreeSliceParameters
                {
                    AnchorName = context.FeatureTree.Anchor,
                    Radius = 6
                }),
            GroundingTools.Dimensions.Execute(
                context,
                new GetDimensionsParameters
                {
                    Scope = "document",
                    IncludeDriven = true
                }),
            GroundingTools.Configurations.Execute(
                context,
                new GetConfigurationsParameters
                {
                    IncludeSuppressionState = true
                }),
            GroundingTools.CustomProperties.Execute(
                context,
                new GetCustomPropertiesParameters
                {
                    Scope = "both",
                    ConfigurationName = context.Configurations.ActiveName
                }),
            GroundingTools.Mates.Execute(
                context,
                new GetMatesParameters
                {
                    Scope = "document",
                    Limit = 25
                }),
            GroundingTools.ReferenceGraph.Execute(
                context,
                new GetReferenceGraphParameters
                {
                    Depth = context.Document?.Type == "assembly" ? 2 : 1,
                    IncludeExternalReferences = true
                }),
            GroundingTools.RebuildDiagnostics.Execute(
                context,
                new GetRebuildDiagnosticsParameters
                {
                    IncludeMissingReferences = true,
                    IncludeWarnings = true
                })
        };
    }

    private static void AppendToolResult(StringBuilder sb, ToolResult result)
    {
        sb.AppendLine(result.ToolName);
        sb.AppendLine(new string('-', result.ToolName.Length));
        sb.AppendLine("success: " + result.Success);
        sb.AppendLine("summary: " + result.Summary);

        foreach (KeyValuePair<string, object?> entry in result.Data)
        {
            AppendValue(sb, entry.Key, entry.Value, 0);
        }

        if (result.Warnings.Count > 0)
        {
            sb.AppendLine("warnings:");
            foreach (string warning in result.Warnings)
            {
                sb.AppendLine("  - " + warning);
            }
        }

        sb.AppendLine();
    }

    private static void AppendValue(StringBuilder sb, string label, object? value, int indent)
    {
        string prefix = new string(' ', indent);
        if (value == null)
        {
            sb.AppendLine(prefix + label + ": <null>");
            return;
        }

        if (value is string || value.GetType().IsPrimitive || value is decimal || value is DateTime || value is DateTimeOffset)
        {
            sb.AppendLine(prefix + label + ": " + value);
            return;
        }

        if (value is IDictionary dictionary)
        {
            sb.AppendLine(prefix + label + ":");
            if (dictionary.Count == 0)
            {
                sb.AppendLine(prefix + "  <empty>");
                return;
            }

            foreach (DictionaryEntry entry in dictionary)
            {
                AppendValue(sb, Convert.ToString(entry.Key) ?? "<key>", entry.Value, indent + 2);
            }

            return;
        }

        if (value is IEnumerable enumerable)
        {
            sb.AppendLine(prefix + label + ":");
            bool any = false;
            int index = 1;
            foreach (object? item in enumerable)
            {
                any = true;
                AppendEnumerableItem(sb, item, indent + 2, index);
                index++;
            }

            if (!any)
            {
                sb.AppendLine(prefix + "  <empty>");
            }

            return;
        }

        sb.AppendLine(prefix + label + ": " + value);
    }

    private static void AppendEnumerableItem(StringBuilder sb, object? value, int indent, int index)
    {
        string prefix = new string(' ', indent);
        if (value == null)
        {
            sb.AppendLine(prefix + "- <null>");
            return;
        }

        if (value is string || value.GetType().IsPrimitive || value is decimal || value is DateTime || value is DateTimeOffset)
        {
            sb.AppendLine(prefix + "- " + value);
            return;
        }

        if (value is IDictionary dictionary)
        {
            sb.AppendLine(prefix + "- item " + index);
            foreach (DictionaryEntry entry in dictionary)
            {
                AppendValue(sb, Convert.ToString(entry.Key) ?? "<key>", entry.Value, indent + 2);
            }

            return;
        }

        sb.AppendLine(prefix + "- " + value);
    }
}
