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
/// Probes local model endpoints (Ollama, LM Studio) to check availability.
/// Pings GET /v1/models to verify the server is running and a model is loaded.
/// </summary>
public static class LocalEndpointHealthCheck
{
    /// <summary>
    /// Checks the health of a local model endpoint.
    /// Returns a result indicating whether the server is reachable and a model is available.
    /// </summary>
    public static LocalHealthResult Check(BrokerModelSettings settings, int timeoutMs = 5000)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        if (!settings.IsLocalProvider)
        {
            return new LocalHealthResult
            {
                Status = LocalHealthStatus.NotApplicable,
                Message = "Not a local provider."
            };
        }

        string modelsUrl = BuildModelsUrl(settings.Endpoint);

        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var request = (HttpWebRequest)WebRequest.Create(modelsUrl);
            request.Method = "GET";
            request.Accept = "application/json";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            using var response = (HttpWebResponse)request.GetResponse();
            using var responseStream = response.GetResponseStream();
            using var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8);
            string responseText = reader.ReadToEnd();

            LocalHealthResult result = ParseModelsResponse(responseText, settings.Model, settings.Provider);
            BrokerDiagnostics.Info(
                "HealthCheck: " + settings.Provider + " status=" + result.Status +
                " endpoint=" + modelsUrl +
                " model=" + settings.Model);
            return result;
        }
        catch (WebException ex) when (ex.Status == WebExceptionStatus.ConnectFailure ||
                                       ex.Status == WebExceptionStatus.Timeout ||
                                       ex.Status == WebExceptionStatus.NameResolutionFailure)
        {
            BrokerDiagnostics.Info(
                "HealthCheck: " + settings.Provider + " status=Unreachable" +
                " endpoint=" + modelsUrl +
                " reason=" + ex.Status);
            return new LocalHealthResult
            {
                Status = LocalHealthStatus.Unreachable,
                Message = settings.Provider + " server is not reachable at " + modelsUrl + ". " + ex.Status + "."
            };
        }
        catch (WebException ex)
        {
            string body = ReadResponseBody(ex.Response);
            return new LocalHealthResult
            {
                Status = LocalHealthStatus.Error,
                Message = settings.Provider + " endpoint returned an error: " +
                    (string.IsNullOrWhiteSpace(body) ? ex.Message : body)
            };
        }
        catch (Exception ex)
        {
            return new LocalHealthResult
            {
                Status = LocalHealthStatus.Error,
                Message = "Health check failed: " + ex.Message
            };
        }
    }

    /// <summary>
    /// Derives the /v1/models URL from the chat completions endpoint.
    /// </summary>
    internal static string BuildModelsUrl(string chatCompletionsEndpoint)
    {
        if (string.IsNullOrWhiteSpace(chatCompletionsEndpoint))
            return "http://localhost/v1/models";

        // Strip /chat/completions or /v1/chat/completions from the end
        string url = chatCompletionsEndpoint.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            url = url.Substring(0, url.Length - "/chat/completions".Length);
        }

        // Ensure we have /v1/models
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return url + "/models";
        }

        return url + "/v1/models";
    }

    private static LocalHealthResult ParseModelsResponse(string responseText, string configuredModel, string provider)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new LocalHealthResult
            {
                Status = LocalHealthStatus.Reachable,
                Message = provider + " server is running (empty model list response)."
            };
        }

        try
        {
            var serializer = ModelResponseParser.CreateSerializer();
            object? payload = serializer.DeserializeObject(responseText);

            if (payload is IDictionary<string, object> root &&
                root.TryGetValue("data", out object? dataValue) &&
                dataValue is object[] models)
            {
                var modelIds = new List<string>();
                foreach (object? item in models)
                {
                    if (item is IDictionary<string, object> modelObj &&
                        modelObj.TryGetValue("id", out object? idValue))
                    {
                        string id = Convert.ToString(idValue) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(id))
                            modelIds.Add(id);
                    }
                }

                if (modelIds.Count == 0)
                {
                    return new LocalHealthResult
                    {
                        Status = LocalHealthStatus.NoModels,
                        Message = provider + " server is running but no models are loaded. Pull or load a model first."
                    };
                }

                bool configuredModelAvailable = modelIds.Exists(id =>
                    string.Equals(id, configuredModel, StringComparison.OrdinalIgnoreCase));

                if (configuredModelAvailable)
                {
                    return new LocalHealthResult
                    {
                        Status = LocalHealthStatus.Ready,
                        Message = provider + " is ready. Model '" + configuredModel + "' is available.",
                        AvailableModels = modelIds
                    };
                }

                return new LocalHealthResult
                {
                    Status = LocalHealthStatus.ModelNotFound,
                    Message = provider + " is running but model '" + configuredModel +
                        "' was not found. Available: " + string.Join(", ", modelIds) + ".",
                    AvailableModels = modelIds
                };
            }
        }
        catch
        {
            // Could not parse — server is reachable but response is unexpected
        }

        return new LocalHealthResult
        {
            Status = LocalHealthStatus.Reachable,
            Message = provider + " server responded but model list could not be parsed."
        };
    }

    private static string ReadResponseBody(WebResponse? response)
    {
        using Stream? responseStream = response?.GetResponseStream();
        if (responseStream == null) return string.Empty;
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

public enum LocalHealthStatus
{
    NotApplicable,
    Ready,
    Reachable,
    NoModels,
    ModelNotFound,
    Unreachable,
    Error
}

public sealed class LocalHealthResult
{
    public LocalHealthStatus Status { get; set; }

    public string Message { get; set; } = string.Empty;

    public List<string> AvailableModels { get; set; } = new();

    public bool IsHealthy => Status == LocalHealthStatus.Ready;
}
