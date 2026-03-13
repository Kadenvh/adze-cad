using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;
using Adze.Broker.Models;

namespace Adze.Broker.Clients;

public sealed class AnthropicMessagesModelClient : IModelClient
{
    private sealed class TextCompletionResult
    {
        public bool Success { get; set; }

        public string AssistantText { get; set; } = string.Empty;

        public string RawResponseText { get; set; } = string.Empty;

        public string FailureReason { get; set; } = string.Empty;
    }

    private readonly BrokerModelSettings _settings;

    public AnthropicMessagesModelClient(BrokerModelSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public ModelTurnResult Complete(BrokerPrompt prompt)
    {
        if (!_settings.IsUsable())
        {
            return new ModelTurnResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                FailureReason = "Model settings are incomplete."
            };
        }

        TextCompletionResult completion = ExecuteTextPrompt(
            prompt.SystemPrompt,
            prompt.UserPrompt,
            _settings.MaxTokens,
            _settings.TimeoutMilliseconds);

        if (!completion.Success)
        {
            return new ModelTurnResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                RawResponseText = completion.RawResponseText,
                FailureReason = completion.FailureReason
            };
        }

        if (!TryExtractJsonPayload(completion.AssistantText, out string payloadText, out string payloadFailure))
        {
            return new ModelTurnResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                RawResponseText = completion.AssistantText,
                FailureReason = payloadFailure
            };
        }

        if (!TryParseBrokerResponse(payloadText, out BrokerResponse? parsedResponse, out string brokerFailure))
        {
            return new ModelTurnResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                RawResponseText = completion.AssistantText,
                FailureReason = brokerFailure
            };
        }

        return new ModelTurnResult
        {
            Success = true,
            Provider = _settings.Provider,
            Model = _settings.Model,
            RawResponseText = completion.AssistantText,
            Response = parsedResponse,
            Summary = parsedResponse?.Summary ?? string.Empty,
            RequestedTools = parsedResponse?.RecommendedTools.Select(item => item.ToolName).ToList() ?? new List<string>()
        };
    }

    public AssistantSynthesisResult Synthesize(AssistantSynthesisPrompt prompt)
    {
        if (!_settings.IsUsable())
        {
            return new AssistantSynthesisResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                FailureReason = "Model settings are incomplete."
            };
        }

        TextCompletionResult completion = ExecuteTextPrompt(
            prompt.SystemPrompt,
            prompt.UserPrompt,
            _settings.SynthesisMaxTokens,
            _settings.SynthesisTimeoutMilliseconds);

        if (!completion.Success)
        {
            return new AssistantSynthesisResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                RawResponseText = completion.RawResponseText,
                FailureReason = completion.FailureReason
            };
        }

        return new AssistantSynthesisResult
        {
            Success = true,
            Provider = _settings.Provider,
            Model = _settings.Model,
            RawResponseText = completion.RawResponseText,
            ResponseText = completion.AssistantText.Trim()
        };
    }

    private TextCompletionResult ExecuteTextPrompt(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        int timeoutMilliseconds)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            string requestBody = CreateSerializer().Serialize(new Dictionary<string, object?>
            {
                ["model"] = _settings.Model,
                ["max_tokens"] = maxTokens,
                ["temperature"] = _settings.Temperature,
                ["system"] = systemPrompt,
                ["messages"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = userPrompt
                    }
                }
            });

            var request = (HttpWebRequest)WebRequest.Create(_settings.Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = timeoutMilliseconds;
            request.ReadWriteTimeout = timeoutMilliseconds;
            request.Headers["x-api-key"] = _settings.ApiKey;
            request.Headers["anthropic-version"] = _settings.ApiVersion;

            byte[] payloadBytes = Encoding.UTF8.GetBytes(requestBody);
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(payloadBytes, 0, payloadBytes.Length);
            }

            using var response = (HttpWebResponse)request.GetResponse();
            using var responseStream = response.GetResponseStream();
            using var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8);
            string responseText = reader.ReadToEnd();

            if (!TryParseAssistantText(responseText, out string assistantText, out string parseFailure))
            {
                return new TextCompletionResult
                {
                    RawResponseText = responseText,
                    FailureReason = parseFailure
                };
            }

            return new TextCompletionResult
            {
                Success = true,
                RawResponseText = responseText,
                AssistantText = assistantText
            };
        }
        catch (WebException ex)
        {
            string responseText = ReadErrorBody(ex);
            return new TextCompletionResult
            {
                RawResponseText = responseText,
                FailureReason = string.IsNullOrWhiteSpace(responseText) ? ex.Message : responseText
            };
        }
        catch (Exception ex)
        {
            return new TextCompletionResult
            {
                FailureReason = ex.Message
            };
        }
    }

    private static bool TryParseAssistantText(string responseText, out string assistantText, out string failure)
    {
        assistantText = string.Empty;
        failure = string.Empty;

        object? payload = CreateSerializer().DeserializeObject(responseText);
        if (payload is not IDictionary<string, object> responseDictionary)
        {
            failure = "Model response was not a JSON object.";
            return false;
        }

        if (!responseDictionary.TryGetValue("content", out object? contentValue) || contentValue is not IEnumerable contentItems)
        {
            failure = "Model response did not contain content blocks.";
            return false;
        }

        var textParts = new List<string>();
        foreach (object? item in contentItems)
        {
            if (item is IDictionary<string, object> itemDictionary &&
                itemDictionary.TryGetValue("type", out object? typeValue) &&
                string.Equals(Convert.ToString(typeValue), "text", StringComparison.OrdinalIgnoreCase) &&
                itemDictionary.TryGetValue("text", out object? textValue))
            {
                string text = Convert.ToString(textValue) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textParts.Add(text);
                }
            }
        }

        if (textParts.Count == 0)
        {
            failure = "Model response did not contain any text content blocks.";
            return false;
        }

        assistantText = string.Join(Environment.NewLine, textParts);
        return true;
    }

    private static bool TryExtractJsonPayload(string assistantText, out string payloadText, out string failure)
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

    private static bool TryParseBrokerResponse(string payloadText, out BrokerResponse? response, out string failure)
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

    private static string ReadErrorBody(WebException ex)
    {
        using Stream? responseStream = ex.Response?.GetResponseStream();
        if (responseStream == null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static JavaScriptSerializer CreateSerializer()
    {
        return new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue
        };
    }
}
