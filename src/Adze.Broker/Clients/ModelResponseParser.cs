using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using Adze.Broker.Models;

namespace Adze.Broker.Clients;

internal static class ModelResponseParser
{
    public static bool TryExtractJsonPayload(string assistantText, out string payloadText, out string failure)
    {
        payloadText = string.Empty;
        failure = string.Empty;

        const string fence = "```";
        int fenceStart = assistantText.IndexOf(fence, StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            int contentStart = assistantText.IndexOf('\n', fenceStart);
            int fenceEnd = assistantText.IndexOf(fence, contentStart >= 0 ? contentStart : fenceStart + fence.Length, StringComparison.Ordinal);
            if (contentStart >= 0 && fenceEnd > contentStart)
            {
                payloadText = assistantText.Substring(contentStart + 1, fenceEnd - contentStart - 1).Trim();
                if (payloadText.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    payloadText = payloadText.Substring(4).Trim();
                }

                if (!string.IsNullOrWhiteSpace(payloadText))
                {
                    return true;
                }
            }
        }

        int objectStart = assistantText.IndexOf('{');
        int objectEnd = assistantText.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            payloadText = assistantText.Substring(objectStart, objectEnd - objectStart + 1).Trim();
            return true;
        }

        failure = "Model response did not contain a JSON object.";
        return false;
    }

    public static bool TryParseBrokerResponse(string payloadText, out BrokerResponse? response, out string failure)
    {
        response = null;
        failure = string.Empty;

        object? payload = CreateSerializer().DeserializeObject(payloadText);
        if (payload is not IDictionary<string, object> dictionary)
        {
            failure = "Structured model payload was not a JSON object.";
            return false;
        }

        response = new BrokerResponse
        {
            Mode = "grounding",
            TurnStatus = ReadString(dictionary, "turn_status", "ready"),
            Intent = ReadString(dictionary, "intent", "general_grounding"),
            Confidence = ReadDouble(dictionary, "confidence"),
            Summary = ReadString(dictionary, "summary"),
            AssistantMessage = ReadString(dictionary, "assistant_message"),
            Blockers = ReadStringList(dictionary, "blockers"),
            RecoverySuggestions = ReadStringList(dictionary, "recovery_suggestions"),
            NextQuestions = ReadStringList(dictionary, "next_questions"),
            RecommendedTools = ReadRecommendations(dictionary, "recommended_tools")
        };

        return true;
    }

    public static JavaScriptSerializer CreateSerializer()
    {
        return new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue
        };
    }

    private static List<BrokerToolRecommendation> ReadRecommendations(IDictionary<string, object> dictionary, string key)
    {
        var recommendations = new List<BrokerToolRecommendation>();
        if (!dictionary.TryGetValue(key, out object? value) || value is not IEnumerable items)
        {
            return recommendations;
        }

        foreach (object? item in items)
        {
            if (item is not IDictionary<string, object> itemDictionary)
            {
                continue;
            }

            string toolName = ReadString(itemDictionary, "tool_name");
            if (string.IsNullOrWhiteSpace(toolName))
            {
                continue;
            }

            recommendations.Add(new BrokerToolRecommendation
            {
                ToolName = toolName,
                Reason = ReadString(itemDictionary, "reason"),
                Priority = Math.Max(0, ReadInteger(itemDictionary, "priority")),
                Score = ReadDouble(itemDictionary, "score")
            });
        }

        return recommendations;
    }

    private static List<string> ReadStringList(IDictionary<string, object> dictionary, string key)
    {
        var values = new List<string>();
        if (!dictionary.TryGetValue(key, out object? rawValue) || rawValue is not IEnumerable items)
        {
            return values;
        }

        foreach (object? item in items)
        {
            string value = Convert.ToString(item) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }

    private static string ReadString(IDictionary<string, object> dictionary, string key, string fallback = "")
    {
        if (!dictionary.TryGetValue(key, out object? value))
        {
            return fallback;
        }

        string stringValue = Convert.ToString(value) ?? string.Empty;
        return string.IsNullOrWhiteSpace(stringValue) ? fallback : stringValue.Trim();
    }

    private static int ReadInteger(IDictionary<string, object> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out object? value))
        {
            return 0;
        }

        return int.TryParse(Convert.ToString(value), out int parsed) ? parsed : 0;
    }

    private static double ReadDouble(IDictionary<string, object> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out object? value))
        {
            return 0;
        }

        return double.TryParse(Convert.ToString(value), out double parsed) ? parsed : 0;
    }
}
