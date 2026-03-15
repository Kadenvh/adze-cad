using System;
using System.Collections.Generic;
using NUnit.Framework;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Trace.Progression;
using Adze.Trace.Recipes;

namespace Adze.Tests.Trace;

[TestFixture]
public class TrustServiceTests
{
    private TrustService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new TrustService();
    }

    [Test]
    public void CanExecuteWriteTool_ReadTool_AlwaysAllowed()
    {
        bool result = _service.CanExecuteWriteTool("get_dimensions", ToolUnlockTier.Baseline, "testuser");
        Assert.IsTrue(result);
    }

    [Test]
    public void CanPromoteRecipe_ReviewReady_WithSufficientScore_Allowed()
    {
        var candidate = new RecipeCandidate
        {
            PromotionState = "review_ready",
            ReliabilityScore = 1.0,
            SourceTraceIds = new List<string> { "t1", "t2", "t3" }
        };

        bool result = _service.CanPromoteRecipe(candidate);
        Assert.IsTrue(result);
    }

    [Test]
    public void CanPromoteRecipe_NotReviewReady_Rejected()
    {
        var candidate = new RecipeCandidate
        {
            PromotionState = "candidate",
            ReliabilityScore = 0.5,
            SourceTraceIds = new List<string> { "t1" }
        };

        bool result = _service.CanPromoteRecipe(candidate);
        Assert.IsFalse(result);
    }

    [Test]
    public void CanPromoteRecipe_LowReliability_Rejected()
    {
        var candidate = new RecipeCandidate
        {
            PromotionState = "review_ready",
            ReliabilityScore = 0.3,
            SourceTraceIds = new List<string> { "t1" }
        };

        bool result = _service.CanPromoteRecipe(candidate);
        Assert.IsFalse(result);
    }

    [Test]
    public void CanPromoteRecipe_InsufficientTraces_Rejected()
    {
        var candidate = new RecipeCandidate
        {
            PromotionState = "review_ready",
            ReliabilityScore = 1.0,
            SourceTraceIds = new List<string> { "t1" }
        };

        bool result = _service.CanPromoteRecipe(candidate);
        Assert.IsFalse(result);
    }
}

[TestFixture]
public class AgentRecipeCaptureServiceTests
{
    [Test]
    public void CaptureFromAgentRun_EmptyToolSequence_ReturnsNull()
    {
        var result = AgentRecipeCaptureService.CaptureFromAgentRun(
            "test intent",
            new List<string>(),
            true,
            "trace1",
            "testuser");

        Assert.IsNull(result);
    }

    [Test]
    public void CaptureFromAgentRun_ReadOnlyTools_CreatesCandidateWithoutWriteCheck()
    {
        var toolSequence = new List<string> { "get_dimensions", "get_custom_properties" };

        var result = AgentRecipeCaptureService.CaptureFromAgentRun(
            "inspect dimensions and properties",
            toolSequence,
            false, // writes not verified (doesn't matter for read-only)
            "trace1",
            "testuser");

        Assert.IsNotNull(result);
        Assert.AreEqual("candidate", result!.PromotionState);
        Assert.That(result.SourceTraceIds, Does.Contain("trace1"));
    }

    [Test]
    public void CaptureFromAgentRun_WriteToolsWithoutVerification_ReturnsNull()
    {
        var toolSequence = new List<string> { "get_dimensions", "set_dimension_value" };

        var result = AgentRecipeCaptureService.CaptureFromAgentRun(
            "change dimension",
            toolSequence,
            false, // writes not verified
            "trace1",
            "testuser");

        Assert.IsNull(result);
    }

    [Test]
    public void CaptureFromAgentRun_WriteToolsVerified_CreatesCandidate()
    {
        var toolSequence = new List<string> { "get_dimensions", "set_dimension_value" };

        var result = AgentRecipeCaptureService.CaptureFromAgentRun(
            "change dimension verified",
            toolSequence,
            true,
            "trace1",
            "testuser");

        Assert.IsNotNull(result);
        Assert.AreEqual(ToolUnlockTier.Reviewed, result!.RequiredUnlockTier);
        Assert.That(result.ReviewNotes, Has.Some.Contains("write tools"));
    }

    [Test]
    public void CaptureFromAgentRun_MultipleCalls_AccumulatesTraces()
    {
        string uniqueIntent = "unique_test_" + Guid.NewGuid().ToString("N");
        var toolSequence = new List<string> { "get_active_document" };

        var result1 = AgentRecipeCaptureService.CaptureFromAgentRun(
            uniqueIntent, toolSequence, true, "trace_a", "testuser");

        var result2 = AgentRecipeCaptureService.CaptureFromAgentRun(
            uniqueIntent, toolSequence, true, "trace_b", "testuser");

        Assert.IsNotNull(result2);
        Assert.AreEqual(2, result2!.SourceTraceIds.Count);
    }

    [Test]
    public void CaptureFromAgentRun_ThreeCalls_BecomesReviewReady()
    {
        string uniqueIntent = "review_ready_test_" + Guid.NewGuid().ToString("N");
        var toolSequence = new List<string> { "get_document_summary" };

        AgentRecipeCaptureService.CaptureFromAgentRun(
            uniqueIntent, toolSequence, true, "t1_" + uniqueIntent, "testuser");
        AgentRecipeCaptureService.CaptureFromAgentRun(
            uniqueIntent, toolSequence, true, "t2_" + uniqueIntent, "testuser");
        var result = AgentRecipeCaptureService.CaptureFromAgentRun(
            uniqueIntent, toolSequence, true, "t3_" + uniqueIntent, "testuser");

        Assert.IsNotNull(result);
        Assert.AreEqual("review_ready", result!.PromotionState);
        Assert.AreEqual(1.0, result.ReliabilityScore);
    }
}

[TestFixture]
public class WriteToolAchievementTests
{
    [Test]
    public void ProgressionEngine_WriteToolAchievements_RecognizedInToolSet()
    {
        // Verify the write tool names are valid constants
        Assert.AreEqual("set_custom_property", Adze.Contracts.Tooling.ToolNames.SetCustomProperty);
        Assert.AreEqual("set_dimension_value", Adze.Contracts.Tooling.ToolNames.SetDimensionValue);
        Assert.AreEqual("suppress_feature", Adze.Contracts.Tooling.ToolNames.SuppressFeature);
        Assert.AreEqual("unsuppress_feature", Adze.Contracts.Tooling.ToolNames.UnsuppressFeature);
    }

    [Test]
    public void ToolUnlockTier_TrustedBounded_IsHighest()
    {
        Assert.IsTrue(ToolUnlockTier.TrustedBounded > ToolUnlockTier.Reviewed);
        Assert.IsTrue(ToolUnlockTier.Reviewed > ToolUnlockTier.Assisted);
        Assert.IsTrue(ToolUnlockTier.Assisted > ToolUnlockTier.Baseline);
    }

    [Test]
    public void RecipeCandidate_DefaultState_IsCandidate()
    {
        var candidate = new RecipeCandidate();
        Assert.AreEqual("candidate", candidate.PromotionState);
        Assert.AreEqual(0, candidate.ReliabilityScore);
    }
}
