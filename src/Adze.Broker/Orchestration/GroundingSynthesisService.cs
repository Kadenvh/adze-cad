using System;
using System.Collections.Generic;
using Adze.Broker.Abstractions;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
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
        return Build(report, contextJson, toolResultsJson, modelClient, null);
    }

    /// <summary>
    /// Builds a grounded synthesis answer. When the model client supports streaming
    /// and onTextChunk is provided, text is streamed via the callback as it arrives.
    /// </summary>
    public static GroundingSynthesisOutcome Build(
        GroundingExecutionReport report,
        string contextJson,
        string toolResultsJson,
        IModelClient? modelClient,
        Action<string>? onTextChunk)
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

        // Use streaming when the client supports it and a callback is provided
        if (onTextChunk != null && modelClient is IStreamingModelClient streamingClient)
        {
            AssistantSynthesisResult synthesis = streamingClient.SynthesizeStreaming(prompt, onTextChunk);
            return BuildOutcomeFromSynthesis(synthesis, fallbackAnswer);
        }

        AssistantSynthesisResult nonStreamingSynthesis = modelClient.Synthesize(prompt);
        return BuildOutcomeFromSynthesis(nonStreamingSynthesis, fallbackAnswer);
    }

    private static GroundingSynthesisOutcome BuildOutcomeFromSynthesis(
        AssistantSynthesisResult synthesis,
        string fallbackAnswer)
    {
        if (!synthesis.Success || string.IsNullOrWhiteSpace(synthesis.ResponseText))
        {
            string failureReason = string.IsNullOrWhiteSpace(synthesis.FailureReason)
                ? "Model synthesis returned no usable answer."
                : synthesis.FailureReason;

            // Provide clearer messaging for local model failures
            if (BrokerModelSettings.IsLocalProviderName(synthesis.Provider))
            {
                failureReason = "Local model (" + synthesis.Provider + ") response was unusable — used deterministic planner. " + failureReason;
            }

            return CreateFallback(fallbackAnswer, failureReason, synthesis.Usage);
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
