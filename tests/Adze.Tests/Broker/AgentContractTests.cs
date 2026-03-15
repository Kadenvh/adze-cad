using System;
using System.Collections.Generic;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;
using Adze.Broker.Models;
using Adze.Tools.Abstractions;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class AgentContractTests
{
    // --- AgentModelSettings.LoadFromEnvironment tests ---

    [Test]
    public void LoadFromEnvironment_NoEnvVars_ReturnsDefaults()
    {
        ClearAgentEnvVars();

        AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

        Assert.That(settings.MaxTokens, Is.EqualTo(4096));
        Assert.That(settings.TimeoutMilliseconds, Is.EqualTo(30000));
        Assert.That(settings.Temperature, Is.EqualTo(0.1));
        Assert.That(settings.MaxIterations, Is.EqualTo(10));
        Assert.That(settings.MaxConsecutiveErrors, Is.EqualTo(2));
        Assert.That(settings.MaxTotalTokens, Is.EqualTo(100000));
        Assert.That(settings.MaxToolResultChars, Is.EqualTo(8192));
        Assert.That(settings.DisableParallelToolCalls, Is.True);
    }

    [Test]
    public void LoadFromEnvironment_AgentMaxTokensSet_OverridesDefault()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS", "8192");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.MaxTokens, Is.EqualTo(8192));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_SharedMaxTokensFallback_UsedWhenAgentNotSet()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS", "2048");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.MaxTokens, Is.EqualTo(2048));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_AgentMaxTokensPreferredOverShared()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS", "6000");
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS", "2048");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.MaxTokens, Is.EqualTo(6000));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS", null);
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_MaxIterationsSet_OverridesDefault()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_ITERATIONS", "5");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.MaxIterations, Is.EqualTo(5));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_ITERATIONS", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_TimeoutFallback_UsesSharedVar()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_TIMEOUT_MS", "15000");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.TimeoutMilliseconds, Is.EqualTo(15000));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_TIMEOUT_MS", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_TemperatureFallback_UsesSharedVar()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_TEMPERATURE", "0.5");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.Temperature, Is.EqualTo(0.5));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_TEMPERATURE", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_DisableParallelToolCallsFalse_ReadsCorrectly()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_DISABLE_PARALLEL_TOOL_CALLS", "false");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.DisableParallelToolCalls, Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_DISABLE_PARALLEL_TOOL_CALLS", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_InvalidMaxTokens_ReturnsDefault()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS", "not_a_number");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.MaxTokens, Is.EqualTo(4096));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS", null);
        }
    }

    [Test]
    public void LoadFromEnvironment_NegativeMaxIterations_ReturnsDefault()
    {
        ClearAgentEnvVars();
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_ITERATIONS", "-1");

        try
        {
            AgentModelSettings settings = AgentModelSettings.LoadFromEnvironment();

            Assert.That(settings.MaxIterations, Is.EqualTo(10));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_ITERATIONS", null);
        }
    }

    // --- AgentToolDefinition construction tests ---

    [Test]
    public void AgentToolDefinition_DefaultConstruction_HasEmptyValues()
    {
        var def = new AgentToolDefinition();

        Assert.That(def.Name, Is.EqualTo(string.Empty));
        Assert.That(def.Description, Is.EqualTo(string.Empty));
        Assert.That(def.ParameterSchema, Is.Not.Null);
        Assert.That(def.ParameterSchema, Is.Empty);
        Assert.That(def.Capability, Is.Null);
    }

    [Test]
    public void AgentToolDefinition_WithProperties_RoundTripsCorrectly()
    {
        var capability = new ToolCapabilityMetadata
        {
            CapabilityClass = ToolCapabilityClass.ReadSafe,
            ApprovalRequirement = ApprovalRequirement.None,
            RequiresUiThread = true
        };

        var def = new AgentToolDefinition
        {
            Name = "get_dimensions",
            Description = "Get dimension values for the active document.",
            ParameterSchema = new Dictionary<string, object?>
            {
                ["scope"] = "selection",
                ["include_driven"] = true
            },
            Capability = capability
        };

        Assert.That(def.Name, Is.EqualTo("get_dimensions"));
        Assert.That(def.Description, Is.EqualTo("Get dimension values for the active document."));
        Assert.That(def.ParameterSchema.Count, Is.EqualTo(2));
        Assert.That(def.Capability, Is.Not.Null);
        Assert.That(def.Capability!.CapabilityClass, Is.EqualTo(ToolCapabilityClass.ReadSafe));
        Assert.That(def.Capability.RequiresUiThread, Is.True);
    }

    // --- Enum value existence tests ---

    [Test]
    public void AgentStopReason_AllExpectedValuesExist()
    {
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.EndTurn), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.ToolUse), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.WaitingForApproval), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.Cancelled), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.MaxTokens), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.MaxIterations), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.Error), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentStopReason), AgentStopReason.Fallback), Is.True);
        Assert.That(Enum.GetValues(typeof(AgentStopReason)).Length, Is.EqualTo(8));
    }

    [Test]
    public void AgentRunOutcome_AllExpectedValuesExist()
    {
        Assert.That(Enum.IsDefined(typeof(AgentRunOutcome), AgentRunOutcome.Success), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentRunOutcome), AgentRunOutcome.Cancelled), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentRunOutcome), AgentRunOutcome.BlockedByPolicy), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentRunOutcome), AgentRunOutcome.Failed), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentRunOutcome), AgentRunOutcome.FellBack), Is.True);
        Assert.That(Enum.GetValues(typeof(AgentRunOutcome)).Length, Is.EqualTo(5));
    }

    [Test]
    public void AgentProgressKind_AllExpectedValuesExist()
    {
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.Started), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.Thinking), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.ToolRequested), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.ToolExecuting), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.WaitingForApproval), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.Approved), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.Cancelled), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.Verifying), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.Completed), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.Failed), Is.True);
        Assert.That(Enum.IsDefined(typeof(AgentProgressKind), AgentProgressKind.FellBack), Is.True);
        Assert.That(Enum.GetValues(typeof(AgentProgressKind)).Length, Is.EqualTo(11));
    }

    [Test]
    public void ToolCapabilityClass_AllExpectedValuesExist()
    {
        Assert.That(Enum.IsDefined(typeof(ToolCapabilityClass), ToolCapabilityClass.ReadSafe), Is.True);
        Assert.That(Enum.IsDefined(typeof(ToolCapabilityClass), ToolCapabilityClass.SoftWrite), Is.True);
        Assert.That(Enum.IsDefined(typeof(ToolCapabilityClass), ToolCapabilityClass.HardWriteFirstWave), Is.True);
        Assert.That(Enum.IsDefined(typeof(ToolCapabilityClass), ToolCapabilityClass.HardWriteAdvanced), Is.True);
        Assert.That(Enum.IsDefined(typeof(ToolCapabilityClass), ToolCapabilityClass.DeferredHighRisk), Is.True);
        Assert.That(Enum.GetValues(typeof(ToolCapabilityClass)).Length, Is.EqualTo(5));
    }

    [Test]
    public void ApprovalRequirement_AllExpectedValuesExist()
    {
        Assert.That(Enum.IsDefined(typeof(ApprovalRequirement), ApprovalRequirement.None), Is.True);
        Assert.That(Enum.IsDefined(typeof(ApprovalRequirement), ApprovalRequirement.StandardConfirmation), Is.True);
        Assert.That(Enum.IsDefined(typeof(ApprovalRequirement), ApprovalRequirement.ElevatedConfirmation), Is.True);
        Assert.That(Enum.IsDefined(typeof(ApprovalRequirement), ApprovalRequirement.Disallowed), Is.True);
        Assert.That(Enum.GetValues(typeof(ApprovalRequirement)).Length, Is.EqualTo(4));
    }

    // --- ModelUsage integration tests ---

    [Test]
    public void AgentTurnResponse_Usage_DefaultsToEmptyModelUsage()
    {
        var response = new AgentTurnResponse();

        Assert.That(response.Usage, Is.Not.Null);
        Assert.That(response.Usage.PromptTokens, Is.EqualTo(0));
        Assert.That(response.Usage.CompletionTokens, Is.EqualTo(0));
        Assert.That(response.Usage.TotalTokens, Is.EqualTo(0));
    }

    [Test]
    public void AgentTurnResponse_Usage_AccumulatesWithOperator()
    {
        var response1 = new AgentTurnResponse
        {
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 50, TotalTokens = 150 }
        };

        var response2 = new AgentTurnResponse
        {
            Usage = new ModelUsage { PromptTokens = 200, CompletionTokens = 80, TotalTokens = 280 }
        };

        ModelUsage aggregate = response1.Usage + response2.Usage;

        Assert.That(aggregate.PromptTokens, Is.EqualTo(300));
        Assert.That(aggregate.CompletionTokens, Is.EqualTo(130));
        Assert.That(aggregate.TotalTokens, Is.EqualTo(430));
    }

    [Test]
    public void AgentLoopResult_AggregateUsage_DefaultsToEmptyModelUsage()
    {
        var result = new AgentLoopResult();

        Assert.That(result.AggregateUsage, Is.Not.Null);
        Assert.That(result.AggregateUsage.TotalTokens, Is.EqualTo(0));
    }

    // --- AgentToolCall and AgentToolResult construction tests ---

    [Test]
    public void AgentToolCall_DefaultConstruction_HasEmptyValues()
    {
        var call = new AgentToolCall();

        Assert.That(call.Id, Is.EqualTo(string.Empty));
        Assert.That(call.Name, Is.EqualTo(string.Empty));
        Assert.That(call.Arguments, Is.Not.Null);
        Assert.That(call.Arguments, Is.Empty);
        Assert.That(call.ArgumentsJson, Is.EqualTo(string.Empty));
    }

    [Test]
    public void AgentToolResult_DefaultConstruction_HasEmptyValues()
    {
        var result = new AgentToolResult();

        Assert.That(result.ToolCallId, Is.EqualTo(string.Empty));
        Assert.That(result.ToolName, Is.EqualTo(string.Empty));
        Assert.That(result.OutputJson, Is.EqualTo(string.Empty));
        Assert.That(result.IsError, Is.False);
    }

    [Test]
    public void AgentToolResult_ErrorResult_SetsIsErrorTrue()
    {
        var result = new AgentToolResult
        {
            ToolCallId = "call_abc123",
            ToolName = "get_dimensions",
            OutputJson = "{\"error\":\"Document not open\"}",
            IsError = true
        };

        Assert.That(result.IsError, Is.True);
        Assert.That(result.ToolCallId, Is.EqualTo("call_abc123"));
    }

    // --- AgentProgressUpdate tests ---

    [Test]
    public void AgentProgressUpdate_DefaultConstruction_HasEmptyValues()
    {
        var update = new AgentProgressUpdate();

        Assert.That(update.Kind, Is.EqualTo(AgentProgressKind.Started));
        Assert.That(update.Message, Is.EqualTo(string.Empty));
        Assert.That(update.ToolName, Is.Null);
        Assert.That(update.Iteration, Is.EqualTo(0));
    }

    // --- ToolExecutionContext tests ---

    [Test]
    public void ToolExecutionContext_DefaultConstruction_HasEmptyValues()
    {
        var ctx = new ToolExecutionContext();

        Assert.That(ctx.SessionId, Is.EqualTo(string.Empty));
        Assert.That(ctx.DocumentKey, Is.EqualTo(string.Empty));
        Assert.That(ctx.CurrentIteration, Is.EqualTo(0));
        Assert.That(ctx.CancellationToken, Is.EqualTo(System.Threading.CancellationToken.None));
    }

    // --- ToolCapabilityMetadata tests ---

    [Test]
    public void ToolCapabilityMetadata_DefaultConstruction_HasReadSafeDefaults()
    {
        var meta = new ToolCapabilityMetadata();

        Assert.That(meta.CapabilityClass, Is.EqualTo(ToolCapabilityClass.ReadSafe));
        Assert.That(meta.ApprovalRequirement, Is.EqualTo(ApprovalRequirement.None));
        Assert.That(meta.RequiresUiThread, Is.False);
        Assert.That(meta.RequiresRebuild, Is.False);
        Assert.That(meta.SupportsUndoGrouping, Is.False);
        Assert.That(meta.MustCaptureSnapshot, Is.False);
        Assert.That(meta.AllowedInBatchPlan, Is.True);
    }

    [Test]
    public void ToolCapabilityMetadata_HardWriteFirstWave_FullConfiguration()
    {
        var meta = new ToolCapabilityMetadata
        {
            CapabilityClass = ToolCapabilityClass.HardWriteFirstWave,
            ApprovalRequirement = ApprovalRequirement.StandardConfirmation,
            RequiresUiThread = true,
            RequiresRebuild = true,
            SupportsUndoGrouping = true,
            MustCaptureSnapshot = true,
            AllowedInBatchPlan = false
        };

        Assert.That(meta.CapabilityClass, Is.EqualTo(ToolCapabilityClass.HardWriteFirstWave));
        Assert.That(meta.ApprovalRequirement, Is.EqualTo(ApprovalRequirement.StandardConfirmation));
        Assert.That(meta.RequiresUiThread, Is.True);
        Assert.That(meta.RequiresRebuild, Is.True);
        Assert.That(meta.SupportsUndoGrouping, Is.True);
        Assert.That(meta.MustCaptureSnapshot, Is.True);
        Assert.That(meta.AllowedInBatchPlan, Is.False);
    }

    // --- Helper ---

    private static void ClearAgentEnvVars()
    {
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOKENS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_MAX_TOKENS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_TIMEOUT_MS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_TIMEOUT_MS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_TEMPERATURE", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_TEMPERATURE", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_ITERATIONS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_CONSECUTIVE_ERRORS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOTAL_TOKENS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_MAX_TOOL_RESULT_CHARS", null);
        Environment.SetEnvironmentVariable("SOLIDWORKS_AI_AGENT_DISABLE_PARALLEL_TOOL_CALLS", null);
    }
}
