using System;
using System.Collections.Generic;
using NUnit.Framework;
using Adze.Broker.Configuration;

namespace Adze.Tests.Broker;

[TestFixture]
public class CostBudgetSettingsTests
{
    [Test]
    public void Defaults_AreReasonable()
    {
        var settings = new CostBudgetSettings();
        Assert.AreEqual(500000, settings.MaxSessionTokens);
        Assert.AreEqual(2000000, settings.MaxDailyTokens);
        Assert.AreEqual(80, settings.WarningThresholdPercent);
    }
}

[TestFixture]
public class BudgetStatusTests
{
    [Test]
    public void IsOverBudget_FalseWhenUnderLimit()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 1000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 5000,
            DailyTokenLimit = 2000000
        };

        Assert.IsFalse(status.IsOverBudget);
        Assert.IsFalse(status.SessionLimitReached);
        Assert.IsFalse(status.DailyLimitReached);
    }

    [Test]
    public void IsOverBudget_TrueWhenSessionLimitReached()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 500000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 500000,
            DailyTokenLimit = 2000000
        };

        Assert.IsTrue(status.IsOverBudget);
        Assert.IsTrue(status.SessionLimitReached);
    }

    [Test]
    public void IsOverBudget_TrueWhenDailyLimitReached()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 1000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 2000000,
            DailyTokenLimit = 2000000
        };

        Assert.IsTrue(status.IsOverBudget);
        Assert.IsTrue(status.DailyLimitReached);
    }

    [Test]
    public void IsNearLimit_TrueAt80Percent()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 400000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 100000,
            DailyTokenLimit = 2000000
        };

        Assert.IsTrue(status.IsNearLimit(80));
    }

    [Test]
    public void IsNearLimit_FalseAt50Percent()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 250000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 100000,
            DailyTokenLimit = 2000000
        };

        Assert.IsFalse(status.IsNearLimit(80));
    }

    [Test]
    public void FormatSummary_IncludesPercentages()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 100000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 400000,
            DailyTokenLimit = 2000000
        };

        string summary = status.FormatSummary();
        Assert.That(summary, Does.Contain("Session:"));
        Assert.That(summary, Does.Contain("Daily:"));
        Assert.That(summary, Does.Contain("20.0%"));
    }

    [Test]
    public void IsOverBudget_WhenBothLimitsReached()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 600000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 3000000,
            DailyTokenLimit = 2000000
        };

        Assert.IsTrue(status.IsOverBudget);
        Assert.IsTrue(status.SessionLimitReached);
        Assert.IsTrue(status.DailyLimitReached);
    }

    [Test]
    public void IsNearLimit_DailyTriggersAlone()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 10000,
            SessionTokenLimit = 500000,
            DailyTokensUsed = 1700000,
            DailyTokenLimit = 2000000
        };

        Assert.IsTrue(status.IsNearLimit(80));
        Assert.IsFalse(status.IsOverBudget);
    }

    [Test]
    public void FormatSummary_ZeroLimitShowsNA()
    {
        var status = new BudgetStatus
        {
            SessionTokensUsed = 0,
            SessionTokenLimit = 0,
            DailyTokensUsed = 0,
            DailyTokenLimit = 0
        };

        string summary = status.FormatSummary();
        Assert.That(summary, Does.Contain("n/a"));
    }
}

[TestFixture]
public class FeatureGateRegistryTests
{
    [Test]
    public void GetAllStates_Returns7Gates()
    {
        Dictionary<string, bool> states = FeatureGateRegistry.GetAllStates();
        Assert.AreEqual(7, states.Count);
    }

    [Test]
    public void FormatSummary_ContainsAllGateNames()
    {
        string summary = FeatureGateRegistry.FormatSummary();
        Assert.That(summary, Does.Contain("AGENT_LOOP"));
        Assert.That(summary, Does.Contain("FIRST_WAVE_WRITES"));
        Assert.That(summary, Does.Contain("RETRIEVAL"));
        Assert.That(summary, Does.Contain("LOCAL_MODELS"));
        Assert.That(summary, Does.Contain("STREAM_FINAL_TEXT"));
        Assert.That(summary, Does.Contain("RIBBON"));
        Assert.That(summary, Does.Contain("CONTEXT_MENU"));
    }

    [Test]
    public void RibbonGate_KnownConstantValue()
    {
        Assert.AreEqual("SOLIDWORKS_AI_RIBBON", FeatureGateRegistry.RibbonTab);
    }

    [Test]
    public void ContextMenuGate_KnownConstantValue()
    {
        Assert.AreEqual("SOLIDWORKS_AI_CONTEXT_MENU", FeatureGateRegistry.ContextMenu);
    }

    [Test]
    public void IsEnabled_ReturnsFalseForUnsetGate()
    {
        bool result = FeatureGateRegistry.IsEnabled("SOLIDWORKS_AI_NONEXISTENT_GATE_" + Guid.NewGuid());
        Assert.IsFalse(result);
    }

    [Test]
    public void GateConstants_MatchExpectedNames()
    {
        Assert.AreEqual("SOLIDWORKS_AI_AGENT_LOOP", FeatureGateRegistry.AgentLoop);
        Assert.AreEqual("SOLIDWORKS_AI_FIRST_WAVE_WRITES", FeatureGateRegistry.FirstWaveWrites);
        Assert.AreEqual("SOLIDWORKS_AI_RETRIEVAL", FeatureGateRegistry.Retrieval);
        Assert.AreEqual("SOLIDWORKS_AI_LOCAL_MODELS", FeatureGateRegistry.LocalModels);
        Assert.AreEqual("SOLIDWORKS_AI_STREAM_FINAL_TEXT", FeatureGateRegistry.StreamFinalText);
    }
}
