using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;
using Adze.Broker.Models;

namespace Adze.Broker.Clients;

public sealed class OpenAIModelClient : IModelClient
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

    public OpenAIModelClient(BrokerModelSettings settings)
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
            RequestedTools = parsedResponse?.RecommendedTools.ConvertAll(item => item.ToolName) ?? new List<string>(),
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
                ["messages"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["role"] = "system",
                        ["content"] = systemPrompt
                    },
                    new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = userPrompt
                    }
                },
                ["max_tokens"] = maxTokens,
                ["temperature"] = _settings.Temperature
            });

            var request = (HttpWebRequest)WebRequest.Create(_settings.Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = timeoutMilliseconds;
            request.ReadWriteTimeout = timeoutMilliseconds;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + _settings.ApiKey;

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
            string responseText = ReadResponseBody(ex.Response);
            return new TextCompletionResult
            {
                RawResponseText = responseText,
                FailureReason = ParseErrorResponse(responseText, ex)
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

        if (!responseDictionary.TryGetValue("choices", out object? choicesValue) || choicesValue is not IEnumerable choices)
        {
            failure = "Model response did not contain choices.";
            return false;
        }

        foreach (object? item in choices)
        {
            if (item is not IDictionary<string, object> choiceDictionary ||
                !choiceDictionary.TryGetValue("message", out object? messageValue) ||
                messageValue is not IDictionary<string, object> messageDictionary ||
                !messageDictionary.TryGetValue("content", out object? contentValue))
            {
                continue;
            }

            string text = ExtractMessageContent(contentValue);
            if (!string.IsNullOrWhiteSpace(text))
            {
                assistantText = text.Trim();
                return true;
            }
        }

        failure = "Model response did not contain assistant message content.";
        return false;
    }

    private static string ExtractMessageContent(object? contentValue)
    {
        if (contentValue is string contentText)
        {
            return contentText;
        }

        if (contentValue is not IEnumerable contentItems)
        {
            return string.Empty;
        }

        var textParts = new List<string>();
        foreach (object? item in contentItems)
        {
            if (item is IDictionary<string, object> contentDictionary &&
                contentDictionary.TryGetValue("text", out object? textValue))
            {
                string text = Convert.ToString(textValue) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textParts.Add(text);
                }
            }
        }

        return string.Join(Environment.NewLine, textParts);
    }

    private static string ParseErrorResponse(string responseText, WebException ex)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                object? payload = ModelResponseParser.CreateSerializer().DeserializeObject(responseText);
                if (payload is IDictionary<string, object> responseDictionary &&
                    responseDictionary.TryGetValue("error", out object? errorValue) &&
                    errorValue is IDictionary<string, object> errorDictionary)
                {
                    string message = TryReadErrorValue(errorDictionary, "message");
                    string type = TryReadErrorValue(errorDictionary, "type");
                    string code = TryReadErrorValue(errorDictionary, "code");
                    string reason = string.Join(
                        " | ",
                        new[] { message.Trim(), type.Trim(), code.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));

                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        return reason;
                    }
                }
            }
            catch
            {
            }

            return responseText;
        }

        return ex.Message;
    }

    private static string TryReadErrorValue(IDictionary<string, object> dictionary, string key)
    {
        return dictionary.TryGetValue(key, out object? value)
            ? Convert.ToString(value) ?? string.Empty
            : string.Empty;
    }

    private static string ReadResponseBody(WebResponse? response)
    {
        using Stream? responseStream = response?.GetResponseStream();
        if (responseStream == null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
