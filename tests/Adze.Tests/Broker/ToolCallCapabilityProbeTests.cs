using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class ToolCallCapabilityProbeTests
{
    [SetUp]
    public void SetUp()
    {
        ToolCallCapabilityProbe.ClearCache();
    }

    // --- ParseProbeResponse tests ---

    [Test]
    public void ParseProbeResponse_WithToolCalls_ReturnsSupported()
    {
        string json = @"{
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": null,
                    ""tool_calls"": [{
                        ""id"": ""call_123"",
                        ""type"": ""function"",
                        ""function"": {
                            ""name"": ""get_current_time"",
                            ""arguments"": ""{}""
                        }
                    }]
                },
                ""finish_reason"": ""tool_calls""
            }]
        }";

        ToolCallProbeResult result = ToolCallCapabilityProbe.ParseProbeResponse(json, "ollama");

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Supported));
        Assert.That(result.Message, Does.Contain("supports tool calling"));
        Assert.That(result.Message, Does.Contain("get_current_time"));
    }

    [Test]
    public void ParseProbeResponse_WithTextResponse_ReturnsNotSupported()
    {
        string json = @"{
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""I don't have access to a clock.""
                },
                ""finish_reason"": ""stop""
            }]
        }";

        ToolCallProbeResult result = ToolCallCapabilityProbe.ParseProbeResponse(json, "ollama");

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.NotSupported));
        Assert.That(result.Message, Does.Contain("text instead of tool calls"));
        Assert.That(result.Message, Does.Contain("finish_reason=stop"));
    }

    [Test]
    public void ParseProbeResponse_EmptyString_ReturnsUnknown()
    {
        ToolCallProbeResult result = ToolCallCapabilityProbe.ParseProbeResponse("", "ollama");

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Unknown));
        Assert.That(result.Message, Does.Contain("Empty response"));
    }

    [Test]
    public void ParseProbeResponse_MalformedJson_ReturnsUnknown()
    {
        ToolCallProbeResult result = ToolCallCapabilityProbe.ParseProbeResponse("{invalid json", "lmstudio");

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Unknown));
        Assert.That(result.Message, Does.Contain("Could not parse"));
    }

    [Test]
    public void ParseProbeResponse_EmptyChoices_ReturnsUnknown()
    {
        string json = @"{ ""choices"": [] }";

        ToolCallProbeResult result = ToolCallCapabilityProbe.ParseProbeResponse(json, "ollama");

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Unknown));
    }

    [Test]
    public void ParseProbeResponse_MultipleToolCalls_ReturnsSupported()
    {
        string json = @"{
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": null,
                    ""tool_calls"": [
                        {
                            ""id"": ""call_1"",
                            ""type"": ""function"",
                            ""function"": { ""name"": ""get_current_time"", ""arguments"": ""{}"" }
                        },
                        {
                            ""id"": ""call_2"",
                            ""type"": ""function"",
                            ""function"": { ""name"": ""other_tool"", ""arguments"": ""{}"" }
                        }
                    ]
                },
                ""finish_reason"": ""tool_calls""
            }]
        }";

        ToolCallProbeResult result = ToolCallCapabilityProbe.ParseProbeResponse(json, "ollama");

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Supported));
    }

    [Test]
    public void ParseProbeResponse_ToolCallMissingFunctionName_ReturnsNotSupported()
    {
        // Malformed tool call: has tool_calls array but no function.name
        string json = @"{
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""tool_calls"": [{
                        ""id"": ""call_1"",
                        ""type"": ""function"",
                        ""function"": { ""arguments"": ""{}"" }
                    }]
                },
                ""finish_reason"": ""tool_calls""
            }]
        }";

        ToolCallProbeResult result = ToolCallCapabilityProbe.ParseProbeResponse(json, "ollama");

        // The probe sees tool_calls but the structure is invalid — falls through to text check
        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.NotSupported));
    }

    // --- BuildProbeRequest tests ---

    [Test]
    public void BuildProbeRequest_ContainsExpectedFields()
    {
        var request = ToolCallCapabilityProbe.BuildProbeRequest("qwen2.5:32b");

        Assert.That(request["model"], Is.EqualTo("qwen2.5:32b"));
        Assert.That(request["messages"], Is.TypeOf<object[]>());
        Assert.That(request["tools"], Is.TypeOf<object[]>());
        Assert.That(request["max_tokens"], Is.EqualTo(100));
    }

    // --- GetOrProbe tests ---

    [Test]
    public void GetOrProbe_CloudProvider_ReturnsSupportedWithoutProbing()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        };

        ToolCallProbeResult result = ToolCallCapabilityProbe.GetOrProbe(settings);

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Supported));
        Assert.That(result.Message, Does.Contain("Cloud providers"));
    }

    [Test]
    public void GetOrProbe_AnthropicProvider_ReturnsSupportedWithoutProbing()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "anthropic",
            Endpoint = "https://api.anthropic.com/v1/messages"
        };

        ToolCallProbeResult result = ToolCallCapabilityProbe.GetOrProbe(settings);

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Supported));
    }

    [Test]
    public void GetOrProbe_LocalProviderUnreachable_ReturnsUnknown()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "ollama",
            Endpoint = "http://127.0.0.1:59997/v1/chat/completions",
            Model = "test-model",
            ApiKey = "ollama"
        };

        ToolCallProbeResult result = ToolCallCapabilityProbe.GetOrProbe(settings, timeoutMs: 2000);

        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Unknown));
        Assert.That(result.Message, Does.Contain("Probe failed"));
    }

    [Test]
    public void GetOrProbe_CachesResultForSameProviderModel()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        };

        ToolCallProbeResult first = ToolCallCapabilityProbe.GetOrProbe(settings);
        ToolCallProbeResult second = ToolCallCapabilityProbe.GetOrProbe(settings);

        Assert.That(first.Capability, Is.EqualTo(second.Capability));
    }

    [Test]
    public void ClearCache_AllowsReprobe()
    {
        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        };

        ToolCallCapabilityProbe.GetOrProbe(settings);
        ToolCallCapabilityProbe.ClearCache();

        // After clearing, a new probe call should work without error
        ToolCallProbeResult result = ToolCallCapabilityProbe.GetOrProbe(settings);
        Assert.That(result.Capability, Is.EqualTo(ToolCallCapability.Supported));
    }
}
