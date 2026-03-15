using Adze.Broker.Clients;
using Adze.Broker.Models;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class ModelUsageParsingTests
{
    [Test]
    public void ParseUsage_OpenAiFormat_ExtractsAllFields()
    {
        string json = @"{
            ""id"": ""chatcmpl-123"",
            ""choices"": [],
            ""usage"": {
                ""prompt_tokens"": 100,
                ""completion_tokens"": 50,
                ""total_tokens"": 150
            }
        }";

        ModelUsage usage = ModelResponseParser.ParseUsage(json);

        Assert.That(usage.PromptTokens, Is.EqualTo(100));
        Assert.That(usage.CompletionTokens, Is.EqualTo(50));
        Assert.That(usage.TotalTokens, Is.EqualTo(150));
    }

    [Test]
    public void ParseUsage_AnthropicFormat_ExtractsInputOutput()
    {
        string json = @"{
            ""id"": ""msg_123"",
            ""content"": [],
            ""usage"": {
                ""input_tokens"": 200,
                ""output_tokens"": 75
            }
        }";

        ModelUsage usage = ModelResponseParser.ParseUsage(json);

        Assert.That(usage.PromptTokens, Is.EqualTo(200));
        Assert.That(usage.CompletionTokens, Is.EqualTo(75));
        Assert.That(usage.TotalTokens, Is.EqualTo(275));
    }

    [Test]
    public void ParseUsage_NoUsageField_ReturnsZeroes()
    {
        string json = @"{ ""id"": ""chatcmpl-123"", ""choices"": [] }";

        ModelUsage usage = ModelResponseParser.ParseUsage(json);

        Assert.That(usage.PromptTokens, Is.EqualTo(0));
        Assert.That(usage.CompletionTokens, Is.EqualTo(0));
        Assert.That(usage.TotalTokens, Is.EqualTo(0));
    }

    [Test]
    public void ParseUsage_EmptyString_ReturnsZeroes()
    {
        ModelUsage usage = ModelResponseParser.ParseUsage("");

        Assert.That(usage.TotalTokens, Is.EqualTo(0));
    }

    [Test]
    public void ParseUsage_InvalidJson_ReturnsZeroes()
    {
        ModelUsage usage = ModelResponseParser.ParseUsage("not json at all");

        Assert.That(usage.TotalTokens, Is.EqualTo(0));
    }

    [Test]
    public void ParseUsage_OpenRouterFormat_ExtractsAllFields()
    {
        string json = @"{
            ""id"": ""gen-123"",
            ""choices"": [],
            ""usage"": {
                ""prompt_tokens"": 320,
                ""completion_tokens"": 110,
                ""total_tokens"": 430
            }
        }";

        ModelUsage usage = ModelResponseParser.ParseUsage(json);

        Assert.That(usage.PromptTokens, Is.EqualTo(320));
        Assert.That(usage.CompletionTokens, Is.EqualTo(110));
        Assert.That(usage.TotalTokens, Is.EqualTo(430));
    }

    [Test]
    public void ModelUsage_Addition_CombinesBothSides()
    {
        var a = new ModelUsage { PromptTokens = 100, CompletionTokens = 50, TotalTokens = 150 };
        var b = new ModelUsage { PromptTokens = 200, CompletionTokens = 75, TotalTokens = 275 };

        ModelUsage sum = a + b;

        Assert.That(sum.PromptTokens, Is.EqualTo(300));
        Assert.That(sum.CompletionTokens, Is.EqualTo(125));
        Assert.That(sum.TotalTokens, Is.EqualTo(425));
    }

    [Test]
    public void ModelUsage_Addition_HandlesNull()
    {
        ModelUsage? a = null;
        var b = new ModelUsage { PromptTokens = 100, CompletionTokens = 50, TotalTokens = 150 };

        ModelUsage sum = a! + b;

        Assert.That(sum.PromptTokens, Is.EqualTo(100));
        Assert.That(sum.CompletionTokens, Is.EqualTo(50));
        Assert.That(sum.TotalTokens, Is.EqualTo(150));
    }

    [Test]
    public void ModelUsage_DefaultValues_AreZero()
    {
        var usage = new ModelUsage();

        Assert.That(usage.PromptTokens, Is.EqualTo(0));
        Assert.That(usage.CompletionTokens, Is.EqualTo(0));
        Assert.That(usage.TotalTokens, Is.EqualTo(0));
    }
}
