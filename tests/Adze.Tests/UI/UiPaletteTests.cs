using System;
using System.Drawing;
using System.IO;
using Adze.Broker.Configuration;
using Adze.UI.V2;
using NUnit.Framework;

namespace Adze.Tests.UI;

/// <summary>
/// Tests for the dual-mode palette infrastructure (chunk 3 of v1.1 UI rebuild).
/// Verifies:
///   - Light and Dark token sets exist and disagree on identity colours
///   - <see cref="UiPalette.SetMode"/> persists to <see cref="UiPreferences"/>
///   - <see cref="UiPalette.ModeChanged"/> fires on mode swap
///   - <see cref="UiPalette.ParseMode"/> defaults to Light on unknown input
///
/// We restore the original mode in <see cref="TearDown"/> so the developer's
/// preference is preserved across the test run.
/// </summary>
[TestFixture]
public sealed class UiPaletteTests
{
    private UiPalette.UiMode _originalMode;
    private string _backupPath = string.Empty;
    private bool _hadOriginalPrefsFile;

    [SetUp]
    public void SetUp()
    {
        _originalMode = UiPalette.CurrentMode;
        string path = UiPreferences.GetPath();
        _backupPath = path + ".pal-testbackup-" + Guid.NewGuid().ToString("N");
        if (File.Exists(path))
        {
            File.Move(path, _backupPath);
            _hadOriginalPrefsFile = true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        // Restore mode in-memory (this also rewrites the prefs file).
        UiPalette.SetMode(_originalMode);
        // Then put the user's real prefs file back if there was one.
        string path = UiPreferences.GetPath();
        if (File.Exists(path)) File.Delete(path);
        if (_hadOriginalPrefsFile && File.Exists(_backupPath)) File.Move(_backupPath, path);
    }

    [Test]
    public void Light_And_Dark_Have_Distinct_Identity_Colours()
    {
        Assert.That(UiPalette.Light.Accent, Is.Not.EqualTo(UiPalette.Dark.Accent),
            "Accent tone differs between modes (lifted indigo on dark surfaces).");
        Assert.That(UiPalette.Light.SurfaceBackground, Is.Not.EqualTo(UiPalette.Dark.SurfaceBackground),
            "Backdrops differ.");
        Assert.That(UiPalette.Light.TextPrimary, Is.Not.EqualTo(UiPalette.Dark.TextPrimary),
            "Body text colour differs.");
    }

    [Test]
    public void DarkPalette_NotPureBlackBackground_ReducesEyeStrain()
    {
        Color bg = UiPalette.Dark.SurfaceBackground;
        Assert.That(bg.R + bg.G + bg.B, Is.GreaterThan(0),
            "Pure black (#000) is unacceptable per design brief — choose near-black.");
        Assert.That(bg.R + bg.G + bg.B, Is.LessThan(120),
            "Dark surface should be visibly dim, not mid-grey.");
    }

    [Test]
    public void SetMode_Dark_PersistsToPreferencesFile()
    {
        UiPalette.SetMode(UiPalette.UiMode.Dark);
        UiPreferences prefs = UiPreferences.Load();

        Assert.That(prefs.UiMode, Is.EqualTo("dark"));
        Assert.That(UiPalette.CurrentMode, Is.EqualTo(UiPalette.UiMode.Dark));
        Assert.That(UiPalette.Active.Accent, Is.EqualTo(UiPalette.Dark.Accent));
    }

    [Test]
    public void SetMode_FiresModeChangedEvent()
    {
        // Force a different starting mode so SetMode actually changes state.
        UiPalette.SetMode(UiPalette.UiMode.Light);

        int fires = 0;
        EventHandler handler = (_, _) => fires++;
        UiPalette.ModeChanged += handler;
        try
        {
            UiPalette.SetMode(UiPalette.UiMode.Dark);
        }
        finally
        {
            UiPalette.ModeChanged -= handler;
        }

        Assert.That(fires, Is.EqualTo(1), "ModeChanged must fire exactly once per SetMode call that changes state.");
    }

    [Test]
    public void ParseMode_UnknownInput_DefaultsToLight()
    {
        Assert.That(UiPalette.ParseMode(null), Is.EqualTo(UiPalette.UiMode.Light));
        Assert.That(UiPalette.ParseMode(""), Is.EqualTo(UiPalette.UiMode.Light));
        Assert.That(UiPalette.ParseMode("blueprint"), Is.EqualTo(UiPalette.UiMode.Light));
        Assert.That(UiPalette.ParseMode("DARK"), Is.EqualTo(UiPalette.UiMode.Dark),
            "Parse must be case-insensitive.");
        Assert.That(UiPalette.ParseMode(" system "), Is.EqualTo(UiPalette.UiMode.System),
            "Surrounding whitespace must be trimmed.");
    }

    [Test]
    public void ModeToString_RoundTripsThroughParseMode()
    {
        foreach (UiPalette.UiMode m in new[] { UiPalette.UiMode.Light, UiPalette.UiMode.Dark, UiPalette.UiMode.System })
        {
            string s = UiPalette.ModeToString(m);
            Assert.That(UiPalette.ParseMode(s), Is.EqualTo(m), $"Mode '{m}' must round-trip through ModeToString/ParseMode.");
        }
    }
}
