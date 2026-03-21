using System;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;

namespace Adze.Broker.Clients;

public static class AgentModelClientFactory
{
    public static IAgentModelClient? CreateDefault()
    {
        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();
        return Create(settings);
    }

    public static IAgentModelClient? Create(BrokerModelSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!settings.IsUsable())
        {
            return null;
        }

        if (!IsAgentLoopEnabled())
        {
            return null;
        }

        // For local providers, probe whether the model supports tool calling.
        // If the probe determines the model does not support tool calls,
        // return null so the classic synthesis-only path is used instead.
        if (settings.IsLocalProvider)
        {
            var probe = ToolCallCapabilityProbe.GetOrProbe(settings, timeoutMs: 5000);
            if (probe.Capability == ToolCallCapability.NotSupported)
                return null;
        }

        // Both OpenAI-compatible and Anthropic providers go through
        // OpenAIFormatAgentClient when routed via OpenRouter.
        // Direct Anthropic support can be added later if needed.
        return new OpenAIFormatAgentClient(settings);
    }

    public static bool IsAgentLoopEnabled()
    {
        return IsFeatureEnabled("SOLIDWORKS_AI_AGENT_LOOP");
    }

    public static bool IsFirstWaveWritesEnabled()
    {
        return IsFeatureEnabled("SOLIDWORKS_AI_FIRST_WAVE_WRITES");
    }

    private static bool IsFeatureEnabled(string envVarName)
    {
        string? value = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false
        };
    }
}
