using System;
using System.Collections.Generic;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Tests.Helpers;
using Adze.Trace.Serialization;
using NUnit.Framework;

namespace Adze.Tests.Trace;

[TestFixture]
public sealed class ModelJsonMapperTests
{
    [Test]
    public void ToJson_ProgressionState_ContainsAllFields()
    {
        var state = new ProgressionState
        {
            UserId = "test-user",
            UpdatedUtc = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            ToolUnlockTier = ToolUnlockTier.Assisted,
            ExplorationPercent = 66.7,
            UnlockedTools = new List<string> { "get_active_document", "get_document_summary" },
            Achievements = new List<AchievementState>
            {
                new AchievementState
                {
                    AchievementId = "first_trace_logged",
                    Title = "First trace logged",
                    UnlockedUtc = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
                    SourceTraceId = "trace-001"
                }
            }
        };

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(state);

        Assert.That(json["user_id"], Is.EqualTo("test-user"));
        Assert.That(json["tool_unlock_tier"], Is.EqualTo("assisted"));
        Assert.That(json["exploration_percent"], Is.EqualTo(66.7));
        Assert.That(json["unlocked_tools"], Is.Not.Null);
        Assert.That(json["achievements"], Is.Not.Null);
    }

    [Test]
    public void ToProgressionState_RoundTrips()
    {
        var original = new ProgressionState
        {
            UserId = "roundtrip-user",
            UpdatedUtc = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero),
            ToolUnlockTier = ToolUnlockTier.Reviewed,
            ExplorationPercent = 100.0,
            UnlockedTools = new List<string> { "get_active_document", "get_document_summary", "get_selection_context" },
            Achievements = new List<AchievementState>
            {
                new AchievementState
                {
                    AchievementId = "grounding_triad_completed",
                    Title = "Grounding triad completed",
                    UnlockedUtc = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero),
                    SourceTraceId = "trace-roundtrip"
                }
            }
        };

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(original);
        var payload = new Dictionary<string, object>();
        foreach (var kvp in json)
        {
            if (kvp.Value != null)
            {
                payload[kvp.Key] = kvp.Value;
            }
        }

        ProgressionState restored = ModelJsonMapper.ToProgressionState(payload, "fallback-user");

        Assert.That(restored.UserId, Is.EqualTo("roundtrip-user"));
        Assert.That(restored.ToolUnlockTier, Is.EqualTo(ToolUnlockTier.Reviewed));
        Assert.That(restored.ExplorationPercent, Is.EqualTo(100.0));
        Assert.That(restored.UnlockedTools, Has.Count.EqualTo(3));
        Assert.That(restored.Achievements, Has.Count.EqualTo(1));
        Assert.That(restored.Achievements[0].AchievementId, Is.EqualTo("grounding_triad_completed"));
    }

    [Test]
    public void ToProgressionState_MissingFields_UsesFallbacks()
    {
        var payload = new Dictionary<string, object>();

        ProgressionState state = ModelJsonMapper.ToProgressionState(payload, "fallback-user");

        Assert.That(state.UserId, Is.EqualTo("fallback-user"));
        Assert.That(state.ToolUnlockTier, Is.EqualTo(ToolUnlockTier.Baseline));
        Assert.That(state.ExplorationPercent, Is.EqualTo(0));
        Assert.That(state.UnlockedTools, Is.Empty);
        Assert.That(state.Achievements, Is.Empty);
    }

    [Test]
    public void ToJson_TraceEvent_ContainsAllFields()
    {
        var traceEvent = new TraceEvent
        {
            TraceId = "trace-001",
            TimestampUtc = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            Intent = "general_grounding",
            ApprovalState = ApprovalState.Completed,
            ToolSequence = new List<string> { "get_active_document", "get_document_summary" },
            Result = new TraceResult
            {
                Status = "success",
                Summary = "Grounded",
                Warnings = new List<string> { "minor warning" }
            },
            AchievementEvents = new List<string> { "first_trace_logged" },
            ExplorationPercent = 66.7,
            ToolUnlockTier = ToolUnlockTier.Assisted
        };

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(traceEvent);

        Assert.That(json["trace_id"], Is.EqualTo("trace-001"));
        Assert.That(json["intent"], Is.EqualTo("general_grounding"));
        Assert.That(json["approval_state"], Is.EqualTo("completed"));
        Assert.That(json["tool_unlock_tier"], Is.EqualTo("assisted"));

        var result = json["result"] as Dictionary<string, object?>;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!["status"], Is.EqualTo("success"));
    }

    [Test]
    public void ToJson_RecipeCandidate_ContainsAllFields()
    {
        var candidate = new RecipeCandidate
        {
            RecipeId = "recipe_abc123",
            Title = "dimension_review",
            Intent = "dimension_review",
            SourceTraceIds = new List<string> { "t1", "t2", "t3" },
            ToolSequence = new List<string> { "get_active_document", "get_dimensions" },
            PromotionState = "review_ready",
            ReliabilityScore = 1.0,
            RequiredUnlockTier = ToolUnlockTier.Assisted,
            ReviewNotes = new List<string> { "Ready for human review." }
        };

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(candidate);

        Assert.That(json["recipe_id"], Is.EqualTo("recipe_abc123"));
        Assert.That(json["promotion_state"], Is.EqualTo("review_ready"));
        Assert.That(json["reliability_score"], Is.EqualTo(1.0));
        Assert.That(json["required_unlock_tier"], Is.EqualTo("assisted"));
    }

    [Test]
    public void ToRecipeCandidate_RoundTrips()
    {
        var original = new RecipeCandidate
        {
            RecipeId = "recipe_roundtrip",
            Title = "test_recipe",
            Intent = "test_intent",
            SourceTraceIds = new List<string> { "t1", "t2" },
            ToolSequence = new List<string> { "get_active_document" },
            PromotionState = "candidate",
            ReliabilityScore = 0.67,
            RequiredUnlockTier = ToolUnlockTier.Assisted,
            ReviewNotes = new List<string> { "note1" }
        };

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(original);
        var payload = new Dictionary<string, object>();
        foreach (var kvp in json)
        {
            if (kvp.Value != null)
            {
                payload[kvp.Key] = kvp.Value;
            }
        }

        RecipeCandidate restored = ModelJsonMapper.ToRecipeCandidate(payload, "fallback-id");

        Assert.That(restored.RecipeId, Is.EqualTo("recipe_roundtrip"));
        Assert.That(restored.Intent, Is.EqualTo("test_intent"));
        Assert.That(restored.PromotionState, Is.EqualTo("candidate"));
        Assert.That(restored.ReliabilityScore, Is.EqualTo(0.67));
        Assert.That(restored.RequiredUnlockTier, Is.EqualTo(ToolUnlockTier.Assisted));
    }

    [Test]
    public void ToJson_SessionContext_ContainsAllSlices()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(context);

        Assert.That(json.ContainsKey("session"), Is.True);
        Assert.That(json.ContainsKey("environment"), Is.True);
        Assert.That(json.ContainsKey("document"), Is.True);
        Assert.That(json.ContainsKey("selection"), Is.True);
        Assert.That(json.ContainsKey("feature_tree"), Is.True);
        Assert.That(json.ContainsKey("configurations"), Is.True);
        Assert.That(json.ContainsKey("dimensions"), Is.True);
        Assert.That(json.ContainsKey("mates"), Is.True);
        Assert.That(json.ContainsKey("reference_graph"), Is.True);
        Assert.That(json.ContainsKey("diagnostics"), Is.True);
        Assert.That(json.ContainsKey("policy"), Is.True);
    }

    [Test]
    public void ToJson_SessionContext_NullDocument_DocumentIsNull()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(context);

        Assert.That(json["document"], Is.Null);
    }

    [Test]
    public void ToJson_ToolResult_ContainsAllFields()
    {
        var toolResult = new ToolResult
        {
            ToolName = "get_active_document",
            Success = true,
            Summary = "Active document resolved.",
            Warnings = new List<string> { "warning1" },
            Data = new Dictionary<string, object?> { ["type"] = "part" }
        };

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(toolResult);

        Assert.That(json["tool_name"], Is.EqualTo("get_active_document"));
        Assert.That(json["success"], Is.EqualTo(true));
        Assert.That(json["summary"], Is.EqualTo("Active document resolved."));
    }

    [Test]
    public void ToJson_GroundingSnapshotRecord_ContainsAllFields()
    {
        var snapshot = new GroundingSnapshotRecord
        {
            Reason = "test_run",
            TimestampUtc = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            Context = SessionContextFactory.CreateWithPart(),
            ToolResults = new List<ToolResult>
            {
                new ToolResult { ToolName = "get_active_document", Success = true, Summary = "ok" }
            },
            AchievementCount = 1,
            ReviewReadyRecipeCount = 0,
            LatestAchievementTitle = "First trace"
        };

        Dictionary<string, object?> json = ModelJsonMapper.ToJson(snapshot);

        Assert.That(json["reason"], Is.EqualTo("test_run"));
        Assert.That(json["achievement_count"], Is.EqualTo(1));
        Assert.That(json["latest_achievement_title"], Is.EqualTo("First trace"));
        Assert.That(json["context"], Is.Not.Null);
        Assert.That(json["tool_results"], Is.Not.Null);
    }
}
