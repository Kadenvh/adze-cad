using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Contracts.Models;
using Adze.Trace.Serialization;

namespace Adze.Host.Services;

internal sealed class GroundingSynthesisOutcome
{
    public string AnswerText { get; set; } = string.Empty;

    public string Source { get; set; } = "deterministic_fallback";

    public string ModelId { get; set; } = string.Empty;

    public string FailureReason { get; set; } = string.Empty;
}

internal static class GroundingSynthesisService
{
    public static GroundingSynthesisOutcome Build(SessionContext context, GroundingExecutionReport report)
    {
        string fallbackAnswer = GroundingAnswerBuilder.Build(report);
        BrokerModelSettings settings = BrokerModelSettings.LoadFromEnvironment();
        if (!settings.IsUsable())
        {
            return CreateFallback(fallbackAnswer);
        }

        var modelClient = new AnthropicMessagesModelClient(settings);
        AssistantSynthesisPrompt prompt = ContextPromptComposer.ComposeSynthesisPrompt(
            report.Request,
            report.Response,
            Serialize(ModelJsonMapper.ToJson(context)),
            Serialize(report.ToolResults.Select(ModelJsonMapper.ToJson).ToArray()));

        AssistantSynthesisResult synthesis = modelClient.Synthesize(prompt);
        if (!synthesis.Success || string.IsNullOrWhiteSpace(synthesis.ResponseText))
        {
            return CreateFallback(
                fallbackAnswer,
                string.IsNullOrWhiteSpace(synthesis.FailureReason)
                    ? "Model synthesis returned no usable answer."
                    : synthesis.FailureReason);
        }

        return new GroundingSynthesisOutcome
        {
            AnswerText = synthesis.ResponseText.Trim(),
            Source = "model_anthropic",
            ModelId = synthesis.Model
        };
    }

    private static GroundingSynthesisOutcome CreateFallback(string answerText, string failureReason = "")
    {
        return new GroundingSynthesisOutcome
        {
            AnswerText = answerText,
            FailureReason = NormalizeFailureReason(failureReason)
        };
    }

    private static string NormalizeFailureReason(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return string.Empty;
        }

        string normalized = string.Join(" ", failureReason.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 240
            ? normalized
            : normalized.Substring(0, 240) + "...";
    }

    private static string Serialize(object value)
    {
        return CreateSerializer().Serialize(value);
    }

    private static JavaScriptSerializer CreateSerializer()
    {
        return new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue
        };
    }
}
