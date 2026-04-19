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
        // Provider preference: env wins; otherwise check the in-app key store
        // (Settings panel writes there). Empty string falls through to the
        // legacy auto-pick logic in ResolveProvider.
        string configuredProvider = NormalizeProvider(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER"));
        if (string.IsNullOrWhiteSpace(configuredProvider))
        {
            configuredProvider = NormalizeProvider(ApiKeyStore.GetConfiguredProvider());
        }

        string anthropicApiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_API_KEY"),
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            ApiKeyStore.GetKey("anthropic"));
        string anthropicModel = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_MODEL"),
            Environment.GetEnvironmentVariable("ANTHROPIC_MODEL"));
        string anthropicEndpoint = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_ENDPOINT"));
        string anthropicVersion = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_VERSION"));

        string openAiApiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            ApiKeyStore.GetKey("openai"));
        string openAiModel = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OPENAI_MODEL"));
        string openAiEndpoint = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OPENAI_ENDPOINT"));

        // Local provider settings
        string ollamaEndpoint = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OLLAMA_ENDPOINT"));
        string ollamaModel = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_OLLAMA_MODEL"));
        string lmStudioEndpoint = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_LMSTUDIO_ENDPOINT"));
        string lmStudioModel = FirstNonEmpty(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_LMSTUDIO_MODEL"));

        // Local providers use longer default timeouts
        int localDefaultTimeoutMs = 60000;
        int localDefaultSynthesisTimeoutMs = 90000;

        string provider = ResolveProvider(configuredProvider, openAiApiKey, anthropicApiKey);
        bool isLocal = IsLocalProviderName(provider);

        string activeApiKey;
        string activeModel;
        string activeEndpoint;
        int defaultTimeoutMs;
        int defaultSynthesisTimeoutMs;

        if (string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            activeApiKey = "ollama"; // Ollama accepts any key
            activeModel = DefaultIfBlank(ollamaModel, "qwen2.5:32b");
            activeEndpoint = EnsureChatCompletionsPath(DefaultIfBlank(ollamaEndpoint, "http://localhost:11434/v1/chat/completions"));
            defaultTimeoutMs = localDefaultTimeoutMs;
            defaultSynthesisTimeoutMs = localDefaultSynthesisTimeoutMs;
        }
        else if (string.Equals(provider, "lmstudio", StringComparison.OrdinalIgnoreCase))
        {
            activeApiKey = "lm-studio"; // LM Studio accepts any key
            activeModel = DefaultIfBlank(lmStudioModel, "qwen2.5-32b");
            activeEndpoint = EnsureChatCompletionsPath(DefaultIfBlank(lmStudioEndpoint, "http://localhost:1234/v1/chat/completions"));
            defaultTimeoutMs = localDefaultTimeoutMs;
            defaultSynthesisTimeoutMs = localDefaultSynthesisTimeoutMs;
        }
        else if (string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            activeApiKey = openAiApiKey;
            activeModel = DefaultIfBlank(openAiModel, "gpt-4o");
            activeEndpoint = EnsureChatCompletionsPath(DefaultIfBlank(openAiEndpoint, "https://api.openai.com/v1/chat/completions"));
            defaultTimeoutMs = 20000;
            defaultSynthesisTimeoutMs = 30000;
        }
        else
        {
            activeApiKey = anthropicApiKey;
            activeModel = DefaultIfBlank(anthropicModel, "claude-sonnet-4-20250514");
            activeEndpoint = DefaultIfBlank(anthropicEndpoint, "https://api.anthropic.com/v1/messages");
            defaultTimeoutMs = 20000;
            defaultSynthesisTimeoutMs = 30000;
        }

        bool defaultEnabled = isLocal || !string.IsNullOrWhiteSpace(activeApiKey);

        return new BrokerModelSettings
        {
            Provider = provider,
            Enabled = ReadBoolean(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ENABLE_MODEL"), defaultEnabled),
            ApiKey = activeApiKey,
            Model = activeModel,
            Endpoint = activeEndpoint,
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
                defaultTimeoutMs),
            SynthesisTimeoutMilliseconds = ReadInteger(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_SYNTHESIS_TIMEOUT_MS"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_TIMEOUT_MS")),
                defaultSynthesisTimeoutMs),
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
               string.Equals(Provider, "anthropic", StringComparison.OrdinalIgnoreCase) ||
               IsLocalProviderName(Provider);
    }

    public bool IsLocalProvider => IsLocalProviderName(Provider);

    public static bool IsLocalProviderName(string provider)
    {
        return string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "lmstudio", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if this provider routes through the OpenAI-compatible client (OpenAI, Ollama, LM Studio).
    /// </summary>
    public bool UsesOpenAIFormat =>
        string.Equals(Provider, "openai", StringComparison.OrdinalIgnoreCase) || IsLocalProvider;

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
            "ollama" => "ollama",
            "lmstudio" or "lm-studio" or "lm_studio" => "lmstudio",
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
