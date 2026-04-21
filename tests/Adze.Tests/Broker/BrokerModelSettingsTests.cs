using System;
using Adze.Broker.Configuration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class BrokerModelSettingsTests
{
    private static readonly string[] EnvVarNames =
    {
        "SOLIDWORKS_AI_PROVIDER",
        "SOLIDWORKS_AI_ENABLE_MODEL",
        "SOLIDWORKS_AI_ANTHROPIC_API_KEY",
        "ANTHROPIC_API_KEY",
        "SOLIDWORKS_AI_ANTHROPIC_MODEL",
        "ANTHROPIC_MODEL",
        "SOLIDWORKS_AI_ANTHROPIC_ENDPOINT",
        "SOLIDWORKS_AI_ANTHROPIC_VERSION",
        "SOLIDWORKS_AI_OPENAI_API_KEY",
        "OPENAI_API_KEY",
        "SOLIDWORKS_AI_OPENAI_MODEL",
        "SOLIDWORKS_AI_OPENAI_ENDPOINT",
        "SOLIDWORKS_AI_OPENROUTER_API_KEY",
        "OPENROUTER_API_KEY",
        "SOLIDWORKS_AI_OPENROUTER_MODEL",
        "SOLIDWORKS_AI_OPENROUTER_ENDPOINT",
        "SOLIDWORKS_AI_OLLAMA_ENDPOINT",
        "SOLIDWORKS_AI_OLLAMA_MODEL",
        "SOLIDWORKS_AI_LMSTUDIO_ENDPOINT",
        "SOLIDWORKS_AI_LMSTUDIO_MODEL",
        "SOLIDWORKS_AI_MAX_TOKENS",
        "SOLIDWORKS_AI_ANTHROPIC_MAX_TOKENS",
        "SOLIDWORKS_AI_SYNTHESIS_MAX_TOKENS",
        "SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_MAX_TOKENS",
        "SOLIDWORKS_AI_TIMEOUT_MS",
        "SOLIDWORKS_AI_ANTHROPIC_TIMEOUT_MS",
        "SOLIDWORKS_AI_SYNTHESIS_TIMEOUT_MS",
        "SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_TIMEOUT_MS",
        "SOLIDWORKS_AI_TEMPERATURE",
        "SOLIDWORKS_AI_ANTHROPIC_TEMPERATURE"
    };

    // Preserve the user's ApiKeyStore across test runs — tests clear it for
    // deterministic behavior, then restore it in TearDown so we don't nuke
    // the key that the Settings panel wrote.
    private (string? Provider, string? Key) _savedKeyStore;

    [SetUp]
    public void SetUp()
    {
        _savedKeyStore = ApiKeyStore.Load();
        ApiKeyStore.Clear();
        foreach (string name in EnvVarNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string name in EnvVarNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        if (!string.IsNullOrWhiteSpace(_savedKeyStore.Provider) && _savedKeyStore.Key != null)
        {
            ApiKeyStore.Save(_savedKeyStore.Provider!, _savedKeyStore.Key);
        }
    }

    [Test]
    public void LoadFromEnvironment_NoVars_ReturnsDefaults()
    {
        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Provider, Is.EqualTo("anthropic"));
        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.ApiKey, Is.Empty);
        Assert.That(settings.Model, Is.EqualTo("claude-sonnet-4-20250514"));
        Assert.That(settings.Endpoint, Is.EqualTo("https://api.anthropic.com/v1/messages"));
        Assert.That(settings.MaxTokens, Is.EqualTo(700));
        Assert.That(settings.SynthesisMaxTokens, Is.EqualTo(1100));
        Assert.That(settings.TimeoutMilliseconds, Is.EqualTo(20000));
        Assert.That(settings.SynthesisTimeoutMilliseconds, Is.EqualTo(30000));
        Assert.That(settings.Temperature, Is.EqualTo(0.1));
    }

    [Test]
    public void LoadFromEnvironment_AnthropicKeyOnly_SelectsAnthropic()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Provider, Is.EqualTo("anthropic"));
        Assert.That(settings.ApiKey, Is.EqualTo("sk-ant-test"));
        Assert.That(settings.Enabled, Is.True);
    }

    [Test]
    public void LoadFromEnvironment_OpenAiKeyOnly_SelectsOpenAi()
    {
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-openai-test");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Provider, Is.EqualTo("openai"));
        Assert.That(settings.ApiKey, Is.EqualTo("sk-openai-test"));
        Assert.That(settings.Model, Is.EqualTo("gpt-4o"));
        Assert.That(settings.Endpoint, Is.EqualTo("https://api.openai.com/v1/chat/completions"));
    }

    [Test]
    public void LoadFromEnvironment_BothKeys_DefaultsToAnthropic()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-openai-test");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Provider, Is.EqualTo("anthropic"));
        Assert.That(settings.ApiKey, Is.EqualTo("sk-ant-test"));
    }

    [Test]
    public void LoadFromEnvironment_ExplicitProvider_OverridesAutoDetection()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "openai");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-test");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-openai-test");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Provider, Is.EqualTo("openai"));
        Assert.That(settings.ApiKey, Is.EqualTo("sk-openai-test"));
    }

    [Test]
    public void LoadFromEnvironment_PrefixedKeyTakesPrecedence()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "generic-key");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_API_KEY", "prefixed-key");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.ApiKey, Is.EqualTo("prefixed-key"));
    }

    [Test]
    public void LoadFromEnvironment_CustomMaxTokens_AppliesOverride()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS", "1500");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.MaxTokens, Is.EqualTo(1500));
    }

    [Test]
    public void LoadFromEnvironment_BackwardCompatibleTokens_Applied()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_MAX_TOKENS", "999");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.MaxTokens, Is.EqualTo(999));
    }

    [Test]
    public void LoadFromEnvironment_NewTokensOverrideBackwardCompat()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS", "2000");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_ANTHROPIC_MAX_TOKENS", "999");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.MaxTokens, Is.EqualTo(2000));
    }

    [Test]
    public void LoadFromEnvironment_EnableModelExplicitlyFalse_DisablesEvenWithKey()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_ENABLE_MODEL", "false");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Enabled, Is.False);
    }

    [Test]
    public void LoadFromEnvironment_EnableModelTrue_EnabledExplicitly()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_ENABLE_MODEL", "true");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Enabled, Is.True);
    }

    [Test]
    public void LoadFromEnvironment_CustomTemperature_Applied()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_TEMPERATURE", "0.7");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Temperature, Is.EqualTo(0.7));
    }

    [Test]
    public void IsUsable_Enabled_WithApiKey_ReturnsTrue()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "sk-test",
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            Endpoint = "https://api.anthropic.com/v1/messages"
        };

        Assert.That(settings.IsUsable(), Is.True);
    }

    [Test]
    public void IsUsable_Disabled_ReturnsFalse()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = false,
            ApiKey = "sk-test",
            Provider = "anthropic"
        };

        Assert.That(settings.IsUsable(), Is.False);
    }

    [Test]
    public void IsUsable_NoApiKey_ReturnsFalse()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = string.Empty,
            Provider = "anthropic"
        };

        Assert.That(settings.IsUsable(), Is.False);
    }

    [Test]
    public void IsUsable_UnknownProvider_ReturnsFalse()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "sk-test",
            Provider = "unknown_provider",
            Model = "test",
            Endpoint = "https://example.com"
        };

        Assert.That(settings.IsUsable(), Is.False);
    }

    [Test]
    public void IsUsable_OpenAiProvider_ReturnsTrue()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "sk-test",
            Provider = "openai",
            Model = "gpt-4o",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        };

        Assert.That(settings.IsUsable(), Is.True);
    }

    // --- Local provider tests ---

    [Test]
    public void LoadFromEnvironment_OllamaProvider_SetsDefaults()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "ollama");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Provider, Is.EqualTo("ollama"));
        Assert.That(settings.ApiKey, Is.EqualTo("ollama"));
        Assert.That(settings.Model, Is.EqualTo("qwen2.5:32b"));
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:11434/v1/chat/completions"));
        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.IsLocalProvider, Is.True);
        Assert.That(settings.UsesOpenAIFormat, Is.True);
    }

    [Test]
    public void LoadFromEnvironment_LmStudioProvider_SetsDefaults()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "lmstudio");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Provider, Is.EqualTo("lmstudio"));
        Assert.That(settings.ApiKey, Is.EqualTo("lm-studio"));
        Assert.That(settings.Model, Is.EqualTo("qwen2.5-32b"));
        Assert.That(settings.Endpoint, Is.EqualTo("http://localhost:1234/v1/chat/completions"));
        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.IsLocalProvider, Is.True);
    }

    [Test]
    public void LoadFromEnvironment_LmStudioVariantNames_Normalize()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "lm-studio");
        BrokerModelSettings settings1 = BrokerModelSettings.LoadFromEnvironment();
        Assert.That(settings1.Provider, Is.EqualTo("lmstudio"));

        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "lm_studio");
        BrokerModelSettings settings2 = BrokerModelSettings.LoadFromEnvironment();
        Assert.That(settings2.Provider, Is.EqualTo("lmstudio"));
    }

    [Test]
    public void LoadFromEnvironment_OllamaCustomModel_Applied()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "ollama");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_OLLAMA_MODEL", "llama3.3:70b");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Model, Is.EqualTo("llama3.3:70b"));
    }

    [Test]
    public void LoadFromEnvironment_OllamaCustomEndpoint_Applied()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "ollama");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_OLLAMA_ENDPOINT", "http://192.168.1.100:11434/v1/chat/completions");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.Endpoint, Is.EqualTo("http://192.168.1.100:11434/v1/chat/completions"));
    }

    [Test]
    public void LoadFromEnvironment_LocalProvider_LongerDefaultTimeouts()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", "ollama");

        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();

        Assert.That(settings.TimeoutMilliseconds, Is.EqualTo(60000));
        Assert.That(settings.SynthesisTimeoutMilliseconds, Is.EqualTo(90000));
    }

    [Test]
    public void IsUsable_OllamaProvider_ReturnsTrue()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "ollama",
            Provider = "ollama",
            Model = "qwen2.5:32b",
            Endpoint = "http://localhost:11434/v1/chat/completions"
        };

        Assert.That(settings.IsUsable(), Is.True);
    }

    [Test]
    public void IsUsable_LmStudioProvider_ReturnsTrue()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "lm-studio",
            Provider = "lmstudio",
            Model = "qwen2.5-32b",
            Endpoint = "http://localhost:1234/v1/chat/completions"
        };

        Assert.That(settings.IsUsable(), Is.True);
    }

    [Test]
    public void IsLocalProviderName_CorrectForAllProviders()
    {
        Assert.That(BrokerModelSettings.IsLocalProviderName("ollama"), Is.True);
        Assert.That(BrokerModelSettings.IsLocalProviderName("lmstudio"), Is.True);
        Assert.That(BrokerModelSettings.IsLocalProviderName("openai"), Is.False);
        Assert.That(BrokerModelSettings.IsLocalProviderName("anthropic"), Is.False);
    }

    [Test]
    public void UsesOpenAIFormat_CorrectForAllProviders()
    {
        Assert.That(new BrokerModelSettings { Provider = "openai" }.UsesOpenAIFormat, Is.True);
        Assert.That(new BrokerModelSettings { Provider = "ollama" }.UsesOpenAIFormat, Is.True);
        Assert.That(new BrokerModelSettings { Provider = "lmstudio" }.UsesOpenAIFormat, Is.True);
        Assert.That(new BrokerModelSettings { Provider = "anthropic" }.UsesOpenAIFormat, Is.False);
    }
}
