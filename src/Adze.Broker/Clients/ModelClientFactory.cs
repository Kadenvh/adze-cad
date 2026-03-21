using System;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;

namespace Adze.Broker.Clients;

public static class ModelClientFactory
{
    public static IModelClient? CreateDefault()
    {
        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();
        return Create(settings);
    }

    public static IModelClient? Create(BrokerModelSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!settings.IsUsable())
        {
            return null;
        }

        // OpenAI, Ollama, and LM Studio all use the OpenAI-compatible client
        return settings.UsesOpenAIFormat
            ? new OpenAIModelClient(settings)
            : new AnthropicMessagesModelClient(settings);
    }

    public static string BuildModelSourceLabel(string provider)
    {
        string normalizedProvider = string.IsNullOrWhiteSpace(provider)
            ? "unknown"
            : provider.Trim().ToLowerInvariant();
        return "model_" + normalizedProvider;
    }
}
