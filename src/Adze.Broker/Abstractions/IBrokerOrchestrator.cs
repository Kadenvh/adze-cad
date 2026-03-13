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
