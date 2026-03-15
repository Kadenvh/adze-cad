using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adze.Contracts.Models;

namespace Adze.Host.Services;

internal static class GroundingToolResultsBuilder
{
    public static string Build(IEnumerable<ToolResult> toolResults)
    {
        List<ToolResult> results = toolResults?.ToList() ?? new List<ToolResult>();
        if (results.Count == 0)
        {
            return "Run the assistant to see the last grounded tool execution results.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Executed tools");
        sb.AppendLine("--------------");

        foreach (ToolResult result in results)
        {
            sb.AppendLine(result.ToolName + " [" + (result.Success ? "ok" : "failed") + "]");
            sb.AppendLine("summary: " + result.Summary);

            foreach (KeyValuePair<string, object?> entry in result.Data.Take(6))
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

        return sb.ToString().TrimEnd();
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

            int count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (count >= 4)
                {
                    sb.AppendLine(prefix + "  ...");
                    break;
                }

                AppendValue(sb, Convert.ToString(entry.Key) ?? "<key>", entry.Value, indent + 2);
                count++;
            }

            return;
        }

        if (value is IEnumerable enumerable)
        {
            sb.AppendLine(prefix + label + ":");
            int count = 0;
            foreach (object? item in enumerable)
            {
                if (count >= 4)
                {
                    sb.AppendLine(prefix + "  ...");
                    return;
                }

                AppendEnumerableItem(sb, item, indent + 2);
                count++;
            }

            if (count == 0)
            {
                sb.AppendLine(prefix + "  <empty>");
            }

            return;
        }

        sb.AppendLine(prefix + label + ": " + value);
    }

    private static void AppendEnumerableItem(StringBuilder sb, object? value, int indent)
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
            sb.AppendLine(prefix + "- item");
            int count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (count >= 4)
                {
                    sb.AppendLine(prefix + "  ...");
                    break;
                }

                AppendValue(sb, Convert.ToString(entry.Key) ?? "<key>", entry.Value, indent + 2);
                count++;
            }

            return;
        }

        sb.AppendLine(prefix + "- " + value);
    }
}
