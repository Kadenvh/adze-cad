using Adze.Broker.Abstractions;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class ModelClientFactoryTests
{
    [Test]
    public void BuildModelSourceLabel_Anthropic_ReturnsModelAnthropic()
    {
        string label = ModelClientFactory.BuildModelSourceLabel("anthropic");

        Assert.That(label, Is.EqualTo("model_anthropic"));
    }

    [Test]
    public void BuildModelSourceLabel_OpenAi_ReturnsModelOpenai()
    {
        string label = ModelClientFactory.BuildModelSourceLabel("openai");

        Assert.That(label, Is.EqualTo("model_openai"));
    }

    [Test]
    public void BuildModelSourceLabel_Empty_ReturnsModelUnknown()
    {
        string label = ModelClientFactory.BuildModelSourceLabel("");

        Assert.That(label, Is.EqualTo("model_unknown"));
    }

    [Test]
    public void BuildModelSourceLabel_Null_ReturnsModelUnknown()
    {
        string label = ModelClientFactory.BuildModelSourceLabel(null!);

        Assert.That(label, Is.EqualTo("model_unknown"));
    }

    [Test]
    public void BuildModelSourceLabel_MixedCase_NormalizesToLower()
    {
        string label = ModelClientFactory.BuildModelSourceLabel("Anthropic");

        Assert.That(label, Is.EqualTo("model_anthropic"));
    }

    [Test]
    public void Create_UnusableSettings_ReturnsNull()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = false,
            ApiKey = string.Empty,
            Provider = "anthropic"
        };

        IModelClient? client = ModelClientFactory.Create(settings);

        Assert.That(client, Is.Null);
    }

    [Test]
    public void Create_UsableAnthropicSettings_ReturnsNonNull()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "sk-test",
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            Endpoint = "https://api.anthropic.com/v1/messages"
        };

        IModelClient? client = ModelClientFactory.Create(settings);

        Assert.That(client, Is.Not.Null);
    }

    [Test]
    public void Create_UsableOpenAiSettings_ReturnsNonNull()
    {
        var settings = new BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "sk-test",
            Provider = "openai",
            Model = "gpt-4o",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        };

        IModelClient? client = ModelClientFactory.Create(settings);

        Assert.That(client, Is.Not.Null);
    }

    [Test]
    public void Create_NullSettings_ThrowsArgumentNull()
    {
        Assert.That(() => ModelClientFactory.Create(null!), Throws.ArgumentNullException);
    }
}
