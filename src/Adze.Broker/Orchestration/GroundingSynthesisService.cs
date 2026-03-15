using System.Collections.Generic;
using Adze.Broker.Abstractions;
using Adze.Broker.Clients;
using Adze.Broker.Formatting;
using Adze.Broker.Models;

namespace Adze.Broker.Orchestration;

public sealed class GroundingSynthesisOutcome
{
    public string AnswerText { get; set; } = string.Empty;

    public string Source { get; set; } = "deterministic_fallback";

    public string ModelId { get; set; } = string.Empty;

    public string FailureReason { get; set; } = string.Empty;

    public ModelUsage Usage { get; set; } = new();
}

public static class GroundingSynthesisService
{
    public static GroundingSynthesisOutcome Build(
        GroundingExecutionReport report,
        string contextJson,
        string toolResultsJson,
        IModelClient? modelClient)
    {
        string fallbackAnswer = GroundingAnswerBuilder.Build(report);

        if (modelClient == null)
        {
            return CreateFallback(fallbackAnswer);
        }

        AssistantSynthesisPrompt prompt = ContextPromptComposer.ComposeSynthesisPrompt(
            report.Request,
            report.Response,
            contextJson,
            toolResultsJson);

        AssistantSynthesisResult synthesis = modelClient.Synthesize(prompt);
        if (!synthesis.Success || string.IsNullOrWhiteSpace(synthesis.ResponseText))
        {
            return CreateFallback(
                fallbackAnswer,
                string.IsNullOrWhiteSpace(synthesis.FailureReason)
                    ? "Model synthesis returned no usable answer."
                    : synthesis.FailureReason,
                synthesis.Usage);
        }

        return new GroundingSynthesisOutcome
        {
            AnswerText = synthesis.ResponseText.Trim(),
            Source = ModelClientFactory.BuildModelSourceLabel(synthesis.Provider),
            ModelId = synthesis.Model,
            Usage = synthesis.Usage ?? new ModelUsage()
        };
    }

    private static GroundingSynthesisOutcome CreateFallback(string answerText, string failureReason = "", ModelUsage? usage = null)
    {
        return new GroundingSynthesisOutcome
        {
            AnswerText = answerText,
            FailureReason = NormalizeFailureReason(failureReason),
            Usage = usage ?? new ModelUsage()
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
}
