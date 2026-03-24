using System;
using System.Net;

namespace Adze.Broker.Orchestration;

/// <summary>
/// Classifies exceptions into presentation tiers for the UI.
/// Tier 1 (ToolError): non-prominent, logged in Tools tab only — agent self-corrects.
/// Tier 2 (ApiError): show retry/rate limit status, provider-specific guidance.
/// Tier 3 (HostError): calm recovery guidance, never stack traces.
/// </summary>
public enum ErrorTier
{
    ToolError,
    ApiError,
    HostError
}

public sealed class ClassifiedError
{
    public ErrorTier Tier { get; set; }

    public string UserMessage { get; set; } = string.Empty;

    public string? Guidance { get; set; }

    public string? TechnicalDetail { get; set; }
}

public static class ErrorClassifier
{
    public static ClassifiedError Classify(Exception ex)
    {
        if (ex == null) throw new ArgumentNullException(nameof(ex));

        // OperationCanceledException — user cancelled
        if (ex is OperationCanceledException)
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.HostError,
                UserMessage = "Run cancelled.",
                Guidance = "You can start a new request any time."
            };
        }

        string message = ex.Message ?? string.Empty;
        string typeName = ex.GetType().Name;

        // Rate limit (429)
        if (ContainsAny(message, "429", "rate limit", "too many requests", "Rate limited"))
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.ApiError,
                UserMessage = "Rate limited by the AI provider.",
                Guidance = "Wait a few seconds and try again. If this persists, check your API plan limits.",
                TechnicalDetail = message
            };
        }

        // Authentication / API key errors (401, 403)
        if (ContainsAny(message, "401", "403", "unauthorized", "forbidden", "invalid api key", "authentication"))
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.ApiError,
                UserMessage = "Authentication failed with the AI provider.",
                Guidance = "Check that your API key is set correctly in the environment variables.",
                TechnicalDetail = message
            };
        }

        // Timeout
        if (ex is TimeoutException || ContainsAny(message, "timed out", "timeout", "request timeout"))
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.ApiError,
                UserMessage = "The AI provider took too long to respond.",
                Guidance = "Try again, or increase the timeout via SOLIDWORKS_AI_TIMEOUT_MS.",
                TechnicalDetail = message
            };
        }

        // Network / connection errors
        if (ex is WebException || ContainsAny(message, "unable to connect", "connection refused", "network", "no such host", "dns", "socket"))
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.ApiError,
                UserMessage = "Could not reach the AI provider.",
                Guidance = "Check your network connection. If using a local model, verify the server is running.",
                TechnicalDetail = message
            };
        }

        // API response errors (500, 502, 503)
        if (ContainsAny(message, "500", "502", "503", "internal server error", "bad gateway", "service unavailable"))
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.ApiError,
                UserMessage = "The AI provider returned a server error.",
                Guidance = "This is usually temporary. Try again in a moment.",
                TechnicalDetail = message
            };
        }

        // COM / SOLIDWORKS errors
        if (ContainsAny(message, "COM", "SOLIDWORKS", "STA", "RPC_E", "interop", "ActiveDoc", "FeatureManager"))
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.HostError,
                UserMessage = "SOLIDWORKS encountered an issue during this operation.",
                Guidance = "Make sure a document is open and SOLIDWORKS is responsive. If the problem persists, try restarting SOLIDWORKS.",
                TechnicalDetail = typeName + ": " + TruncateMessage(message, 200)
            };
        }

        // Invalid operation / argument — likely a host-side logic issue
        if (ex is InvalidOperationException || ex is ArgumentException)
        {
            return new ClassifiedError
            {
                Tier = ErrorTier.HostError,
                UserMessage = "An unexpected error occurred.",
                Guidance = "Try rephrasing your request. If the problem persists, check the Tools Log for details.",
                TechnicalDetail = typeName + ": " + TruncateMessage(message, 200)
            };
        }

        // Default — unknown error, present calmly
        return new ClassifiedError
        {
            Tier = ErrorTier.HostError,
            UserMessage = "Something went wrong.",
            Guidance = "Try again. If this keeps happening, expand the Status section for diagnostic details.",
            TechnicalDetail = typeName + ": " + TruncateMessage(message, 200)
        };
    }

    /// <summary>
    /// Formats a classified error into a user-facing message suitable for the chat panel.
    /// Never includes stack traces.
    /// </summary>
    public static string FormatForUser(ClassifiedError error)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));

        string result = error.UserMessage;
        if (!string.IsNullOrWhiteSpace(error.Guidance))
        {
            result += "\n\n" + error.Guidance;
        }
        return result;
    }

    private static bool ContainsAny(string text, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static string TruncateMessage(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return message.Length <= maxLength ? message : message.Substring(0, maxLength) + "...";
    }
}
