using System;

namespace Adze.Broker.Configuration;

public sealed class BrokerModelSettings
{
    public string Provider { get; set; } = "anthropic";

    public bool Enabled { get; set; }

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-sonnet-4-20250514";

    public string Endpoint { get; set; } = "https://api.anthropic.com/v1/messages";

    public string ApiVersion { get; set; } = "2023-06-01";

    public int MaxTokens { get; set; } = 700;

    public int SynthesisMaxTokens { get; set; } = 1100;

    public int TimeoutMilliseconds { get; set; } = 20000;

    public int SynthesisTimeoutMilliseconds { get; set; } = 30000;

    public double Temperature { get; set; } = 0.1;

    public static BrokerModelSettings LoadFromEnvironment()
    {
        string configuredProvider = NormalizeProvider(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER"));

        string anthropicApiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_API_KEY"),
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        string anthropicModel = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_MODEL"),
            Environment.GetEnvironmentVariable("ANTHROPIC_MODEL"));
        string anthropicEndpoint = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_ENDPOINT"));
        string anthropicVersion = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_VERSION"));

        string openAiApiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        string openAiModel = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OPENAI_MODEL"));
        string openAiEndpoint = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OPENAI_ENDPOINT"));

        string provider = ResolveProvider(configuredProvider, openAiApiKey, anthropicApiKey);
        string activeApiKey = string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase)
            ? openAiApiKey
            : anthropicApiKey;
        bool defaultEnabled = !string.IsNullOrWhiteSpace(activeApiKey);

        return new BrokerModelSettings
        {
            Provider = provider,
            Enabled = ReadBoolean(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ENABLE_MODEL"), defaultEnabled),
            ApiKey = activeApiKey,
            Model = string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase)
                ? DefaultIfBlank(openAiModel, "gpt-4o")
                : DefaultIfBlank(anthropicModel, "claude-sonnet-4-20250514"),
            Endpoint = string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase)
                ? EnsureChatCompletionsPath(DefaultIfBlank(openAiEndpoint, "https://api.openai.com/v1/chat/completions"))
                : DefaultIfBlank(anthropicEndpoint, "https://api.anthropic.com/v1/messages"),
            ApiVersion = DefaultIfBlank(anthropicVersion, "2023-06-01"),
            MaxTokens = ReadInteger(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_MAX_TOKENS")),
                700),
            SynthesisMaxTokens = ReadInteger(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_SYNTHESIS_MAX_TOKENS"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_MAX_TOKENS")),
                1100),
            TimeoutMilliseconds = ReadInteger(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_TIMEOUT_MS"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_TIMEOUT_MS")),
                20000),
            SynthesisTimeoutMilliseconds = ReadInteger(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_SYNTHESIS_TIMEOUT_MS"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_TIMEOUT_MS")),
                30000),
            Temperature = ReadDouble(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_TEMPERATURE"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_TEMPERATURE")),
                0.1)
        };
    }

    public bool IsUsable()
    {
        if (!Enabled ||
            string.IsNullOrWhiteSpace(ApiKey) ||
            string.IsNullOrWhiteSpace(Model) ||
            string.IsNullOrWhiteSpace(Endpoint))
        {
            return false;
        }

        return string.Equals(Provider, "openai", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Provider, "anthropic", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProvider(string configuredProvider, string openAiApiKey, string anthropicApiKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredProvider))
        {
            return configuredProvider;
        }

        if (!string.IsNullOrWhiteSpace(openAiApiKey) && string.IsNullOrWhiteSpace(anthropicApiKey))
        {
            return "openai";
        }

        if (!string.IsNullOrWhiteSpace(anthropicApiKey))
        {
            return "anthropic";
        }

        if (!string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return "openai";
        }

        return "anthropic";
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return string.Empty;
        }

        string normalizedProvider = provider!.Trim().ToLowerInvariant();
        return normalizedProvider switch
        {
            "anthropic" => "anthropic",
            "openai" => "openai",
            _ => normalizedProvider
        };
    }

    private static string EnsureChatCompletionsPath(string endpoint)
    {
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return endpoint;
        return endpoint.TrimEnd('/') + "/chat/completions";
    }

    private static string DefaultIfBlank(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (value is string currentValue && !string.IsNullOrWhiteSpace(currentValue))
            {
                string trimmedValue = currentValue.Trim();
                if (trimmedValue.Length > 0)
                {
                    return trimmedValue;
                }
            }
        }

        return string.Empty;
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        if (value is not string normalizedSource || string.IsNullOrWhiteSpace(normalizedSource))
        {
            return fallback;
        }

        string normalizedValue = normalizedSource.Trim().ToLowerInvariant();
        switch (normalizedValue)
        {
            case "1":
            case "true":
            case "yes":
            case "on":
                return true;
            case "0":
            case "false":
            case "no":
            case "off":
                return false;
            default:
                return fallback;
        }
    }

    private static int ReadInteger(string? value, int fallback)
    {
        return int.TryParse(value, out int parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static double ReadDouble(string? value, double fallback)
    {
        return double.TryParse(value, out double parsed) && parsed >= 0
            ? parsed
            : fallback;
    }
}
