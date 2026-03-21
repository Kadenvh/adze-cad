using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;
using Adze.Broker.Models;

namespace Adze.Broker.Clients;

/// <summary>
/// Agent model client for OpenAI-compatible tool-calling endpoints.
/// Supports OpenAI, OpenRouter, Ollama, and LM Studio.
/// Implements the agentic loop contract: send a turn with tool definitions,
/// parse tool calls or text responses, and build messages for conversation history.
/// </summary>
public sealed class OpenAIFormatAgentClient : IStreamingAgentModelClient
{
    private readonly BrokerModelSettings _settings;

    public OpenAIFormatAgentClient(BrokerModelSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings)
    {
        const int maxRetries = 1;
        for (int attempt = 0; ; attempt++)
        {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var serializer = ModelResponseParser.CreateSerializer();

            var messages = new List<object>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                });
            }

            messages.AddRange(conversationHistory);

            var requestPayload = new Dictionary<string, object?>
            {
                ["model"] = _settings.Model,
                ["messages"] = messages,
                ["max_tokens"] = settings.MaxTokens,
                ["temperature"] = settings.Temperature
            };

            if (toolDefinitions.Count > 0)
            {
                requestPayload["tools"] = BuildToolDefinitions(toolDefinitions);
            }

            if (settings.DisableParallelToolCalls && toolDefinitions.Count > 0)
            {
                requestPayload["parallel_tool_calls"] = false;
            }

            string requestBody = serializer.Serialize(requestPayload);

            var request = (HttpWebRequest)WebRequest.Create(_settings.Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = settings.TimeoutMilliseconds;
            request.ReadWriteTimeout = settings.TimeoutMilliseconds;
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

            return ParseResponse(responseText, usage);
        }
        catch (WebException ex) when (RateLimitHelper.IsRateLimited(ex) && attempt < maxRetries)
        {
            RateLimitHelper.WaitForRetry(ex);
            continue;
        }
        catch (WebException ex)
        {
            if (RateLimitHelper.IsRateLimited(ex))
            {
                return new AgentTurnResponse
                {
                    Success = false,
                    StopReason = AgentStopReason.Error,
                    FailureReason = RateLimitHelper.FormatRateLimitMessage(ex, 0),
                    Provider = _settings.Provider,
                    Model = _settings.Model
                };
            }
            string responseText = ReadResponseBody(ex.Response);
            string failureReason = ParseErrorResponse(responseText, ex);

            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = failureReason,
                Provider = _settings.Provider,
                Model = _settings.Model
            };
        }
        catch (Exception ex)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = ex.Message,
                Provider = _settings.Provider,
                Model = _settings.Model
            };
        }
        } // end retry loop
    }

    public AgentTurnResponse SendTurnStreaming(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        Action<string> onTextChunk)
    {
        const int maxRetries = 1;
        for (int attempt = 0; ; attempt++)
        {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var serializer = ModelResponseParser.CreateSerializer();

            var messages = new List<object>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                });
            }

            messages.AddRange(conversationHistory);

            var requestPayload = new Dictionary<string, object?>
            {
                ["model"] = _settings.Model,
                ["messages"] = messages,
                ["max_tokens"] = settings.MaxTokens,
                ["temperature"] = settings.Temperature,
                ["stream"] = true,
                ["stream_options"] = new Dictionary<string, object?> { ["include_usage"] = true }
            };

            if (toolDefinitions.Count > 0)
            {
                requestPayload["tools"] = BuildToolDefinitions(toolDefinitions);
            }

            if (settings.DisableParallelToolCalls && toolDefinitions.Count > 0)
            {
                requestPayload["parallel_tool_calls"] = false;
            }

            string requestBody = serializer.Serialize(requestPayload);

            var request = (HttpWebRequest)WebRequest.Create(_settings.Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "text/event-stream";
            request.Timeout = settings.TimeoutMilliseconds;
            request.ReadWriteTimeout = settings.TimeoutMilliseconds;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + _settings.ApiKey;

            byte[] payloadBytes = Encoding.UTF8.GetBytes(requestBody);
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(payloadBytes, 0, payloadBytes.Length);
            }

            using var response = (HttpWebResponse)request.GetResponse();
            using var responseStream = response.GetResponseStream();
            using var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8);

            return ReadStreamingAgentResponse(reader, onTextChunk);
        }
        catch (WebException ex) when (RateLimitHelper.IsRateLimited(ex) && attempt < maxRetries)
        {
            RateLimitHelper.WaitForRetry(ex);
            continue;
        }
        catch (WebException ex)
        {
            if (RateLimitHelper.IsRateLimited(ex))
            {
                return new AgentTurnResponse
                {
                    Success = false,
                    StopReason = AgentStopReason.Error,
                    FailureReason = RateLimitHelper.FormatRateLimitMessage(ex, 0),
                    Provider = _settings.Provider,
                    Model = _settings.Model
                };
            }
            string responseText = ReadResponseBody(ex.Response);
            string failureReason = ParseErrorResponse(responseText, ex);

            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = failureReason,
                Provider = _settings.Provider,
                Model = _settings.Model
            };
        }
        catch (Exception ex)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = ex.Message,
                Provider = _settings.Provider,
                Model = _settings.Model
            };
        }
        } // end retry loop
    }

    public object BuildUserMessage(string content)
    {
        return new Dictionary<string, object?>
        {
            ["role"] = "user",
            ["content"] = content
        };
    }

    public List<object> BuildToolResultMessages(List<AgentToolResult> results)
    {
        var messages = new List<object>();

        foreach (AgentToolResult result in results)
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "tool",
                ["tool_call_id"] = result.ToolCallId,
                ["content"] = result.OutputJson
            });
        }

        return messages;
    }

    private static List<object> BuildToolDefinitions(List<AgentToolDefinition> toolDefinitions)
    {
        var tools = new List<object>();

        foreach (AgentToolDefinition toolDef in toolDefinitions)
        {
            var functionObject = new Dictionary<string, object?>
            {
                ["name"] = toolDef.Name,
                ["description"] = toolDef.Description
            };

            if (toolDef.ParameterSchema.Count > 0)
            {
                functionObject["parameters"] = toolDef.ParameterSchema;
            }
            else
            {
                functionObject["parameters"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>()
                };
            }

            tools.Add(new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = functionObject
            });
        }

        return tools;
    }

    private AgentTurnResponse ParseResponse(string responseText, ModelUsage usage)
    {
        var serializer = ModelResponseParser.CreateSerializer();

        object? payload = serializer.DeserializeObject(responseText);
        if (payload is not IDictionary<string, object> responseDictionary)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = "Model response was not a JSON object.",
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        if (!responseDictionary.TryGetValue("choices", out object? choicesValue) || choicesValue is not IEnumerable choices)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = "Model response did not contain choices.",
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        IDictionary<string, object>? firstChoice = null;
        foreach (object? item in choices)
        {
            if (item is IDictionary<string, object> choiceDict)
            {
                firstChoice = choiceDict;
                break;
            }
        }

        if (firstChoice == null)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = "Model response choices array was empty or malformed.",
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        string finishReason = firstChoice.TryGetValue("finish_reason", out object? finishReasonValue)
            ? Convert.ToString(finishReasonValue) ?? string.Empty
            : string.Empty;

        if (!firstChoice.TryGetValue("message", out object? messageValue) ||
            messageValue is not IDictionary<string, object> messageDictionary)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = "Model response choice did not contain a message.",
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        if (string.Equals(finishReason, "tool_calls", StringComparison.OrdinalIgnoreCase))
        {
            return ParseToolCallResponse(messageDictionary, usage);
        }

        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
        {
            string partialText = ExtractTextContent(messageDictionary);
            return new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.MaxTokens,
                TextContent = partialText,
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        // finish_reason == "stop" or other terminal reasons: extract text content
        string textContent = ExtractTextContent(messageDictionary);
        return new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = textContent,
            Provider = _settings.Provider,
            Model = _settings.Model,
            Usage = usage
        };
    }

    private AgentTurnResponse ParseToolCallResponse(
        IDictionary<string, object> messageDictionary,
        ModelUsage usage)
    {
        if (!messageDictionary.TryGetValue("tool_calls", out object? toolCallsValue) ||
            toolCallsValue is not IEnumerable toolCallsArray)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = "finish_reason was tool_calls but no tool_calls array found in message.",
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        var serializer = ModelResponseParser.CreateSerializer();
        var toolCalls = new List<AgentToolCall>();

        foreach (object? item in toolCallsArray)
        {
            if (item is not IDictionary<string, object> toolCallDict)
            {
                continue;
            }

            string id = toolCallDict.TryGetValue("id", out object? idValue)
                ? Convert.ToString(idValue) ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!toolCallDict.TryGetValue("function", out object? functionValue) ||
                functionValue is not IDictionary<string, object> functionDict)
            {
                continue;
            }

            string name = functionDict.TryGetValue("name", out object? nameValue)
                ? Convert.ToString(nameValue) ?? string.Empty
                : string.Empty;

            string argumentsJson = functionDict.TryGetValue("arguments", out object? argsValue)
                ? Convert.ToString(argsValue) ?? string.Empty
                : string.Empty;

            Dictionary<string, object?> arguments = ParseArgumentsJson(serializer, argumentsJson);

            toolCalls.Add(new AgentToolCall
            {
                Id = id,
                Name = name,
                Arguments = arguments,
                ArgumentsJson = argumentsJson
            });
        }

        if (toolCalls.Count == 0)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = "finish_reason was tool_calls but no valid tool calls could be parsed.",
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        // Also extract any text content the model may have included alongside tool calls
        string textContent = ExtractTextContent(messageDictionary);

        // Build the raw assistant message for conversation history echo-back.
        // The conversation must include the assistant's tool_call message before
        // the corresponding tool result messages.
        object rawAssistantMessage = BuildRawAssistantMessage(messageDictionary);

        return new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            TextContent = textContent,
            ToolCalls = toolCalls,
            Provider = _settings.Provider,
            Model = _settings.Model,
            Usage = usage,
            RawAssistantMessage = rawAssistantMessage
        };
    }

    private static Dictionary<string, object?> ParseArgumentsJson(
        System.Web.Script.Serialization.JavaScriptSerializer serializer,
        string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            object? parsed = serializer.DeserializeObject(argumentsJson);
            if (parsed is IDictionary<string, object> parsedDict)
            {
                var result = new Dictionary<string, object?>();
                foreach (KeyValuePair<string, object> kvp in parsedDict)
                {
                    result[kvp.Key] = kvp.Value;
                }

                return result;
            }
        }
        catch
        {
            // Model may emit malformed JSON for arguments.
            // Fall through and return an empty dictionary so the caller
            // still has the raw ArgumentsJson string to work with.
        }

        return new Dictionary<string, object?>();
    }

    private static object BuildRawAssistantMessage(IDictionary<string, object> messageDictionary)
    {
        // Reconstruct a clean assistant message dictionary that preserves
        // the role, content (which may be null), and tool_calls array.
        // This dictionary will be appended directly to the conversation history.
        var assistantMessage = new Dictionary<string, object?>
        {
            ["role"] = "assistant"
        };

        if (messageDictionary.TryGetValue("content", out object? contentValue) && contentValue != null)
        {
            assistantMessage["content"] = contentValue;
        }
        else
        {
            assistantMessage["content"] = null;
        }

        if (messageDictionary.TryGetValue("tool_calls", out object? toolCallsValue))
        {
            assistantMessage["tool_calls"] = toolCallsValue;
        }

        return assistantMessage;
    }

    private static string ExtractTextContent(IDictionary<string, object> messageDictionary)
    {
        if (!messageDictionary.TryGetValue("content", out object? contentValue) || contentValue == null)
        {
            return string.Empty;
        }

        if (contentValue is string contentText)
        {
            return contentText.Trim();
        }

        // Handle content as an array of content parts (some models return this format)
        if (contentValue is IEnumerable contentItems)
        {
            var textParts = new List<string>();
            foreach (object? item in contentItems)
            {
                if (item is IDictionary<string, object> contentPart &&
                    contentPart.TryGetValue("text", out object? textValue))
                {
                    string text = Convert.ToString(textValue) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textParts.Add(text);
                    }
                }
            }

            return string.Join(Environment.NewLine, textParts).Trim();
        }

        // Fallback: try Convert.ToString for unexpected types
        string fallbackText = Convert.ToString(contentValue) ?? string.Empty;
        return fallbackText.Trim();
    }

    /// <summary>
    /// Reads an SSE stream from the model, detecting whether the response contains
    /// tool calls or text content. Text content streams via onTextChunk; tool calls
    /// are buffered and reassembled into an AgentTurnResponse.
    /// </summary>
    private AgentTurnResponse ReadStreamingAgentResponse(
        StreamReader reader,
        Action<string> onTextChunk)
    {
        var serializer = ModelResponseParser.CreateSerializer();
        var fullText = new StringBuilder();
        string? finishReason = null;
        ModelUsage usage = new();

        // Tool call accumulation: index → (id, name, arguments)
        bool isToolCallResponse = false;
        bool modeDetected = false;
        var toolCallAccumulators = new Dictionary<int, StreamingToolCallAccumulator>();

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            string data = line.Substring(6);

            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                break;

            // Parse the chunk JSON
            object? payload;
            try
            {
                payload = serializer.DeserializeObject(data);
            }
            catch
            {
                continue; // Skip malformed JSON
            }

            if (payload is not IDictionary<string, object> root)
                continue;

            // Extract usage from final chunk
            if (root.TryGetValue("usage", out object? usageValue) &&
                usageValue is IDictionary<string, object> usageDict)
            {
                usage = new ModelUsage
                {
                    PromptTokens = TryGetInt(usageDict, "prompt_tokens"),
                    CompletionTokens = TryGetInt(usageDict, "completion_tokens"),
                    TotalTokens = TryGetInt(usageDict, "total_tokens")
                };
            }

            if (!root.TryGetValue("choices", out object? choicesValue) ||
                choicesValue is not object[] choices ||
                choices.Length == 0 ||
                choices[0] is not IDictionary<string, object> firstChoice)
            {
                continue;
            }

            // Extract finish_reason
            if (firstChoice.TryGetValue("finish_reason", out object? frValue) && frValue != null)
            {
                string fr = Convert.ToString(frValue) ?? string.Empty;
                if (!string.IsNullOrEmpty(fr))
                    finishReason = fr;
            }

            if (!firstChoice.TryGetValue("delta", out object? deltaValue) ||
                deltaValue is not IDictionary<string, object> delta)
            {
                continue;
            }

            // Detect mode from first meaningful delta
            if (!modeDetected)
            {
                if (delta.ContainsKey("tool_calls"))
                {
                    isToolCallResponse = true;
                    modeDetected = true;
                }
                else if (delta.ContainsKey("content"))
                {
                    isToolCallResponse = false;
                    modeDetected = true;
                }
                // First chunk may only have "role" — skip and detect on next chunk
            }

            if (isToolCallResponse)
            {
                // Accumulate tool call deltas
                if (delta.TryGetValue("tool_calls", out object? tcValue) && tcValue is object[] toolCallDeltas)
                {
                    foreach (object? tcItem in toolCallDeltas)
                    {
                        if (tcItem is not IDictionary<string, object> tcDelta)
                            continue;

                        int index = tcDelta.TryGetValue("index", out object? idxVal) && idxVal != null
                            ? Convert.ToInt32(idxVal)
                            : 0;

                        if (!toolCallAccumulators.TryGetValue(index, out StreamingToolCallAccumulator? acc))
                        {
                            acc = new StreamingToolCallAccumulator();
                            toolCallAccumulators[index] = acc;
                        }

                        if (tcDelta.TryGetValue("id", out object? idVal) && idVal != null)
                        {
                            string id = Convert.ToString(idVal) ?? string.Empty;
                            if (!string.IsNullOrEmpty(id))
                                acc.Id = id;
                        }

                        if (tcDelta.TryGetValue("function", out object? fnVal) &&
                            fnVal is IDictionary<string, object> fnDelta)
                        {
                            if (fnDelta.TryGetValue("name", out object? nameVal) && nameVal != null)
                            {
                                string name = Convert.ToString(nameVal) ?? string.Empty;
                                if (!string.IsNullOrEmpty(name))
                                    acc.Name = name;
                            }

                            if (fnDelta.TryGetValue("arguments", out object? argsVal) && argsVal != null)
                            {
                                acc.Arguments.Append(Convert.ToString(argsVal) ?? string.Empty);
                            }
                        }
                    }
                }
            }
            else
            {
                // Stream text content via SseStreamReader.ParseChunk (reuse tested parser)
                SseChunkResult chunk = SseStreamReader.ParseChunk(data);
                if (!string.IsNullOrEmpty(chunk.ContentDelta))
                {
                    fullText.Append(chunk.ContentDelta);
                    onTextChunk(chunk.ContentDelta);
                }
            }
        }

        // Build the response
        if (isToolCallResponse)
        {
            return BuildStreamingToolCallResponse(toolCallAccumulators, finishReason, usage, serializer);
        }

        // Text response
        AgentStopReason stopReason = string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase)
            ? AgentStopReason.MaxTokens
            : AgentStopReason.EndTurn;

        return new AgentTurnResponse
        {
            Success = true,
            StopReason = stopReason,
            TextContent = fullText.ToString(),
            Provider = _settings.Provider,
            Model = _settings.Model,
            Usage = usage
        };
    }

    private AgentTurnResponse BuildStreamingToolCallResponse(
        Dictionary<int, StreamingToolCallAccumulator> accumulators,
        string? finishReason,
        ModelUsage usage,
        System.Web.Script.Serialization.JavaScriptSerializer serializer)
    {
        var toolCalls = new List<AgentToolCall>();

        foreach (KeyValuePair<int, StreamingToolCallAccumulator> kvp in accumulators)
        {
            StreamingToolCallAccumulator acc = kvp.Value;
            if (string.IsNullOrWhiteSpace(acc.Id))
                continue;

            string argumentsJson = acc.Arguments.ToString();
            Dictionary<string, object?> arguments = ParseArgumentsJson(serializer, argumentsJson);

            toolCalls.Add(new AgentToolCall
            {
                Id = acc.Id,
                Name = acc.Name,
                Arguments = arguments,
                ArgumentsJson = argumentsJson
            });
        }

        if (toolCalls.Count == 0)
        {
            return new AgentTurnResponse
            {
                Success = false,
                StopReason = AgentStopReason.Error,
                FailureReason = "Streaming response indicated tool_calls but no valid tool calls were accumulated.",
                Provider = _settings.Provider,
                Model = _settings.Model,
                Usage = usage
            };
        }

        // Build raw assistant message for conversation history echo-back
        var rawToolCalls = new List<object>();
        foreach (AgentToolCall tc in toolCalls)
        {
            rawToolCalls.Add(new Dictionary<string, object?>
            {
                ["id"] = tc.Id,
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = tc.Name,
                    ["arguments"] = tc.ArgumentsJson
                }
            });
        }

        object rawAssistantMessage = new Dictionary<string, object?>
        {
            ["role"] = "assistant",
            ["content"] = null,
            ["tool_calls"] = rawToolCalls
        };

        return new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = toolCalls,
            Provider = _settings.Provider,
            Model = _settings.Model,
            Usage = usage,
            RawAssistantMessage = rawAssistantMessage
        };
    }

    private static int TryGetInt(IDictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out object? value) && value != null)
        {
            if (value is int intVal) return intVal;
            if (int.TryParse(Convert.ToString(value), out int parsed)) return parsed;
        }
        return 0;
    }

    /// <summary>
    /// Accumulates streaming SSE deltas for a single tool call.
    /// </summary>
    private sealed class StreamingToolCallAccumulator
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder Arguments { get; } = new();
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
                    string message = TryReadStringValue(errorDictionary, "message");
                    string type = TryReadStringValue(errorDictionary, "type");
                    string code = TryReadStringValue(errorDictionary, "code");
                    string reason = string.Join(
                        " | ",
                        new[] { message.Trim(), type.Trim(), code.Trim() }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));

                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        return reason;
                    }
                }
            }
            catch
            {
                // Could not parse error JSON; fall through to raw text.
            }

            return responseText;
        }

        return ex.Message;
    }

    private static string TryReadStringValue(IDictionary<string, object> dictionary, string key)
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
