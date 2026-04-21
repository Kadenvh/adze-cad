using System.Collections.Generic;
using Adze.Broker.Infrastructure;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Broker.Orchestration;

public sealed class AgentPolicyEngine : IAgentPolicyEngine
{
    private readonly ITrustService _trustService;

    private static readonly HashSet<string> ReadTools = new HashSet<string>
    {
        ToolNames.GetActiveDocument,
        ToolNames.GetDocumentSummary,
        ToolNames.GetSelectionContext,
        ToolNames.GetFeatureTreeSlice,
        ToolNames.GetDimensions,
        ToolNames.GetConfigurations,
        ToolNames.GetCustomProperties,
        ToolNames.GetMates,
        ToolNames.GetRebuildDiagnostics,
        ToolNames.GetReferenceGraph,
        ToolNames.SearchProjectFiles
    };

    private static readonly HashSet<string> FirstWaveWriteTools = new HashSet<string>
    {
        ToolNames.SetCustomProperty,
        ToolNames.SetDimensionValue,
        ToolNames.SuppressFeature,
        ToolNames.UnsuppressFeature,
        ToolNames.RenameObject
    };

    private static readonly HashSet<string> AdvancedWriteTools = new HashSet<string>
    {
        ToolNames.InsertComponent,
        ToolNames.CreateDrawingView
    };

    public AgentPolicyEngine(ITrustService trustService)
    {
        _trustService = trustService;
    }

    public PolicyEvaluation Evaluate(string toolName, SessionContext context, string userId)
    {
        ToolUnlockTier currentTier = _trustService.GetCurrentTier(userId);
        PolicyEvaluation evaluation = EvaluateCore(toolName, context, currentTier);

        // Log every Deny outcome so "why didn't my write go through" is diagnosable from host.log.
        if (evaluation.Policy == ToolExecutionPolicy.Deny)
        {
            BrokerDiagnostics.Info(
                "Policy: DENY tool=" + toolName +
                " current_tier=" + currentTier +
                " required_tier=" + evaluation.RequiredTier +
                " reason=\"" + evaluation.Reason + "\"");
        }

        return evaluation;
    }

    private PolicyEvaluation EvaluateCore(string toolName, SessionContext context, ToolUnlockTier currentTier)
    {
        // Read tools always allowed
        if (ReadTools.Contains(toolName))
        {
            return new PolicyEvaluation
            {
                Policy = ToolExecutionPolicy.Allow,
                Reason = "Read-only tool — always allowed.",
                RequiredTier = ToolUnlockTier.Baseline,
                CurrentTier = currentTier
            };
        }

        // Check document is not read-only for write tools
        if (context?.Document != null && context.Document.IsReadOnly)
        {
            return new PolicyEvaluation
            {
                Policy = ToolExecutionPolicy.Deny,
                Reason = "Document is read-only. Write operations are not allowed.",
                RequiredTier = ToolUnlockTier.Assisted,
                CurrentTier = currentTier
            };
        }

        // First-wave write tools require Assisted tier
        if (FirstWaveWriteTools.Contains(toolName))
        {
            if (currentTier < ToolUnlockTier.Assisted)
            {
                return new PolicyEvaluation
                {
                    Policy = ToolExecutionPolicy.Deny,
                    Reason = $"Tool '{toolName}' requires Assisted tier. Current tier: {currentTier}.",
                    RequiredTier = ToolUnlockTier.Assisted,
                    CurrentTier = currentTier
                };
            }

            return new PolicyEvaluation
            {
                Policy = ToolExecutionPolicy.RequireConfirmation,
                Reason = "First-wave write tool — requires user confirmation.",
                RequiredTier = ToolUnlockTier.Assisted,
                CurrentTier = currentTier
            };
        }

        // Advanced write tools require Reviewed tier
        if (AdvancedWriteTools.Contains(toolName))
        {
            if (currentTier < ToolUnlockTier.Reviewed)
            {
                return new PolicyEvaluation
                {
                    Policy = ToolExecutionPolicy.Deny,
                    Reason = $"Tool '{toolName}' requires Reviewed tier. Current tier: {currentTier}.",
                    RequiredTier = ToolUnlockTier.Reviewed,
                    CurrentTier = currentTier
                };
            }

            return new PolicyEvaluation
            {
                Policy = ToolExecutionPolicy.RequireConfirmation,
                Reason = "Advanced write tool — requires elevated confirmation.",
                RequiredTier = ToolUnlockTier.Reviewed,
                CurrentTier = currentTier
            };
        }

        // Unknown tool — deny
        return new PolicyEvaluation
        {
            Policy = ToolExecutionPolicy.Deny,
            Reason = $"Unknown tool '{toolName}' — not in policy registry.",
            RequiredTier = ToolUnlockTier.TrustedBounded,
            CurrentTier = currentTier
        };
    }
}
