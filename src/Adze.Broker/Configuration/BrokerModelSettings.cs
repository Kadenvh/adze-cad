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
        string apiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_API_KEY"),
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

        string configuredModel = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_MODEL"),
            Environment.GetEnvironmentVariable("ANTHROPIC_MODEL"));

        string configuredEndpoint = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_ENDPOINT"));

        string configuredVersion = FirstNonEmpty(
            Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_VERSION"));

        return new BrokerModelSettings
        {
            Enabled = ReadBoolean(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ENABLE_MODEL"), !string.IsNullOrWhiteSpace(apiKey)),
            ApiKey = apiKey,
            Model = string.IsNullOrWhiteSpace(configuredModel) ? "claude-sonnet-4-20250514" : configuredModel.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(configuredEndpoint) ? "https://api.anthropic.com/v1/messages" : configuredEndpoint.Trim(),
            ApiVersion = string.IsNullOrWhiteSpace(configuredVersion) ? "2023-06-01" : configuredVersion.Trim(),
            MaxTokens = ReadInteger(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_MAX_TOKENS"), 700),
            SynthesisMaxTokens = ReadInteger(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_MAX_TOKENS"), 1100),
            TimeoutMilliseconds = ReadInteger(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_TIMEOUT_MS"), 20000),
            SynthesisTimeoutMilliseconds = ReadInteger(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_TIMEOUT_MS"), 30000),
            Temperature = ReadDouble(Environment.GetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_TEMPERATURE"), 0.1)
        };
    }

    public bool IsUsable()
    {
        return Enabled &&
               !string.IsNullOrWhiteSpace(ApiKey) &&
               !string.IsNullOrWhiteSpace(Model) &&
               !string.IsNullOrWhiteSpace(Endpoint);
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
