using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Adze.Broker.Configuration;
using Adze.Broker.Infrastructure;

namespace Adze.Broker.Clients;

/// <summary>
/// Probes whether a local model endpoint supports tool calling by sending a minimal
/// tool-calling request and checking if the response contains valid tool_calls.
/// Results are cached per provider+model combination.
/// </summary>
public static class ToolCallCapabilityProbe
{
    private static ToolCallProbeResult? _cachedResult;
    private static string? _cachedProvider;
    private static string? _cachedModel;

    /// <summary>
    /// Returns the cached probe result for the given settings, running the probe if needed.
    /// Cloud providers always return Supported without probing.
    /// </summary>
    public static ToolCallProbeResult GetOrProbe(BrokerModelSettings settings, int timeoutMs = 5000)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        if (!settings.IsLocalProvider)
        {
            return new ToolCallProbeResult
            {
                Capability = ToolCallCapability.Supported,
                Message = "Cloud providers support tool calling."
            };
        }

        // Return cached if same provider+model
        if (_cachedResult != null &&
            string.Equals(_cachedProvider, settings.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_cachedModel, settings.Model, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedResult;
        }

        var result = Probe(settings, timeoutMs);
        _cachedResult = result;
        _cachedProvider = settings.Provider;
        _cachedModel = settings.Model;
        BrokerDiagnostics.Info(
            "ToolProbe: provider=" + settings.Provider +
            " model=" + settings.Model +
            " capability=" + result.Capability);
        return result;
    }

    /// <summary>
    /// Sends a minimal tool-calling request to probe whether the local model supports tool calling.
    /// </summary>
    public static ToolCallProbeResult Probe(BrokerModelSettings settings, int timeoutMs = 5000)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        if (!settings.IsLocalProvider)
        {
            return new ToolCallProbeResult
            {
                Capability = ToolCallCapability.Supported,
                Message = "Cloud providers support tool calling."
            };
        }

        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var serializer = ModelResponseParser.CreateSerializer();
            string requestBody = serializer.Serialize(BuildProbeRequest(settings.Model));

            var request = (HttpWebRequest)WebRequest.Create(settings.Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + settings.ApiKey;

            byte[] payloadBytes = Encoding.UTF8.GetBytes(requestBody);
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(payloadBytes, 0, payloadBytes.Length);
            }

            using var response = (HttpWebResponse)request.GetResponse();
            using var responseStream = response.GetResponseStream();
            using var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8);
            string responseText = reader.ReadToEnd();

            return ParseProbeResponse(responseText, settings.Provider);
        }
        catch (WebException ex)
        {
            return new ToolCallProbeResult
            {
                Capability = ToolCallCapability.Unknown,
                Message = "Probe failed: " + ex.Message
            };
        }
        catch (Exception ex)
        {
            return new ToolCallProbeResult
            {
                Capability = ToolCallCapability.Unknown,
                Message = "Probe failed: " + ex.Message
            };
        }
    }

    /// <summary>
    /// Clears the cached probe result. Useful for tests.
    /// </summary>
    public static void ClearCache()
    {
        _cachedResult = null;
        _cachedProvider = null;
        _cachedModel = null;
    }

    internal static Dictionary<string, object?> BuildProbeRequest(string model)
    {
        return new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = "What time is it right now?"
                }
            },
            ["tools"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = "get_current_time",
                        ["description"] = "Returns the current date and time.",
                        ["parameters"] = new Dictionary<string, object?>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object?>(),
                            ["required"] = new string[0]
                        }
                    }
                }
            },
            ["max_tokens"] = 100,
            ["temperature"] = 0.0
        };
    }

    internal static ToolCallProbeResult ParseProbeResponse(string responseText, string provider)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new ToolCallProbeResult
            {
                Capability = ToolCallCapability.Unknown,
                Message = "Empty response from " + provider + "."
            };
        }

        try
        {
            var serializer = ModelResponseParser.CreateSerializer();
            object? payload = serializer.DeserializeObject(responseText);

            if (payload is IDictionary<string, object> root &&
                root.TryGetValue("choices", out object? choicesValue) &&
                choicesValue is object[] choices &&
                choices.Length > 0)
            {
                var firstChoice = choices[0] as IDictionary<string, object>;
                if (firstChoice == null)
                {
                    return new ToolCallProbeResult
                    {
                        Capability = ToolCallCapability.Unknown,
                        Message = "Could not parse response from " + provider + "."
                    };
                }

                // Check if there's a message with tool_calls
                if (firstChoice.TryGetValue("message", out object? msgValue) &&
                    msgValue is IDictionary<string, object> msg &&
                    msg.TryGetValue("tool_calls", out object? toolCallsValue) &&
                    toolCallsValue is object[] toolCalls &&
                    toolCalls.Length > 0)
                {
                    // Validate the tool call has the expected structure
                    var firstCall = toolCalls[0] as IDictionary<string, object>;
                    if (firstCall != null &&
                        firstCall.TryGetValue("function", out object? funcValue) &&
                        funcValue is IDictionary<string, object> func &&
                        func.ContainsKey("name"))
                    {
                        string toolName = func["name"]?.ToString() ?? "";
                        return new ToolCallProbeResult
                        {
                            Capability = ToolCallCapability.Supported,
                            Message = provider + " supports tool calling (probe tool: " + toolName + ")."
                        };
                    }
                }

                // Response has choices but no tool_calls — model responded with text
                string finishReason = firstChoice.TryGetValue("finish_reason", out object? frValue)
                    ? frValue?.ToString() ?? ""
                    : "";

                return new ToolCallProbeResult
                {
                    Capability = ToolCallCapability.NotSupported,
                    Message = provider + " responded with text instead of tool calls (finish_reason=" + finishReason + "). Tool calling may not be supported by this model."
                };
            }
        }
        catch
        {
            // Parse failure — fall through to unknown
        }

        return new ToolCallProbeResult
        {
            Capability = ToolCallCapability.Unknown,
            Message = "Could not parse probe response from " + provider + "."
        };
    }
}

public enum ToolCallCapability
{
    Unknown,
    Supported,
    NotSupported
}

public sealed class ToolCallProbeResult
{
    public ToolCallCapability Capability { get; set; } = ToolCallCapability.Unknown;

    public string Message { get; set; } = string.Empty;
}
