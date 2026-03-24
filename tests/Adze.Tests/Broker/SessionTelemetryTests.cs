using NUnit.Framework;
using Adze.Broker.Models;

namespace Adze.Tests.Broker;

[TestFixture]
public class SessionTelemetryTests
{
    private SessionTelemetry _telemetry = null!;

    [SetUp]
    public void SetUp()
    {
        _telemetry = new SessionTelemetry();
    }

    [Test]
    public void NewTelemetry_AllCountersZero()
    {
        Assert.AreEqual(0, _telemetry.RunsTotal);
        Assert.AreEqual(0, _telemetry.RunsSuccess);
        Assert.AreEqual(0, _telemetry.AgenticRuns);
        Assert.AreEqual(0, _telemetry.ClassicRuns);
        Assert.AreEqual(0, _telemetry.WritesProposed);
        Assert.AreEqual(0, _telemetry.GetToolCallRanking().Count);
    }

    [Test]
    public void RecordRunOutcome_TracksSuccess()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);
        _telemetry.RecordRunOutcome(AgentRunOutcome.Failed);

        Assert.AreEqual(3, _telemetry.RunsTotal);
        Assert.AreEqual(2, _telemetry.RunsSuccess);
        Assert.AreEqual(1, _telemetry.RunsFailed);
    }

    [Test]
    public void RecordRunOutcome_TracksCancelled()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Cancelled);

        Assert.AreEqual(1, _telemetry.RunsCancelled);
    }

    [Test]
    public void RecordRunOutcome_TracksFellBack()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.FellBack);
        _telemetry.RecordRunOutcome(AgentRunOutcome.BlockedByPolicy);

        Assert.AreEqual(2, _telemetry.RunsFellBack);
    }

    [Test]
    public void RecordClassicRun_IncrementsClassicAndTotal()
    {
        _telemetry.RecordClassicRun(true);
        _telemetry.RecordClassicRun(false);

        Assert.AreEqual(2, _telemetry.RunsTotal);
        Assert.AreEqual(2, _telemetry.ClassicRuns);
        Assert.AreEqual(1, _telemetry.RunsSuccess);
        Assert.AreEqual(1, _telemetry.RunsFellBack);
    }

    [Test]
    public void RecordAgenticRun_IncrementsCounter()
    {
        _telemetry.RecordAgenticRun();
        _telemetry.RecordAgenticRun();

        Assert.AreEqual(2, _telemetry.AgenticRuns);
    }

    [Test]
    public void RecordToolCall_TracksFrequency()
    {
        _telemetry.RecordToolCall("get_dimensions", false);
        _telemetry.RecordToolCall("get_dimensions", false);
        _telemetry.RecordToolCall("get_feature_tree_slice", false);

        var ranking = _telemetry.GetToolCallRanking();
        Assert.AreEqual(2, ranking.Count);
        Assert.AreEqual("get_dimensions", ranking[0].Key);
        Assert.AreEqual(2, ranking[0].Value);
        Assert.AreEqual("get_feature_tree_slice", ranking[1].Key);
        Assert.AreEqual(1, ranking[1].Value);
    }

    [Test]
    public void RecordToolCall_TracksErrors()
    {
        _telemetry.RecordToolCall("get_dimensions", false);
        _telemetry.RecordToolCall("get_dimensions", true);

        var counts = _telemetry.GetToolCallCounts();
        var errors = _telemetry.GetToolErrorCounts();
        Assert.AreEqual(2, counts["get_dimensions"]);
        Assert.AreEqual(1, errors["get_dimensions"]);
    }

    [Test]
    public void RecordToolCall_EmptyNameIgnored()
    {
        _telemetry.RecordToolCall("", false);
        _telemetry.RecordToolCall(null!, false);

        Assert.AreEqual(0, _telemetry.GetToolCallRanking().Count);
    }

    [Test]
    public void RecordToolCall_CaseInsensitive()
    {
        _telemetry.RecordToolCall("Get_Dimensions", false);
        _telemetry.RecordToolCall("get_dimensions", false);

        var counts = _telemetry.GetToolCallCounts();
        Assert.AreEqual(2, counts["get_dimensions"]);
    }

    [Test]
    public void WriteTracking_AllCounters()
    {
        _telemetry.RecordWriteProposed();
        _telemetry.RecordWriteProposed();
        _telemetry.RecordWriteProposed();
        _telemetry.RecordWriteApplied();
        _telemetry.RecordWriteCancelled();
        _telemetry.RecordWriteFailed();

        Assert.AreEqual(3, _telemetry.WritesProposed);
        Assert.AreEqual(1, _telemetry.WritesApplied);
        Assert.AreEqual(1, _telemetry.WritesCancelled);
        Assert.AreEqual(1, _telemetry.WritesFailed);
    }

    [Test]
    public void RecordWritePlanBatchApplied_Increments()
    {
        _telemetry.RecordWritePlanBatchApplied();
        _telemetry.RecordWritePlanBatchApplied();

        Assert.AreEqual(2, _telemetry.WritePlanBatchesApplied);
    }

    [Test]
    public void RecordCancellation_TracksPhase()
    {
        _telemetry.RecordCancellation("tool_execution");
        _telemetry.RecordCancellation("api_call");
        _telemetry.RecordCancellation("user");

        Assert.AreEqual(3, _telemetry.CancelledByUser);
        Assert.AreEqual(1, _telemetry.CancelledDuringToolExecution);
        Assert.AreEqual(1, _telemetry.CancelledDuringApiCall);
    }

    [Test]
    public void RecipeTracking()
    {
        _telemetry.RecordRecipeCaptured();
        _telemetry.RecordRecipeCaptured();
        _telemetry.RecordRecipePromoted();

        Assert.AreEqual(2, _telemetry.RecipesCaptureCandidates);
        Assert.AreEqual(1, _telemetry.RecipesPromoted);
    }

    [Test]
    public void SuccessRate_Computed()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);
        _telemetry.RecordRunOutcome(AgentRunOutcome.Failed);

        Assert.AreEqual(0.5, _telemetry.SuccessRate, 0.001);
    }

    [Test]
    public void SuccessRate_ZeroWhenNoRuns()
    {
        Assert.AreEqual(0.0, _telemetry.SuccessRate);
    }

    [Test]
    public void CancellationRate_Computed()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);
        _telemetry.RecordRunOutcome(AgentRunOutcome.Cancelled);
        _telemetry.RecordRunOutcome(AgentRunOutcome.Cancelled);

        Assert.AreEqual(2.0 / 3.0, _telemetry.CancellationRate, 0.001);
    }

    [Test]
    public void WriteApplyRate_Computed()
    {
        _telemetry.RecordWriteProposed();
        _telemetry.RecordWriteProposed();
        _telemetry.RecordWriteApplied();

        Assert.AreEqual(0.5, _telemetry.WriteApplyRate, 0.001);
    }

    [Test]
    public void FormatSummary_ContainsRunStats()
    {
        _telemetry.RecordAgenticRun();
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);
        _telemetry.RecordToolCall("get_dimensions", false);

        string summary = _telemetry.FormatSummary();

        Assert.That(summary, Does.Contain("Session Telemetry"));
        Assert.That(summary, Does.Contain("Runs: 1 total"));
        Assert.That(summary, Does.Contain("1 agentic"));
        Assert.That(summary, Does.Contain("Success: 1 (100%)"));
        Assert.That(summary, Does.Contain("get_dimensions: 1"));
    }

    [Test]
    public void FormatSummary_IncludesWriteStats()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);
        _telemetry.RecordWriteProposed();
        _telemetry.RecordWriteApplied();

        string summary = _telemetry.FormatSummary();
        Assert.That(summary, Does.Contain("Writes:"));
        Assert.That(summary, Does.Contain("1 applied"));
    }

    [Test]
    public void FormatSummary_OmitsWritesWhenNone()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);

        string summary = _telemetry.FormatSummary();
        Assert.That(summary, Does.Not.Contain("Writes:"));
    }

    [Test]
    public void FormatSummary_IncludesCancellationDetail()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Cancelled);
        _telemetry.RecordCancellation("api_call");

        string summary = _telemetry.FormatSummary();
        Assert.That(summary, Does.Contain("Cancellations:"));
        Assert.That(summary, Does.Contain("During API call: 1"));
    }

    [Test]
    public void FormatSummary_IncludesRecipes()
    {
        _telemetry.RecordRunOutcome(AgentRunOutcome.Success);
        _telemetry.RecordRecipeCaptured();
        _telemetry.RecordRecipePromoted();

        string summary = _telemetry.FormatSummary();
        Assert.That(summary, Does.Contain("Recipes:"));
        Assert.That(summary, Does.Contain("1 captured"));
        Assert.That(summary, Does.Contain("1 promoted"));
    }
}
