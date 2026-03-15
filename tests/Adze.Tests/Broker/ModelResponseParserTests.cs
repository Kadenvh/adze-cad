using Adze.Broker.Clients;
using Adze.Broker.Models;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class ModelResponseParserTests
{
    [Test]
    public void TryExtractJsonPayload_FencedJson_ExtractsPayload()
    {
        string input = "Here is the response:\n```json\n{\"turn_status\":\"ready\"}\n```\nDone.";

        bool result = ModelResponseParser.TryExtractJsonPayload(input, out string payload, out string failure);

        Assert.That(result, Is.True);
        Assert.That(payload, Does.Contain("turn_status"));
        Assert.That(failure, Is.Empty);
    }

    [Test]
    public void TryExtractJsonPayload_FencedWithoutLanguageTag_ExtractsPayload()
    {
        string input = "```\n{\"intent\":\"test\"}\n```";

        bool result = ModelResponseParser.TryExtractJsonPayload(input, out string payload, out _);

        Assert.That(result, Is.True);
        Assert.That(payload, Does.Contain("intent"));
    }

    [Test]
    public void TryExtractJsonPayload_RawJsonObject_ExtractsPayload()
    {
        string input = "The answer is {\"turn_status\":\"ready\",\"intent\":\"general\"} and that is it.";

        bool result = ModelResponseParser.TryExtractJsonPayload(input, out string payload, out _);

        Assert.That(result, Is.True);
        Assert.That(payload, Does.StartWith("{"));
        Assert.That(payload, Does.EndWith("}"));
    }

    [Test]
    public void TryExtractJsonPayload_NoJson_ReturnsFalse()
    {
        string input = "This is plain text with no JSON at all.";

        bool result = ModelResponseParser.TryExtractJsonPayload(input, out _, out string failure);

        Assert.That(result, Is.False);
        Assert.That(failure, Is.Not.Empty);
    }

    [Test]
    public void TryExtractJsonPayload_EmptyFence_FallsBackToRawExtraction()
    {
        string input = "```\n\n```\nBut here is {\"turn_status\":\"ready\"}";

        bool result = ModelResponseParser.TryExtractJsonPayload(input, out string payload, out _);

        Assert.That(result, Is.True);
        Assert.That(payload, Does.Contain("turn_status"));
    }

    [Test]
    public void TryParseBrokerResponse_ValidPayload_ParsesAllFields()
    {
        string json = @"{
            ""turn_status"": ""ready"",
            ""intent"": ""dimension_review"",
            ""confidence"": 0.85,
            ""summary"": ""Test summary"",
            ""assistant_message"": ""Test message"",
            ""blockers"": [""blocker1""],
            ""recovery_suggestions"": [""suggestion1""],
            ""next_questions"": [""question1""],
            ""recommended_tools"": [
                {""tool_name"": ""get_dimensions"", ""reason"": ""user asked"", ""priority"": 1, ""score"": 10.0}
            ]
        }";

        bool result = ModelResponseParser.TryParseBrokerResponse(json, out BrokerResponse? response, out _);

        Assert.That(result, Is.True);
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.TurnStatus, Is.EqualTo("ready"));
        Assert.That(response.Intent, Is.EqualTo("dimension_review"));
        Assert.That(response.Confidence, Is.EqualTo(0.85));
        Assert.That(response.Summary, Is.EqualTo("Test summary"));
        Assert.That(response.AssistantMessage, Is.EqualTo("Test message"));
        Assert.That(response.Blockers, Has.Count.EqualTo(1));
        Assert.That(response.RecoverySuggestions, Has.Count.EqualTo(1));
        Assert.That(response.NextQuestions, Has.Count.EqualTo(1));
        Assert.That(response.RecommendedTools, Has.Count.EqualTo(1));
        Assert.That(response.RecommendedTools[0].ToolName, Is.EqualTo("get_dimensions"));
        Assert.That(response.RecommendedTools[0].Priority, Is.EqualTo(1));
        Assert.That(response.RecommendedTools[0].Score, Is.EqualTo(10.0));
    }

    [Test]
    public void TryParseBrokerResponse_MinimalPayload_UsesDefaults()
    {
        string json = "{}";

        bool result = ModelResponseParser.TryParseBrokerResponse(json, out BrokerResponse? response, out _);

        Assert.That(result, Is.True);
        Assert.That(response!.TurnStatus, Is.EqualTo("ready"));
        Assert.That(response.Intent, Is.EqualTo("general_grounding"));
        Assert.That(response.Confidence, Is.EqualTo(0));
        Assert.That(response.Blockers, Is.Empty);
        Assert.That(response.RecommendedTools, Is.Empty);
    }

    [Test]
    public void TryParseBrokerResponse_NonObjectPayload_ReturnsFalse()
    {
        string json = "[1, 2, 3]";

        bool result = ModelResponseParser.TryParseBrokerResponse(json, out _, out string failure);

        Assert.That(result, Is.False);
        Assert.That(failure, Is.Not.Empty);
    }

    [Test]
    public void TryParseBrokerResponse_EmptyToolName_SkipsRecommendation()
    {
        string json = @"{
            ""recommended_tools"": [
                {""tool_name"": """", ""reason"": ""invalid"", ""priority"": 1},
                {""tool_name"": ""get_active_document"", ""reason"": ""valid"", ""priority"": 2}
            ]
        }";

        bool result = ModelResponseParser.TryParseBrokerResponse(json, out BrokerResponse? response, out _);

        Assert.That(result, Is.True);
        Assert.That(response!.RecommendedTools, Has.Count.EqualTo(1));
        Assert.That(response.RecommendedTools[0].ToolName, Is.EqualTo("get_active_document"));
    }

    [Test]
    public void TryParseBrokerResponse_NegativePriority_ClampedToZero()
    {
        string json = @"{
            ""recommended_tools"": [
                {""tool_name"": ""get_dimensions"", ""priority"": -5, ""score"": 3.0}
            ]
        }";

        bool result = ModelResponseParser.TryParseBrokerResponse(json, out BrokerResponse? response, out _);

        Assert.That(result, Is.True);
        Assert.That(response!.RecommendedTools[0].Priority, Is.EqualTo(0));
    }

    [Test]
    public void TryExtractJsonPayload_NestedBraces_ExtractsOuterObject()
    {
        string input = @"Answer: {""tools"":[{""name"":""a""}],""status"":""ok""}";

        bool result = ModelResponseParser.TryExtractJsonPayload(input, out string payload, out _);

        Assert.That(result, Is.True);
        Assert.That(payload, Does.StartWith("{"));
        Assert.That(payload, Does.EndWith("}"));
    }
}
