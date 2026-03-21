using System;
using Adze.Broker.Models;
using Adze.Contracts.Models;

namespace Adze.Broker.Abstractions;

public interface IBrokerOrchestrator
{
    BrokerResponse CreateGroundingPlan(SessionContext context, string userRequest);
}

public interface IModelClient
{
    ModelTurnResult Complete(BrokerPrompt prompt);

    AssistantSynthesisResult Synthesize(AssistantSynthesisPrompt prompt);
}

/// <summary>
/// Optional extension for model clients that support SSE streaming.
/// The synthesis pass streams text chunks via the onTextChunk callback
/// as they arrive, improving perceived responsiveness.
/// </summary>
public interface IStreamingModelClient : IModelClient
{
    AssistantSynthesisResult SynthesizeStreaming(
        AssistantSynthesisPrompt prompt,
        Action<string> onTextChunk);
}
