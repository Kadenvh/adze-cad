using System.Collections.Generic;

namespace Adze.Broker.Models;

public sealed class ModelUsage
{
    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int TotalTokens { get; set; }

    public static ModelUsage operator +(ModelUsage a, ModelUsage b)
    {
        return new ModelUsage
        {
            PromptTokens = (a?.PromptTokens ?? 0) + (b?.PromptTokens ?? 0),
            CompletionTokens = (a?.CompletionTokens ?? 0) + (b?.CompletionTokens ?? 0),
            TotalTokens = (a?.TotalTokens ?? 0) + (b?.TotalTokens ?? 0)
        };
    }
}

public sealed class BrokerPrompt
{
    public string SystemPrompt { get; set; } = string.Empty;

    public string UserPrompt { get; set; } = string.Empty;

    public List<string> AllowedTools { get; set; } = new();
}

public sealed class AssistantSynthesisPrompt
{
    public string SystemPrompt { get; set; } = string.Empty;

    public string UserPrompt { get; set; } = string.Empty;
}

public sealed class BrokerToolRecommendation
{
    public string ToolName { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int Priority { get; set; }

    public double Score { get; set; }
}

public sealed class BrokerResponse
{
    public string Mode { get; set; } = "grounding";

    public string Source { get; set; } = "deterministic_fallback";

    public string ModelId { get; set; } = string.Empty;

    public string TurnStatus { get; set; } = "ready";

    public string Intent { get; set; } = "general_grounding";

    public double Confidence { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string AssistantMessage { get; set; } = string.Empty;

    public List<string> Blockers { get; set; } = new();

    public List<string> RecoverySuggestions { get; set; } = new();

    public List<string> NextQuestions { get; set; } = new();

    public List<BrokerToolRecommendation> RecommendedTools { get; set; } = new();
}

public sealed class ModelTurnResult
{
    public bool Success { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string RawResponseText { get; set; } = string.Empty;

    public string FailureReason { get; set; } = string.Empty;

    public BrokerResponse? Response { get; set; }

    public string Summary { get; set; } = string.Empty;

    public List<string> RequestedTools { get; set; } = new();

    public ModelUsage Usage { get; set; } = new();
}

public sealed class AssistantSynthesisResult
{
    public bool Success { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string RawResponseText { get; set; } = string.Empty;

    public string FailureReason { get; set; } = string.Empty;

    public string ResponseText { get; set; } = string.Empty;

    public ModelUsage Usage { get; set; } = new();
}
