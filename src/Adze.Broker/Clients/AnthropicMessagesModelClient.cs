using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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

        public ModelUsage Usage { get; set; } = new();
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
                FailureReason = completion.FailureReason,
                Usage = completion.Usage
            };
        }

        if (!ModelResponseParser.TryExtractJsonPayload(completion.AssistantText, out string payloadText, out string payloadFailure))
        {
            return new ModelTurnResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                RawResponseText = completion.AssistantText,
                FailureReason = payloadFailure,
                Usage = completion.Usage
            };
        }

        if (!ModelResponseParser.TryParseBrokerResponse(payloadText, out BrokerResponse? parsedResponse, out string brokerFailure))
        {
            return new ModelTurnResult
            {
                Provider = _settings.Provider,
                Model = _settings.Model,
                RawResponseText = completion.AssistantText,
                FailureReason = brokerFailure,
                Usage = completion.Usage
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
            RequestedTools = parsedResponse?.RecommendedTools.Select(item => item.ToolName).ToList() ?? new List<string>(),
            Usage = completion.Usage
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
                FailureReason = completion.FailureReason,
                Usage = completion.Usage
            };
        }

        return new AssistantSynthesisResult
        {
            Success = true,
            Provider = _settings.Provider,
            Model = _settings.Model,
            RawResponseText = completion.RawResponseText,
            ResponseText = completion.AssistantText.Trim(),
            Usage = completion.Usage
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

            string requestBody = ModelResponseParser.CreateSerializer().Serialize(new Dictionary<string, object?>
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

            ModelUsage usage = ModelResponseParser.ParseUsage(responseText);

            if (!TryParseAssistantText(responseText, out string assistantText, out string parseFailure))
            {
                return new TextCompletionResult
                {
                    RawResponseText = responseText,
                    FailureReason = parseFailure,
                    Usage = usage
                };
            }

            return new TextCompletionResult
            {
                Success = true,
                RawResponseText = responseText,
                AssistantText = assistantText,
                Usage = usage
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

        object? payload = ModelResponseParser.CreateSerializer().DeserializeObject(responseText);
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
}
