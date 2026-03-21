using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class LocalEndpointHealthCheckTests
{
    [Test]
    public void Check_NonLocalProvider_ReturnsNotApplicable()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        };

        LocalHealthResult result = LocalEndpointHealthCheck.Check(settings);

        Assert.That(result.Status, Is.EqualTo(LocalHealthStatus.NotApplicable));
        Assert.That(result.IsHealthy, Is.False);
    }

    [Test]
    public void Check_AnthropicProvider_ReturnsNotApplicable()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "anthropic",
            Endpoint = "https://api.anthropic.com/v1/messages"
        };

        LocalHealthResult result = LocalEndpointHealthCheck.Check(settings);

        Assert.That(result.Status, Is.EqualTo(LocalHealthStatus.NotApplicable));
    }

    [Test]
    public void Check_OllamaUnreachable_ReturnsUnreachable()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "ollama",
            Endpoint = "http://127.0.0.1:59999/v1/chat/completions",
            Model = "qwen2.5:32b"
        };

        // This should fail to connect (nothing listening on port 59999)
        LocalHealthResult result = LocalEndpointHealthCheck.Check(settings, timeoutMs: 2000);

        Assert.That(result.Status, Is.EqualTo(LocalHealthStatus.Unreachable).Or.EqualTo(LocalHealthStatus.Error));
        Assert.That(result.IsHealthy, Is.False);
        Assert.That(result.Message, Is.Not.Empty);
    }

    [Test]
    public void Check_LmStudioUnreachable_ReturnsUnreachable()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "lmstudio",
            Endpoint = "http://127.0.0.1:59998/v1/chat/completions",
            Model = "qwen2.5-32b"
        };

        LocalHealthResult result = LocalEndpointHealthCheck.Check(settings, timeoutMs: 2000);

        Assert.That(result.Status, Is.EqualTo(LocalHealthStatus.Unreachable).Or.EqualTo(LocalHealthStatus.Error));
        Assert.That(result.IsHealthy, Is.False);
    }

    // --- BuildModelsUrl tests ---

    [Test]
    public void BuildModelsUrl_OllamaDefault_StripsAndBuilds()
    {
        string result = LocalEndpointHealthCheck.BuildModelsUrl("http://localhost:11434/v1/chat/completions");

        Assert.That(result, Is.EqualTo("http://localhost:11434/v1/models"));
    }

    [Test]
    public void BuildModelsUrl_LmStudioDefault_StripsAndBuilds()
    {
        string result = LocalEndpointHealthCheck.BuildModelsUrl("http://localhost:1234/v1/chat/completions");

        Assert.That(result, Is.EqualTo("http://localhost:1234/v1/models"));
    }

    [Test]
    public void BuildModelsUrl_CustomEndpoint_BuildsCorrectly()
    {
        string result = LocalEndpointHealthCheck.BuildModelsUrl("http://192.168.1.100:11434/v1/chat/completions");

        Assert.That(result, Is.EqualTo("http://192.168.1.100:11434/v1/models"));
    }

    [Test]
    public void BuildModelsUrl_TrailingSlash_Handled()
    {
        string result = LocalEndpointHealthCheck.BuildModelsUrl("http://localhost:11434/v1/chat/completions/");

        Assert.That(result, Is.EqualTo("http://localhost:11434/v1/models"));
    }

    [Test]
    public void BuildModelsUrl_EmptyString_ReturnsDefault()
    {
        string result = LocalEndpointHealthCheck.BuildModelsUrl("");

        Assert.That(result, Does.EndWith("/v1/models"));
    }

    // --- LocalHealthResult tests ---

    [Test]
    public void LocalHealthResult_ReadyStatus_IsHealthy()
    {
        var result = new LocalHealthResult { Status = LocalHealthStatus.Ready };
        Assert.That(result.IsHealthy, Is.True);
    }

    [Test]
    public void LocalHealthResult_UnreachableStatus_NotHealthy()
    {
        var result = new LocalHealthResult { Status = LocalHealthStatus.Unreachable };
        Assert.That(result.IsHealthy, Is.False);
    }

    [Test]
    public void LocalHealthResult_NoModelsStatus_NotHealthy()
    {
        var result = new LocalHealthResult { Status = LocalHealthStatus.NoModels };
        Assert.That(result.IsHealthy, Is.False);
    }

    [Test]
    public void LocalHealthResult_ModelNotFoundStatus_NotHealthy()
    {
        var result = new LocalHealthResult { Status = LocalHealthStatus.ModelNotFound };
        Assert.That(result.IsHealthy, Is.False);
    }
}
