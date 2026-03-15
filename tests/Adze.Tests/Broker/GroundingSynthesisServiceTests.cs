using System.Collections.Generic;
using Adze.Broker.Abstractions;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class GroundingSynthesisServiceTests
{
    [Test]
    public void Build_NullModelClient_ReturnsDeterministicFallback()
    {
        GroundingExecutionReport report = CreateReport();

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", null);

        Assert.That(outcome.Source, Is.EqualTo("deterministic_fallback"));
        Assert.That(outcome.FailureReason, Is.Empty);
    }

    [Test]
    public void Build_NullModelClient_FallbackAnswerContainsRequest()
    {
        GroundingExecutionReport report = CreateReport("describe the part");

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", null);

        Assert.That(outcome.AnswerText, Does.Contain("describe the part"));
    }

    [Test]
    public void Build_ModelSuccess_ReturnsModelAnswer()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            ResponseText = "The part has three features."
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.AnswerText, Is.EqualTo("The part has three features."));
    }

    [Test]
    public void Build_ModelSuccess_SetsCorrectSource_Anthropic()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            ResponseText = "Answer text"
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.Source, Is.EqualTo("model_anthropic"));
    }

    [Test]
    public void Build_ModelSuccess_SetsCorrectSource_OpenAI()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "openai",
            Model = "gpt-4o",
            ResponseText = "Answer text"
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.Source, Is.EqualTo("model_openai"));
    }

    [Test]
    public void Build_ModelSuccess_SetsModelId()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "openai",
            Model = "gpt-4o",
            ResponseText = "Answer text"
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.ModelId, Is.EqualTo("gpt-4o"));
    }

    [Test]
    public void Build_ModelFailure_ReturnsFallbackWithReason()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = false,
            FailureReason = "timeout"
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.Source, Is.EqualTo("deterministic_fallback"));
        Assert.That(outcome.FailureReason, Does.Contain("timeout"));
    }

    [Test]
    public void Build_ModelReturnsEmptyText_ReturnsFallbackWithDefaultReason()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            ResponseText = ""
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.Source, Is.EqualTo("deterministic_fallback"));
        Assert.That(outcome.FailureReason, Does.Contain("no usable answer"));
    }

    [Test]
    public void Build_ModelReturnsWhitespace_ReturnsFallbackWithDefaultReason()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            ResponseText = "   "
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.Source, Is.EqualTo("deterministic_fallback"));
        Assert.That(outcome.FailureReason, Does.Contain("no usable answer"));
    }

    [Test]
    public void Build_FailureReasonNormalized_CollapsesWhitespace()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = false,
            FailureReason = "connection   reset\n\nby   peer"
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.FailureReason, Is.EqualTo("connection reset by peer"));
    }

    [Test]
    public void Build_FailureReasonTruncated_Over240Chars()
    {
        GroundingExecutionReport report = CreateReport();
        string longReason = new string('x', 300);
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = false,
            FailureReason = longReason
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.FailureReason.Length, Is.EqualTo(243));
        Assert.That(outcome.FailureReason, Does.EndWith("..."));
    }

    [Test]
    public void Build_ModelSuccess_TrimsResponseText()
    {
        GroundingExecutionReport report = CreateReport();
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            ResponseText = "  trimmed answer  "
        });

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(report, "{}", "[]", client);

        Assert.That(outcome.AnswerText, Is.EqualTo("trimmed answer"));
    }

    [Test]
    public void Build_CallsSynthesizeWithCorrectPrompt()
    {
        GroundingExecutionReport report = CreateReport("explain the mates");
        var client = new SynthesisStubModelClient(new AssistantSynthesisResult
        {
            Success = true,
            Provider = "openai",
            Model = "gpt-4o",
            ResponseText = "Here are the mates."
        });

        GroundingSynthesisService.Build(report, "{\"session\":true}", "[{\"tool\":\"result\"}]", client);

        Assert.That(client.SynthesizeCalled, Is.True);
        Assert.That(client.LastPrompt, Is.Not.Null);
        Assert.That(client.LastPrompt!.UserPrompt, Does.Contain("explain the mates"));
        Assert.That(client.LastPrompt.UserPrompt, Does.Contain("{\"session\":true}"));
        Assert.That(client.LastPrompt.UserPrompt, Does.Contain("[{\"tool\":\"result\"}]"));
    }

    private static GroundingExecutionReport CreateReport(string request = "test request", string turnStatus = "ready")
    {
        return new GroundingExecutionReport
        {
            Request = request,
            Response = new BrokerResponse
            {
                TurnStatus = turnStatus,
                RecoverySuggestions = new List<string>(),
                NextQuestions = new List<string>()
            },
            ToolResults = new List<ToolResult>
            {
                new ToolResult { ToolName = "get_active_document", Success = true, Summary = "Active part found" }
            },
            SuccessfulToolCount = 1
        };
    }

    private sealed class SynthesisStubModelClient : IModelClient
    {
        private readonly AssistantSynthesisResult _synthesisResult;

        public SynthesisStubModelClient(AssistantSynthesisResult result)
        {
            _synthesisResult = result;
        }

        public bool SynthesizeCalled { get; private set; }
        public AssistantSynthesisPrompt? LastPrompt { get; private set; }

        public ModelTurnResult Complete(BrokerPrompt prompt)
        {
            return new ModelTurnResult { Success = false, FailureReason = "stub" };
        }

        public AssistantSynthesisResult Synthesize(AssistantSynthesisPrompt prompt)
        {
            SynthesizeCalled = true;
            LastPrompt = prompt;
            return _synthesisResult;
        }
    }
}
