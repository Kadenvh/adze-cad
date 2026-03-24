using NUnit.Framework;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Enums;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Broker.Orchestration;
using Adze.Tests.Helpers;

namespace Adze.Tests.Broker;

[TestFixture]
public class AgentPolicyEngineTests
{
    private const string UserId = "test-user";

    private static AgentPolicyEngine CreateEngine(ToolUnlockTier tier)
    {
        return new AgentPolicyEngine(new StubTrustService(tier));
    }

    // --- Read tools ---

    [Test]
    public void ReadTool_AlwaysAllowed_AtBaseline()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Baseline);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(ToolNames.GetActiveDocument, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Allow, result.Policy);
    }

    [Test]
    public void SearchProjectFiles_IsReadTool()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Baseline);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(ToolNames.SearchProjectFiles, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Allow, result.Policy);
    }

    [TestCase(ToolNames.GetActiveDocument)]
    [TestCase(ToolNames.GetDocumentSummary)]
    [TestCase(ToolNames.GetFeatureTreeSlice)]
    [TestCase(ToolNames.GetDimensions)]
    [TestCase(ToolNames.GetConfigurations)]
    [TestCase(ToolNames.GetCustomProperties)]
    [TestCase(ToolNames.GetMates)]
    [TestCase(ToolNames.GetRebuildDiagnostics)]
    [TestCase(ToolNames.GetReferenceGraph)]
    [TestCase(ToolNames.GetSelectionContext)]
    public void AllReadTools_Allowed_AtBaseline(string toolName)
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Baseline);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(toolName, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Allow, result.Policy);
    }

    // --- First-wave write tools ---

    [Test]
    public void FirstWaveWrite_Denied_AtBaseline()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Baseline);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(ToolNames.SetCustomProperty, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Deny, result.Policy);
        Assert.That(result.Reason, Does.Contain("Assisted"));
    }

    [Test]
    public void FirstWaveWrite_RequiresConfirmation_AtAssisted()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Assisted);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(ToolNames.SetCustomProperty, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.RequireConfirmation, result.Policy);
    }

    [TestCase(ToolNames.SetCustomProperty)]
    [TestCase(ToolNames.SetDimensionValue)]
    [TestCase(ToolNames.SuppressFeature)]
    [TestCase(ToolNames.UnsuppressFeature)]
    [TestCase(ToolNames.RenameObject)]
    public void AllFirstWaveWrites_RequireConfirmation_AtAssisted(string toolName)
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Assisted);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(toolName, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.RequireConfirmation, result.Policy);
    }

    // --- Advanced write tools ---

    [Test]
    public void AdvancedWrite_Denied_AtAssisted()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Assisted);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(ToolNames.InsertComponent, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Deny, result.Policy);
        Assert.That(result.Reason, Does.Contain("Reviewed"));
    }

    [Test]
    public void AdvancedWrite_RequiresConfirmation_AtReviewed()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Reviewed);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(ToolNames.InsertComponent, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.RequireConfirmation, result.Policy);
    }

    [TestCase(ToolNames.InsertComponent)]
    [TestCase(ToolNames.CreateDrawingView)]
    public void AllAdvancedWrites_RequireConfirmation_AtReviewed(string toolName)
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Reviewed);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(toolName, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.RequireConfirmation, result.Policy);
    }

    // --- Read-only document ---

    [Test]
    public void WriteOnReadOnlyDoc_Denied()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.TrustedBounded);
        SessionContext context = SessionContextFactory.CreateReadOnly();

        PolicyEvaluation result = engine.Evaluate(ToolNames.SetCustomProperty, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Deny, result.Policy);
        Assert.That(result.Reason, Does.Contain("read-only"));
    }

    [Test]
    public void ReadOnReadOnlyDoc_StillAllowed()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Baseline);
        SessionContext context = SessionContextFactory.CreateReadOnly();

        PolicyEvaluation result = engine.Evaluate(ToolNames.GetDimensions, context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Allow, result.Policy);
    }

    // --- Unknown tools ---

    [Test]
    public void UnknownTool_Denied()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.TrustedBounded);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate("nonexistent_tool", context, UserId);

        Assert.AreEqual(ToolExecutionPolicy.Deny, result.Policy);
        Assert.That(result.Reason, Does.Contain("Unknown"));
    }

    // --- Tier tracking ---

    [Test]
    public void Evaluation_ReportsCurrentAndRequiredTier()
    {
        AgentPolicyEngine engine = CreateEngine(ToolUnlockTier.Baseline);
        SessionContext context = SessionContextFactory.CreateWithPart();

        PolicyEvaluation result = engine.Evaluate(ToolNames.InsertComponent, context, UserId);

        Assert.AreEqual(ToolUnlockTier.Reviewed, result.RequiredTier);
        Assert.AreEqual(ToolUnlockTier.Baseline, result.CurrentTier);
    }

    // --- Stub ---

    private sealed class StubTrustService : ITrustService
    {
        private readonly ToolUnlockTier _tier;

        public StubTrustService(ToolUnlockTier tier) => _tier = tier;

        public ToolUnlockTier GetCurrentTier(string userId) => _tier;

        public bool CanExecuteWriteTool(string toolName, ToolUnlockTier requiredTier, string userId)
            => _tier >= requiredTier;

        public bool CanPromoteRecipe(RecipeCandidate candidate) => true;
    }
}
