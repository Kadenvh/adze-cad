using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Broker.Abstractions;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Contracts.Models;

namespace Adze.Broker.Orchestration;

public sealed class HybridBrokerOrchestrator : IBrokerOrchestrator
{
    /// <summary>
    /// Per-run diagnostic string populated during <see cref="CreateGroundingPlan(SessionContext, string, bool)"/>.
    /// Host layer reads this after each call to surface planning-phase model outcomes
    /// (success, skipped, or failed with reason) in host.log. Cleared at the start of
    /// every run. Thread-safety: set via <see cref="System.Threading.Interlocked"/>.
    /// </summary>
    public static string LastPlanningOutcome { get; private set; } = string.Empty;

    internal static void SetLastPlanningOutcome(string outcome)
    {
        System.Threading.Interlocked.Exchange(ref _lastPlanningOutcome, outcome ?? string.Empty);
        LastPlanningOutcome = _lastPlanningOutcome;
    }

    private static string _lastPlanningOutcome = string.Empty;

    private readonly KeywordBrokerOrchestrator _fallbackOrchestrator;
    private readonly IModelClient? _modelClient;

    public HybridBrokerOrchestrator()
        : this(new KeywordBrokerOrchestrator(), CreateDefaultModelClient())
    {
    }

    public HybridBrokerOrchestrator(KeywordBrokerOrchestrator fallbackOrchestrator, IModelClient? modelClient)
    {
        _fallbackOrchestrator = fallbackOrchestrator ?? throw new ArgumentNullException(nameof(fallbackOrchestrator));
        _modelClient = modelClient;
    }

    public BrokerResponse CreateGroundingPlan(SessionContext context, string userRequest)
    {
        return CreateGroundingPlan(context, userRequest, true);
    }

    public BrokerResponse CreateGroundingPlan(SessionContext context, string userRequest, bool isApplicationConnected)
    {
        BrokerResponse fallbackResponse = _fallbackOrchestrator.CreateGroundingPlan(context, userRequest, isApplicationConnected);
        if (!ShouldAttemptModel(fallbackResponse))
        {
            SetLastPlanningOutcome("skipped — turn_status=" + fallbackResponse.TurnStatus);
            return fallbackResponse;
        }

        if (_modelClient == null)
        {
            SetLastPlanningOutcome("skipped — no model client (provider unconfigured or gate off)");
            return fallbackResponse;
        }

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, userRequest);
        ModelTurnResult modelTurn = _modelClient.Complete(prompt);
        if (!modelTurn.Success || modelTurn.Response == null)
        {
            string reason = string.IsNullOrWhiteSpace(modelTurn.FailureReason)
                ? "unknown"
                : modelTurn.FailureReason;
            SetLastPlanningOutcome(
                "failed — provider=" + modelTurn.Provider +
                " model=" + modelTurn.Model +
                " reason=" + reason);
            return fallbackResponse;
        }

        SetLastPlanningOutcome(
            "ok — provider=" + modelTurn.Provider +
            " model=" + modelTurn.Model +
            " model_tools=" + modelTurn.Response.RecommendedTools.Count);
        return MergeResponses(context, fallbackResponse, modelTurn.Response, modelTurn.Provider, modelTurn.Model);
    }

    private static bool ShouldAttemptModel(BrokerResponse fallbackResponse)
    {
        return !string.Equals(fallbackResponse.TurnStatus, "host_unavailable", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(fallbackResponse.TurnStatus, "needs_document", StringComparison.OrdinalIgnoreCase);
    }

    private static BrokerResponse MergeResponses(
        SessionContext context,
        BrokerResponse fallbackResponse,
        BrokerResponse modelResponse,
        string provider,
        string modelId)
    {
        List<string> blockers = MergeLists(fallbackResponse.Blockers, modelResponse.Blockers, 6);
        List<string> recoverySuggestions = MergeLists(modelResponse.RecoverySuggestions, fallbackResponse.RecoverySuggestions, 6);
        List<string> nextQuestions = MergeLists(modelResponse.NextQuestions, fallbackResponse.NextQuestions, 4);
        List<BrokerToolRecommendation> recommendations = SanitizeRecommendations(
            context.Policy.EnabledTools,
            modelResponse.RecommendedTools,
            fallbackResponse.RecommendedTools);

        string mergedTurnStatus = !string.IsNullOrWhiteSpace(modelResponse.TurnStatus)
            ? modelResponse.TurnStatus
            : fallbackResponse.TurnStatus;

        if (blockers.Count > 0 &&
            string.Equals(mergedTurnStatus, "ready", StringComparison.OrdinalIgnoreCase))
        {
            mergedTurnStatus = "attention_needed";
        }

        return new BrokerResponse
        {
            Mode = fallbackResponse.Mode,
            Source = ModelClientFactory.BuildModelSourceLabel(provider),
            ModelId = modelId,
            TurnStatus = mergedTurnStatus,
            Intent = ChooseValue(modelResponse.Intent, fallbackResponse.Intent),
            Confidence = ClampConfidence(modelResponse.Confidence, fallbackResponse.Confidence),
            Summary = ChooseValue(modelResponse.Summary, fallbackResponse.Summary),
            AssistantMessage = ChooseValue(modelResponse.AssistantMessage, fallbackResponse.AssistantMessage),
            Blockers = blockers,
            RecoverySuggestions = recoverySuggestions,
            NextQuestions = nextQuestions,
            RecommendedTools = recommendations
        };
    }

    private static List<BrokerToolRecommendation> SanitizeRecommendations(
        IEnumerable<string> enabledTools,
        IReadOnlyCollection<BrokerToolRecommendation> modelRecommendations,
        IReadOnlyCollection<BrokerToolRecommendation> fallbackRecommendations)
    {
        HashSet<string> allowedTools = new HashSet<string>(enabledTools, StringComparer.OrdinalIgnoreCase);
        List<BrokerToolRecommendation> filtered = modelRecommendations
            .Where(item => !string.IsNullOrWhiteSpace(item.ToolName) && allowedTools.Contains(item.ToolName))
            .OrderBy(item => item.Priority <= 0 ? int.MaxValue : item.Priority)
            .ThenByDescending(item => item.Score)
            .ToList();

        if (filtered.Count == 0)
        {
            return fallbackRecommendations.ToList();
        }

        for (int index = 0; index < filtered.Count; index++)
        {
            filtered[index].Priority = index + 1;
            if (filtered[index].Score <= 0)
            {
                filtered[index].Score = Math.Max(1, 10 - index);
            }
        }

        return filtered.Take(4).ToList();
    }

    private static List<string> MergeLists(IEnumerable<string> preferred, IEnumerable<string> fallback, int limit)
    {
        return preferred
            .Concat(fallback)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static string ChooseValue(string preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();
    }

    private static double ClampConfidence(double preferred, double fallback)
    {
        double value = preferred > 0 ? preferred : fallback;
        if (value < 0)
        {
            return 0;
        }

        if (value > 1)
        {
            return 1;
        }

        return value;
    }

    private static IModelClient? CreateDefaultModelClient()
    {
        return ModelClientFactory.CreateDefault();
    }
}
