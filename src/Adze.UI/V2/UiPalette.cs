using System;
using System.Drawing;
using Adze.Broker.Configuration;

namespace Adze.UI.V2;

/// <summary>
/// v1.1 visual identity tokens. Dual-mode (light/dark/system) added in chunk 3.
///
/// Design:
///   - <see cref="UiPaletteTokens"/> is a value-holding class with one field per
///     token. Two singletons: <see cref="Light"/> and <see cref="Dark"/>.
///   - <see cref="Active"/> returns whichever token set the user has selected.
///   - Every legacy <c>UiPalette.Foo</c> static accessor is preserved as a
///     read-through to <see cref="Active"/>. Call sites stay one-liner.
///   - <see cref="ModeChanged"/> fires whenever <see cref="SetMode"/> swaps.
///     Painted controls subscribe in <c>OnHandleCreated</c> and
///     <c>Invalidate()</c> on the event so a mode flip immediately recolors.
///
/// Persistence: <see cref="UiPreferences"/> stores the chosen mode under
/// <c>%LOCALAPPDATA%\Adze\ui-prefs.json</c>. The static initializer reads it
/// once at first access; later <see cref="SetMode"/> calls also persist.
///
/// "System" mode tracks Windows' <c>AppsUseLightTheme</c> registry value
/// (1 → light, 0 → dark, missing → light). We don't subscribe to OS theme-
/// change broadcasts; users get the OS theme at app startup. Re-launch picks
/// up subsequent OS theme changes. Live OS-theme tracking is a v1.2 item.
/// </summary>
public static class UiPalette
{
    /// <summary>Discrete UI mode the user has selected.</summary>
    public enum UiMode
    {
        Light,
        Dark,
        System
    }

    private static UiPaletteTokens _active;
    private static UiMode _currentMode;

    /// <summary>Fired after <see cref="SetMode"/> mutates <see cref="Active"/>.</summary>
    public static event EventHandler? ModeChanged;

    /// <summary>The bright/light token set.</summary>
    public static readonly UiPaletteTokens Light = BuildLight();

    /// <summary>The dim/dark token set — chosen indigo lifted in luminance for AA contrast on dark surfaces.</summary>
    public static readonly UiPaletteTokens Dark = BuildDark();

    /// <summary>The currently active token set. Use this for new code; legacy accessors below already read through.</summary>
    public static UiPaletteTokens Active => _active;

    /// <summary>The currently selected mode (Light / Dark / System).</summary>
    public static UiMode CurrentMode => _currentMode;

    static UiPalette()
    {
        // Defer loading prefs to a try block — we never want palette init to
        // throw, since every UI control reads from this class on construction.
        try
        {
            UiPreferences prefs = UiPreferences.Load();
            UiMode mode = ParseMode(prefs.UiMode);
            _currentMode = mode;
            _active = ResolveTokens(mode);
        }
        catch
        {
            _currentMode = UiMode.Light;
            _active = Light;
        }
    }

    /// <summary>Set the active mode. Persists to <see cref="UiPreferences"/> and fires <see cref="ModeChanged"/>.</summary>
    public static void SetMode(UiMode mode)
    {
        UiPaletteTokens next = ResolveTokens(mode);
        bool changed = !ReferenceEquals(next, _active) || mode != _currentMode;
        _active = next;
        _currentMode = mode;

        try
        {
            UiPreferences prefs = UiPreferences.Load();
            prefs.UiMode = ModeToString(mode);
            prefs.Save();
        }
        catch
        {
            // Best-effort persistence; the runtime swap still happens.
        }

        if (changed)
        {
            try { ModeChanged?.Invoke(null, EventArgs.Empty); }
            catch { /* one bad subscriber must not poison the rest */ }
        }
    }

    /// <summary>Parses a mode string from prefs ("light"/"dark"/"system"). Defaults to Light.</summary>
    public static UiMode ParseMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return UiMode.Light;
        string m = mode!.Trim().ToLowerInvariant();
        return m switch
        {
            "dark" => UiMode.Dark,
            "system" => UiMode.System,
            _ => UiMode.Light
        };
    }

    /// <summary>Inverse of <see cref="ParseMode"/>.</summary>
    public static string ModeToString(UiMode mode) => mode switch
    {
        UiMode.Dark => "dark",
        UiMode.System => "system",
        _ => "light"
    };

    private static UiPaletteTokens ResolveTokens(UiMode mode)
    {
        return mode switch
        {
            UiMode.Dark => Dark,
            UiMode.System => DetectSystemPrefersDark() ? Dark : Light,
            _ => Light
        };
    }

    /// <summary>
    /// Reads <c>HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>:
    /// 0 → dark, 1 → light, missing/error → light.
    /// </summary>
    private static bool DetectSystemPrefersDark()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key == null) return false;
            object? value = key.GetValue("AppsUseLightTheme");
            if (value is int i) return i == 0;
        }
        catch
        {
            // Sandboxed / non-Windows / locked-down — assume light.
        }
        return false;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Token builders
    // ──────────────────────────────────────────────────────────────────────

    private static UiPaletteTokens BuildLight() => new()
    {
        // Backdrops
        SurfaceBackground = Color.FromArgb(250, 251, 253),
        CardBackground = Color.White,
        CardBorder = Color.FromArgb(228, 232, 240),
        HeaderBackground = Color.White,
        HeaderForeground = Color.FromArgb(17, 24, 39),
        HeaderBorder = Color.FromArgb(228, 232, 240),
        // Type
        TextPrimary = Color.FromArgb(17, 24, 39),
        TextSecondary = Color.FromArgb(107, 114, 128),
        // Accent
        Accent = Color.FromArgb(79, 70, 229),
        AccentDark = Color.FromArgb(67, 56, 202),
        AccentTint = Color.FromArgb(238, 236, 253),
        AccentForeground = Color.White,
        // Bubbles
        UserBubbleBackground = Color.FromArgb(79, 70, 229),
        UserBubbleForeground = Color.White,
        UserBubbleBorder = Color.FromArgb(67, 56, 202),
        AssistantBubbleBackground = Color.White,
        AssistantBubbleForeground = Color.FromArgb(17, 24, 39),
        AssistantBubbleBorder = Color.FromArgb(228, 232, 240),
        SystemBubbleBackground = Color.FromArgb(254, 252, 232),
        SystemBubbleForeground = Color.FromArgb(120, 80, 12),
        SystemBubbleBorder = Color.FromArgb(245, 230, 180),
        // Banners
        BannerWarningBackground = Color.FromArgb(254, 252, 232),
        BannerWarningForeground = Color.FromArgb(120, 80, 12),
        BannerWarningAccent = Color.FromArgb(245, 158, 11),
        BannerErrorBackground = Color.FromArgb(254, 242, 242),
        BannerErrorForeground = Color.FromArgb(127, 29, 29),
        BannerErrorAccent = Color.FromArgb(220, 38, 38),
        BannerSuccessBackground = Color.FromArgb(240, 253, 244),
        BannerSuccessForeground = Color.FromArgb(20, 83, 45),
        BannerSuccessAccent = Color.FromArgb(22, 163, 74),
        // Write cards
        WriteCardBackground = Color.White,
        WriteCardBorder = Color.FromArgb(228, 232, 240),
        WriteCardElevatedBorder = Color.FromArgb(245, 158, 11),
        WriteCardTagBackground = Color.FromArgb(238, 236, 253),
        WriteCardTagForeground = Color.FromArgb(67, 56, 202),
        DiffAddedBackground = Color.FromArgb(220, 252, 231),
        DiffAddedForeground = Color.FromArgb(20, 83, 45),
        DiffRemovedBackground = Color.FromArgb(254, 226, 226),
        DiffRemovedForeground = Color.FromArgb(127, 29, 29),
        // Subtle button
        SubtleButtonBackground = Color.FromArgb(243, 244, 246),
        SubtleButtonForeground = Color.FromArgb(55, 65, 81),
        SubtleButtonBorder = Color.FromArgb(228, 232, 240),
        SubtleButtonHover = Color.FromArgb(229, 231, 235),
        // Input
        InputBackground = Color.White,
        InputBorder = Color.FromArgb(209, 213, 219),
        InputBorderFocused = Color.FromArgb(79, 70, 229),
        // Tabs
        TabStripBackground = Color.FromArgb(250, 251, 253),
        TabInactiveForeground = Color.FromArgb(107, 114, 128),
        TabActiveForeground = Color.FromArgb(79, 70, 229),
        TabActiveBackground = Color.White,
        TabIndicator = Color.FromArgb(79, 70, 229),
    };

    private static UiPaletteTokens BuildDark() => new()
    {
        // Backdrops — near-black with a hint of blue, NOT pure black to reduce eye strain.
        SurfaceBackground = Color.FromArgb(15, 18, 26),
        CardBackground = Color.FromArgb(22, 26, 36),
        CardBorder = Color.FromArgb(38, 44, 58),
        HeaderBackground = Color.FromArgb(22, 26, 36),
        HeaderForeground = Color.FromArgb(229, 231, 235),
        HeaderBorder = Color.FromArgb(38, 44, 58),
        // Type
        TextPrimary = Color.FromArgb(229, 231, 235),
        TextSecondary = Color.FromArgb(148, 156, 173),
        // Accent — same indigo identity, lifted in luminance for readability on dark surfaces.
        Accent = Color.FromArgb(129, 121, 240),
        AccentDark = Color.FromArgb(99, 92, 232),
        AccentTint = Color.FromArgb(40, 38, 70),
        AccentForeground = Color.White,
        // Bubbles
        UserBubbleBackground = Color.FromArgb(99, 92, 232),
        UserBubbleForeground = Color.White,
        UserBubbleBorder = Color.FromArgb(79, 70, 229),
        AssistantBubbleBackground = Color.FromArgb(22, 26, 36),
        AssistantBubbleForeground = Color.FromArgb(229, 231, 235),
        AssistantBubbleBorder = Color.FromArgb(48, 56, 74),
        SystemBubbleBackground = Color.FromArgb(48, 38, 14),
        SystemBubbleForeground = Color.FromArgb(251, 191, 36),
        SystemBubbleBorder = Color.FromArgb(91, 67, 18),
        // Banners — saturated tone over dark base, AA-readable foreground.
        BannerWarningBackground = Color.FromArgb(48, 38, 14),
        BannerWarningForeground = Color.FromArgb(251, 191, 36),
        BannerWarningAccent = Color.FromArgb(251, 191, 36),
        BannerErrorBackground = Color.FromArgb(48, 18, 18),
        BannerErrorForeground = Color.FromArgb(248, 113, 113),
        BannerErrorAccent = Color.FromArgb(248, 113, 113),
        BannerSuccessBackground = Color.FromArgb(14, 38, 22),
        BannerSuccessForeground = Color.FromArgb(74, 222, 128),
        BannerSuccessAccent = Color.FromArgb(74, 222, 128),
        // Write cards
        WriteCardBackground = Color.FromArgb(22, 26, 36),
        WriteCardBorder = Color.FromArgb(38, 44, 58),
        WriteCardElevatedBorder = Color.FromArgb(251, 191, 36),
        WriteCardTagBackground = Color.FromArgb(40, 38, 70),
        WriteCardTagForeground = Color.FromArgb(165, 158, 247),
        DiffAddedBackground = Color.FromArgb(14, 38, 22),
        DiffAddedForeground = Color.FromArgb(74, 222, 128),
        DiffRemovedBackground = Color.FromArgb(48, 18, 18),
        DiffRemovedForeground = Color.FromArgb(248, 113, 113),
        // Subtle button
        SubtleButtonBackground = Color.FromArgb(32, 38, 50),
        SubtleButtonForeground = Color.FromArgb(208, 214, 226),
        SubtleButtonBorder = Color.FromArgb(48, 56, 74),
        SubtleButtonHover = Color.FromArgb(44, 52, 68),
        // Input
        InputBackground = Color.FromArgb(22, 26, 36),
        InputBorder = Color.FromArgb(48, 56, 74),
        InputBorderFocused = Color.FromArgb(129, 121, 240),
        // Tabs
        TabStripBackground = Color.FromArgb(15, 18, 26),
        TabInactiveForeground = Color.FromArgb(148, 156, 173),
        TabActiveForeground = Color.FromArgb(165, 158, 247),
        TabActiveBackground = Color.FromArgb(22, 26, 36),
        TabIndicator = Color.FromArgb(129, 121, 240),
    };

    // ──────────────────────────────────────────────────────────────────────
    // Legacy read-through accessors. Every existing call site keeps working;
    // each property reads the current token set via `Active`.
    // ──────────────────────────────────────────────────────────────────────

    public static Color SurfaceBackground => _active.SurfaceBackground;
    public static Color CardBackground => _active.CardBackground;
    public static Color CardBorder => _active.CardBorder;
    public static Color HeaderBackground => _active.HeaderBackground;
    public static Color HeaderForeground => _active.HeaderForeground;
    public static Color HeaderBorder => _active.HeaderBorder;
    public static Color TextPrimary => _active.TextPrimary;
    public static Color TextSecondary => _active.TextSecondary;
    public static Color Accent => _active.Accent;
    public static Color AccentDark => _active.AccentDark;
    public static Color AccentTint => _active.AccentTint;
    public static Color AccentForeground => _active.AccentForeground;
    public static Color UserBubbleBackground => _active.UserBubbleBackground;
    public static Color UserBubbleForeground => _active.UserBubbleForeground;
    public static Color UserBubbleBorder => _active.UserBubbleBorder;
    public static Color AssistantBubbleBackground => _active.AssistantBubbleBackground;
    public static Color AssistantBubbleForeground => _active.AssistantBubbleForeground;
    public static Color AssistantBubbleBorder => _active.AssistantBubbleBorder;
    public static Color SystemBubbleBackground => _active.SystemBubbleBackground;
    public static Color SystemBubbleForeground => _active.SystemBubbleForeground;
    public static Color SystemBubbleBorder => _active.SystemBubbleBorder;
    public static Color BannerWarningBackground => _active.BannerWarningBackground;
    public static Color BannerWarningForeground => _active.BannerWarningForeground;
    public static Color BannerWarningAccent => _active.BannerWarningAccent;
    public static Color BannerErrorBackground => _active.BannerErrorBackground;
    public static Color BannerErrorForeground => _active.BannerErrorForeground;
    public static Color BannerErrorAccent => _active.BannerErrorAccent;
    public static Color BannerSuccessBackground => _active.BannerSuccessBackground;
    public static Color BannerSuccessForeground => _active.BannerSuccessForeground;
    public static Color BannerSuccessAccent => _active.BannerSuccessAccent;
    public static Color WriteCardBackground => _active.WriteCardBackground;
    public static Color WriteCardBorder => _active.WriteCardBorder;
    public static Color WriteCardElevatedBorder => _active.WriteCardElevatedBorder;
    public static Color WriteCardTagBackground => _active.WriteCardTagBackground;
    public static Color WriteCardTagForeground => _active.WriteCardTagForeground;
    public static Color DiffAddedBackground => _active.DiffAddedBackground;
    public static Color DiffAddedForeground => _active.DiffAddedForeground;
    public static Color DiffRemovedBackground => _active.DiffRemovedBackground;
    public static Color DiffRemovedForeground => _active.DiffRemovedForeground;
    public static Color SubtleButtonBackground => _active.SubtleButtonBackground;
    public static Color SubtleButtonForeground => _active.SubtleButtonForeground;
    public static Color SubtleButtonBorder => _active.SubtleButtonBorder;
    public static Color SubtleButtonHover => _active.SubtleButtonHover;
    public static Color InputBackground => _active.InputBackground;
    public static Color InputBorder => _active.InputBorder;
    public static Color InputBorderFocused => _active.InputBorderFocused;
    public static Color TabStripBackground => _active.TabStripBackground;
    public static Color TabInactiveForeground => _active.TabInactiveForeground;
    public static Color TabActiveForeground => _active.TabActiveForeground;
    public static Color TabActiveBackground => _active.TabActiveBackground;
    public static Color TabIndicator => _active.TabIndicator;

    // ──────────────────────────────────────────────────────────────────────
    // Typography (mode-invariant — same fonts/sizes in light and dark).
    // ──────────────────────────────────────────────────────────────────────

    public const string FontFamily = "Segoe UI";
    public const string MonoFontFamily = "Consolas";

    /// <summary>Default body text — bumped from 9pt for legibility.</summary>
    public const float BodyFontSize = 10F;

    /// <summary>Assistant chat body — slightly larger for comfortable read.</summary>
    public const float ChatBodyFontSize = 10.5F;

    /// <summary>Section headers (Write Plan, Provider, etc).</summary>
    public const float HeaderFontSize = 11F;

    /// <summary>Document title in the top header strip.</summary>
    public const float DocHeaderFontSize = 12F;

    /// <summary>Footer / timestamp / hint text.</summary>
    public const float FooterFontSize = 8.25F;

    /// <summary>Quick-action chip text.</summary>
    public const float ChipFontSize = 9F;

    /// <summary>Buttons.</summary>
    public const float ButtonFontSize = 9.5F;
}

/// <summary>
/// One mode's worth of tokens. Pure data — no behaviour. Two singletons live
/// on <see cref="UiPalette"/>: <see cref="UiPalette.Light"/> and
/// <see cref="UiPalette.Dark"/>.
/// </summary>
public sealed class UiPaletteTokens
{
    public Color SurfaceBackground { get; set; }
    public Color CardBackground { get; set; }
    public Color CardBorder { get; set; }
    public Color HeaderBackground { get; set; }
    public Color HeaderForeground { get; set; }
    public Color HeaderBorder { get; set; }
    public Color TextPrimary { get; set; }
    public Color TextSecondary { get; set; }
    public Color Accent { get; set; }
    public Color AccentDark { get; set; }
    public Color AccentTint { get; set; }
    public Color AccentForeground { get; set; }
    public Color UserBubbleBackground { get; set; }
    public Color UserBubbleForeground { get; set; }
    public Color UserBubbleBorder { get; set; }
    public Color AssistantBubbleBackground { get; set; }
    public Color AssistantBubbleForeground { get; set; }
    public Color AssistantBubbleBorder { get; set; }
    public Color SystemBubbleBackground { get; set; }
    public Color SystemBubbleForeground { get; set; }
    public Color SystemBubbleBorder { get; set; }
    public Color BannerWarningBackground { get; set; }
    public Color BannerWarningForeground { get; set; }
    public Color BannerWarningAccent { get; set; }
    public Color BannerErrorBackground { get; set; }
    public Color BannerErrorForeground { get; set; }
    public Color BannerErrorAccent { get; set; }
    public Color BannerSuccessBackground { get; set; }
    public Color BannerSuccessForeground { get; set; }
    public Color BannerSuccessAccent { get; set; }
    public Color WriteCardBackground { get; set; }
    public Color WriteCardBorder { get; set; }
    public Color WriteCardElevatedBorder { get; set; }
    public Color WriteCardTagBackground { get; set; }
    public Color WriteCardTagForeground { get; set; }
    public Color DiffAddedBackground { get; set; }
    public Color DiffAddedForeground { get; set; }
    public Color DiffRemovedBackground { get; set; }
    public Color DiffRemovedForeground { get; set; }
    public Color SubtleButtonBackground { get; set; }
    public Color SubtleButtonForeground { get; set; }
    public Color SubtleButtonBorder { get; set; }
    public Color SubtleButtonHover { get; set; }
    public Color InputBackground { get; set; }
    public Color InputBorder { get; set; }
    public Color InputBorderFocused { get; set; }
    public Color TabStripBackground { get; set; }
    public Color TabInactiveForeground { get; set; }
    public Color TabActiveForeground { get; set; }
    public Color TabActiveBackground { get; set; }
    public Color TabIndicator { get; set; }
}
