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

    [SetUp]
    public void SetUp()
    {
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
}
