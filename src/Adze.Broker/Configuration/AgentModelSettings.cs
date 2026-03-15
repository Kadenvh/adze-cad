using System;

namespace Adze.Broker.Configuration;

public sealed class AgentModelSettings
{
    public int MaxTokens { get; set; } = 4096;

    public int TimeoutMilliseconds { get; set; } = 30000;

    public double Temperature { get; set; } = 0.1;

    public int MaxIterations { get; set; } = 10;

    public int MaxConsecutiveErrors { get; set; } = 2;

    public int MaxTotalTokens { get; set; } = 100000;

    public int MaxToolResultChars { get; set; } = 8192;

    public bool DisableParallelToolCalls { get; set; } = true;

    public static AgentModelSettings LoadFromEnvironment()
    {
        return new AgentModelSettings
        {
            MaxTokens = ReadInteger(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS")),
                4096),
            TimeoutMilliseconds = ReadInteger(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_TIMEOUT_MS"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_TIMEOUT_MS")),
                30000),
            Temperature = ReadDouble(
                FirstNonEmpty(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_TEMPERATURE"),
                    Environment.GetEnvironmentVariable("SOLIDWORKS_AI_TEMPERATURE")),
                0.1),
            MaxIterations = ReadInteger(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_ITERATIONS"),
                10),
            MaxConsecutiveErrors = ReadInteger(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_CONSECUTIVE_ERRORS"),
                2),
            MaxTotalTokens = ReadInteger(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOTAL_TOKENS"),
                100000),
            MaxToolResultChars = ReadInteger(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOOL_RESULT_CHARS"),
                8192),
            DisableParallelToolCalls = ReadBoolean(
                Environment.GetEnvironmentVariable("SOLIDWORKS_AI_AGENT_DISABLE_PARALLEL_TOOL_CALLS"),
                true)
        };
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
}
