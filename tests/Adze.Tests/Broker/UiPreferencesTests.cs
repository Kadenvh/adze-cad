using System;
using System.Collections.Generic;
using System.IO;
using Adze.Broker.Configuration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

/// <summary>
/// Tests for the v1.1 chunk-3 <see cref="UiPreferences"/> store. Verifies:
///   - Round-trip save/load
///   - Defaults when file is missing
///   - Defaults when file is corrupted
///   - Mode normalization (light/dark/system)
///   - Clarification panel state persistence (intent/scope/output/diagnostics)
///
/// Persistence path is fixed under <c>%LOCALAPPDATA%\Adze\ui-prefs.json</c>.
/// We back the existing file up in <see cref="SetUp"/> and restore it in
/// <see cref="TearDown"/> so the developer's real prefs survive a test run.
/// Mirrors the pattern used by <c>ProductionHardeningTests</c> for the
/// feature-gate config file.
/// </summary>
[TestFixture]
public sealed class UiPreferencesTests
{
    private string _backupPath = string.Empty;
    private bool _hadOriginal;

    [SetUp]
    public void SetUp()
    {
        string path = UiPreferences.GetPath();
        _backupPath = path + ".testbackup-" + Guid.NewGuid().ToString("N");
        if (File.Exists(path))
        {
            File.Move(path, _backupPath);
            _hadOriginal = true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        string path = UiPreferences.GetPath();
        if (File.Exists(path)) File.Delete(path);
        if (_hadOriginal && File.Exists(_backupPath)) File.Move(_backupPath, path);
    }

    [Test]
    public void Load_NoFile_ReturnsDefaults()
    {
        UiPreferences prefs = UiPreferences.Load();

        Assert.That(prefs.UiMode, Is.EqualTo("light"));
        Assert.That(prefs.ClarificationIntent, Is.EqualTo("none"));
        Assert.That(prefs.ClarificationOutput, Is.EqualTo("default"));
        Assert.That(prefs.ClarificationScopes, Is.Empty);
        Assert.That(prefs.ClarificationDiagnostics, Is.Empty);
        Assert.That(prefs.ClarificationExpanded, Is.False);
    }

    [Test]
    public void SaveLoad_RoundTrip_PreservesAllFields()
    {
        var prefs = new UiPreferences
        {
            UiMode = "dark",
            ClarificationIntent = "diagnostic",
            ClarificationOutput = "concise",
            ClarificationScopes = new List<string> { "active_doc", "selected_feature" },
            ClarificationDiagnostics = new List<string> { "tool_calls", "timings" },
            ClarificationExpanded = true,
        };
        prefs.Save();

        UiPreferences loaded = UiPreferences.Load();
        Assert.That(loaded.UiMode, Is.EqualTo("dark"));
        Assert.That(loaded.ClarificationIntent, Is.EqualTo("diagnostic"));
        Assert.That(loaded.ClarificationOutput, Is.EqualTo("concise"));
        Assert.That(loaded.ClarificationScopes, Is.EqualTo(new[] { "active_doc", "selected_feature" }));
        Assert.That(loaded.ClarificationDiagnostics, Is.EqualTo(new[] { "tool_calls", "timings" }));
        Assert.That(loaded.ClarificationExpanded, Is.True);
    }

    [Test]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        string path = UiPreferences.GetPath();
        File.WriteAllText(path, "{ this is not json :::: ((( garbage");

        UiPreferences prefs = UiPreferences.Load();
        Assert.That(prefs.UiMode, Is.EqualTo("light"));
        Assert.That(prefs.ClarificationScopes, Is.Empty);
    }

    [Test]
    public void Save_NormalizesUnknownMode_ToLight()
    {
        var prefs = new UiPreferences { UiMode = "neon-rainbow" };
        prefs.Save();

        UiPreferences loaded = UiPreferences.Load();
        Assert.That(loaded.UiMode, Is.EqualTo("light"),
            "Unknown / freeform mode strings must normalize to 'light'.");
    }

    [Test]
    public void Save_PreservesValidModes_LightDarkSystem()
    {
        foreach (string mode in new[] { "light", "dark", "system" })
        {
            new UiPreferences { UiMode = mode }.Save();
            UiPreferences loaded = UiPreferences.Load();
            Assert.That(loaded.UiMode, Is.EqualTo(mode), $"Mode '{mode}' should round-trip unchanged.");
        }
    }
}
