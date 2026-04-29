using System;
using System.Collections.Generic;
using System.IO;
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
    public void GetAllStates_ReturnsAllKnownGates()
    {
        Dictionary<string, bool> states = FeatureGateRegistry.GetAllStates();
        Assert.AreEqual(11, states.Count);
    }

    [Test]
    public void EnableModelGate_KnownConstantValue()
    {
        Assert.AreEqual("SOLIDWORKS_AI_ENABLE_MODEL", FeatureGateRegistry.EnableModel);
    }

    [Test]
    public void GetDefault_ReturnsExpectedDefaultsForKnownGates()
    {
        // Zero-config first-run experience — these are load-bearing.
        Assert.IsTrue(FeatureGateRegistry.GetDefault(FeatureGateRegistry.EnableModel));
        Assert.IsTrue(FeatureGateRegistry.GetDefault(FeatureGateRegistry.AgentLoop));
        Assert.IsTrue(FeatureGateRegistry.GetDefault(FeatureGateRegistry.RibbonTab));
        // Context menu default is temporarily false — see FeatureGateRegistry.GetDefault.
        Assert.IsFalse(FeatureGateRegistry.GetDefault(FeatureGateRegistry.ContextMenu));
        Assert.IsTrue(FeatureGateRegistry.GetDefault(FeatureGateRegistry.FirstWaveWrites));
        Assert.IsTrue(FeatureGateRegistry.GetDefault(FeatureGateRegistry.StreamFinalText));
        Assert.IsTrue(FeatureGateRegistry.GetDefault(FeatureGateRegistry.Retrieval));

        // Opt-in surfaces stay off by default.
        Assert.IsFalse(FeatureGateRegistry.GetDefault(FeatureGateRegistry.ToastNotifications));
        Assert.IsFalse(FeatureGateRegistry.GetDefault(FeatureGateRegistry.PropertyManagerPageWrites));
        Assert.IsFalse(FeatureGateRegistry.GetDefault(FeatureGateRegistry.LocalModels));
    }

    [Test]
    public void GetDefault_ReturnsFalseForUnknownGate()
    {
        Assert.IsFalse(FeatureGateRegistry.GetDefault("SOLIDWORKS_AI_UNKNOWN_" + Guid.NewGuid()));
    }

    [Test]
    public void KnownGates_IncludesAllTenGates()
    {
        Assert.AreEqual(11, FeatureGateRegistry.KnownGates.Count);
    }

    [Test]
    public void NativeSidebarGate_KnownConstantValue()
    {
        Assert.AreEqual("SOLIDWORKS_AI_NATIVE_SIDEBAR", FeatureGateRegistry.NativeSidebar);
    }

    [Test]
    public void NativeSidebarGate_DefaultsToFalse_ForSafetyDuringCutover()
    {
        // v1.1 cutover safety: legacy TaskPaneControl stays default until the new
        // sidebar is verified live in SOLIDWORKS. Flip on via Settings or env var.
        Assert.IsFalse(FeatureGateRegistry.GetDefault(FeatureGateRegistry.NativeSidebar));
    }

    [Test]
    public void NativeSidebarGate_RegisteredInKnownGates()
    {
        Assert.That(FeatureGateRegistry.KnownGates, Has.Member(FeatureGateRegistry.NativeSidebar));
    }

    [Test]
    public void ToastGate_KnownConstantValue()
    {
        Assert.AreEqual("SOLIDWORKS_AI_TOAST", FeatureGateRegistry.ToastNotifications);
    }

    [Test]
    public void PmpWritesGate_KnownConstantValue()
    {
        Assert.AreEqual("SOLIDWORKS_AI_PMP_WRITES", FeatureGateRegistry.PropertyManagerPageWrites);
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

[TestFixture]
public class ApiKeyStoreTests
{
    private const string TestProvider = "test-provider-ignore-me";
    private const string TestKey = "sk-test-roundtrip-0123456789";
    private bool _hadPriorStore;
    private (string? Provider, string? Key) _priorStore;

    [SetUp]
    public void PreserveExistingStore()
    {
        _priorStore = ApiKeyStore.Load();
        _hadPriorStore = !string.IsNullOrWhiteSpace(_priorStore.Provider) || !string.IsNullOrWhiteSpace(_priorStore.Key);
    }

    [TearDown]
    public void RestoreExistingStore()
    {
        if (_hadPriorStore && _priorStore.Provider != null)
        {
            ApiKeyStore.Save(_priorStore.Provider, _priorStore.Key ?? string.Empty);
        }
        else
        {
            ApiKeyStore.Clear();
        }
    }

    [Test]
    public void Save_Then_Load_RoundTrips()
    {
        ApiKeyStore.Save(TestProvider, TestKey);
        (string? provider, string? key) = ApiKeyStore.Load();
        Assert.AreEqual(TestProvider, provider);
        Assert.AreEqual(TestKey, key);
    }

    [Test]
    public void GetKey_ReturnsKeyForMatchingProvider()
    {
        ApiKeyStore.Save(TestProvider, TestKey);
        Assert.AreEqual(TestKey, ApiKeyStore.GetKey(TestProvider));
    }

    [Test]
    public void GetKey_ReturnsEmptyForOtherProvider()
    {
        ApiKeyStore.Save(TestProvider, TestKey);
        Assert.AreEqual(string.Empty, ApiKeyStore.GetKey("something-else"));
    }

    [Test]
    public void Clear_RemovesStoredKey()
    {
        ApiKeyStore.Save(TestProvider, TestKey);
        Assert.IsTrue(ApiKeyStore.HasStoredKey());
        ApiKeyStore.Clear();
        Assert.IsFalse(ApiKeyStore.HasStoredKey());
    }

    [Test]
    public void Load_ReturnsNull_WhenFileMissing()
    {
        ApiKeyStore.Clear();
        (string? provider, string? key) = ApiKeyStore.Load();
        Assert.IsNull(provider);
        Assert.IsNull(key);
    }

    [Test]
    public void Load_TreatsCorruptedFileAsMissing()
    {
        // Write random bytes to the keys file — DPAPI Unprotect will throw,
        // Load should swallow and return (null, null).
        string path = ApiKeyStore.GetKeysPath();
        File.WriteAllBytes(path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        (string? provider, string? key) = ApiKeyStore.Load();
        Assert.IsNull(provider);
        Assert.IsNull(key);
    }
}

[TestFixture]
public class SwBuildStateServiceTests
{
    // Use a sentinel that no real SW build would ever produce, so we don't step
    // on a live install's persisted state when running tests alongside a
    // production installation.
    private const string SentinelBuild = "0.0.0.TEST-ROUNDTRIP-DO-NOT-USE";
    private string _priorPersisted = string.Empty;

    [SetUp]
    public void PreserveExisting()
    {
        _priorPersisted = SwBuildStateService.GetLastVerifiedBuild();
    }

    [TearDown]
    public void Restore()
    {
        if (string.IsNullOrWhiteSpace(_priorPersisted))
        {
            SwBuildStateService.ClearLastVerifiedBuild();
        }
        else
        {
            SwBuildStateService.SaveLastVerifiedBuild(_priorPersisted);
        }
    }

    [Test]
    public void GetStatePath_IsUnderLocalAppData()
    {
        string path = SwBuildStateService.GetStatePath();
        Assert.That(path, Does.Contain("Adze"));
        Assert.That(path, Does.Contain("state"));
        Assert.That(path, Does.EndWith("sw-build.txt"));
    }

    [Test]
    public void Save_Then_Get_RoundTrips()
    {
        SwBuildStateService.SaveLastVerifiedBuild(SentinelBuild);
        Assert.AreEqual(SentinelBuild, SwBuildStateService.GetLastVerifiedBuild());
    }

    [Test]
    public void Clear_RemovesPersistedBuild()
    {
        SwBuildStateService.SaveLastVerifiedBuild(SentinelBuild);
        SwBuildStateService.ClearLastVerifiedBuild();
        Assert.AreEqual(string.Empty, SwBuildStateService.GetLastVerifiedBuild());
    }

    [Test]
    public void HasBuildChanged_TrueWhenNoPersistedValue()
    {
        SwBuildStateService.ClearLastVerifiedBuild();
        Assert.IsTrue(SwBuildStateService.HasBuildChangedSinceLastVerification("34.1.0.0140"));
    }

    [Test]
    public void HasBuildChanged_FalseWhenPersistedMatches()
    {
        SwBuildStateService.SaveLastVerifiedBuild(SentinelBuild);
        Assert.IsFalse(SwBuildStateService.HasBuildChangedSinceLastVerification(SentinelBuild));
    }

    [Test]
    public void HasBuildChanged_TrueWhenBuildsDiffer()
    {
        SwBuildStateService.SaveLastVerifiedBuild("34.0.0.0120");
        Assert.IsTrue(SwBuildStateService.HasBuildChangedSinceLastVerification("34.1.0.0140"));
    }

    [Test]
    public void Save_TrimsWhitespace()
    {
        SwBuildStateService.SaveLastVerifiedBuild("  " + SentinelBuild + "  \n");
        Assert.AreEqual(SentinelBuild, SwBuildStateService.GetLastVerifiedBuild());
    }

    [Test]
    public void Save_IgnoresEmptyOrWhitespace()
    {
        SwBuildStateService.SaveLastVerifiedBuild(SentinelBuild);
        SwBuildStateService.SaveLastVerifiedBuild("");
        Assert.AreEqual(SentinelBuild, SwBuildStateService.GetLastVerifiedBuild(),
            "Empty string should not overwrite a previously-valid persisted build.");
    }
}

[TestFixture]
public class FeatureGateConfigServiceTests
{
    // These tests write to the real config path. They set/restore keys that
    // are unknown to FeatureGateRegistry.KnownGates so production behavior
    // is unaffected — FeatureGateRegistry.IsEnabled short-circuits unknown
    // names before consulting the config.

    private const string SentinelGate = "SOLIDWORKS_AI_TEST_SENTINEL_DO_NOT_USE";

    [SetUp]
    public void ClearSentinel()
    {
        Dictionary<string, bool> config = FeatureGateConfigService.Load();
        if (config.ContainsKey(SentinelGate))
        {
            config.Remove(SentinelGate);
            FeatureGateConfigService.Save(config);
        }
        FeatureGateRegistry.InvalidateCache();
    }

    [Test]
    public void GetConfigPath_IsUnderLocalAppData()
    {
        string path = FeatureGateConfigService.GetConfigPath();
        Assert.That(path, Does.Contain("Adze"));
        Assert.That(path, Does.EndWith("config.json"));
    }

    [Test]
    public void SetGate_Then_Load_RoundTrips()
    {
        FeatureGateConfigService.SetGate(SentinelGate, true);
        Dictionary<string, bool> loaded = FeatureGateConfigService.Load();
        Assert.IsTrue(loaded.ContainsKey(SentinelGate));
        Assert.IsTrue(loaded[SentinelGate]);

        FeatureGateConfigService.SetGate(SentinelGate, false);
        loaded = FeatureGateConfigService.Load();
        Assert.IsFalse(loaded[SentinelGate]);
    }

    [Test]
    public void Load_ReturnsEmpty_WhenFileAbsent()
    {
        // We can't easily force the file to not exist without racing other tests.
        // Instead verify Load tolerates malformed content (same code path as missing).
        string path = FeatureGateConfigService.GetConfigPath();
        string original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        try
        {
            File.WriteAllText(path, "{ this is not valid json");
            Dictionary<string, bool> loaded = FeatureGateConfigService.Load();
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded.Count);
        }
        finally
        {
            if (string.IsNullOrEmpty(original)) File.Delete(path);
            else File.WriteAllText(path, original);
        }
    }

    [Test]
    public void InvalidateCache_ForcesRefreshOnNextIsEnabled()
    {
        // Known gate — use LocalModels (default false) and flip via config.
        string gate = FeatureGateRegistry.LocalModels;
        Dictionary<string, bool> originalConfig = FeatureGateConfigService.Load();
        bool? originalHad = originalConfig.ContainsKey(gate) ? originalConfig[gate] : (bool?)null;

        try
        {
            FeatureGateConfigService.SetGate(gate, true);
            FeatureGateRegistry.InvalidateCache();

            // Only test this when the env var is NOT set (env wins if it is)
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(gate)))
            {
                Assert.IsTrue(FeatureGateRegistry.IsEnabled(gate));
            }
        }
        finally
        {
            // Restore
            Dictionary<string, bool> restore = FeatureGateConfigService.Load();
            if (originalHad.HasValue) restore[gate] = originalHad.Value;
            else restore.Remove(gate);
            FeatureGateConfigService.Save(restore);
            FeatureGateRegistry.InvalidateCache();
        }
    }
}
