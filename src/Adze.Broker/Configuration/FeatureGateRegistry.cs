using System;
using System.Collections.Generic;

namespace Adze.Broker.Configuration;

/// <summary>
/// Central registry of feature gates. Each gate is backed by an environment
/// variable name (kept stable so power users can override via env) but is
/// also readable and writable through a user-facing config file under
/// <c>%LOCALAPPDATA%\Adze\config.json</c> managed by
/// <see cref="FeatureGateConfigService"/>.
///
/// Resolution order for <see cref="IsEnabled"/>:
/// <list type="number">
///   <item>Environment variable (explicit override — wins if set to a truthy value).</item>
///   <item>Config file value (set via Settings panel or hand-edited).</item>
///   <item>Baked-in default (see <see cref="GetDefault"/>).</item>
/// </list>
/// </summary>
public static class FeatureGateRegistry
{
    public const string EnableModel = "SOLIDWORKS_AI_ENABLE_MODEL";
    public const string AgentLoop = "SOLIDWORKS_AI_AGENT_LOOP";
    public const string FirstWaveWrites = "SOLIDWORKS_AI_FIRST_WAVE_WRITES";
    public const string Retrieval = "SOLIDWORKS_AI_RETRIEVAL";
    public const string LocalModels = "SOLIDWORKS_AI_LOCAL_MODELS";
    public const string StreamFinalText = "SOLIDWORKS_AI_STREAM_FINAL_TEXT";
    public const string RibbonTab = "SOLIDWORKS_AI_RIBBON";
    public const string ContextMenu = "SOLIDWORKS_AI_CONTEXT_MENU";
    public const string ToastNotifications = "SOLIDWORKS_AI_TOAST";
    public const string PropertyManagerPageWrites = "SOLIDWORKS_AI_PMP_WRITES";

    /// <summary>
    /// v1.1 native WinForms sidebar (<c>Adze.UI.V2.NativeTaskPaneControl</c>).
    /// When ON, <c>AdzeAddIn.CreateTaskPane()</c> registers the new shim
    /// (<c>Adze.Host.NativeTaskPaneControl</c>) instead of the legacy
    /// WebBrowser-based <c>Adze.Host.TaskPaneControl</c>. Default: OFF — safety
    /// rail during the v1.1 cutover. Flip to ON via Settings or
    /// <c>setx SOLIDWORKS_AI_NATIVE_SIDEBAR true</c> once verified live.
    /// </summary>
    public const string NativeSidebar = "SOLIDWORKS_AI_NATIVE_SIDEBAR";

    private static readonly string[] AllGates =
    {
        EnableModel,
        AgentLoop,
        FirstWaveWrites,
        Retrieval,
        LocalModels,
        StreamFinalText,
        RibbonTab,
        ContextMenu,
        ToastNotifications,
        PropertyManagerPageWrites,
        NativeSidebar
    };

    /// <summary>Ordered list of every known gate name.</summary>
    public static IReadOnlyList<string> KnownGates => AllGates;

    // Cached config snapshot — reload-on-demand to avoid disk hit on every gate check.
    private static Dictionary<string, bool>? _cachedConfig;
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Returns the baked-in default for a known gate. Unknown gates default to false.
    /// Safe defaults for v1.0 zero-config first run:
    ///  - AI + tools + rendering surfaces ON (ribbon, model, agent loop, writes, streaming, retrieval)
    ///  - Opt-in surfaces OFF (context menu until R2 fix, toast, PMP writes, local models)
    ///
    /// Context menu defaults OFF pending R2026x interop resolution.
    /// SW 34.1.0.0140 changed an interop binary signature that ContextMenu.Register touches;
    /// the CompatibilityProbe now catches this at startup and skips the registration, but
    /// defaulting the gate off gives clean behavior even if the probe is ever bypassed.
    /// Flip to true via Settings or SOLIDWORKS_AI_CONTEXT_MENU=true on builds where the
    /// probe reports clean.
    /// </summary>
    public static bool GetDefault(string gateName)
    {
        return gateName switch
        {
            EnableModel => true,
            AgentLoop => true,
            FirstWaveWrites => true,
            Retrieval => true,
            LocalModels => false,
            StreamFinalText => true,
            RibbonTab => true,
            ContextMenu => false, // TEMPORARY: R2026x interop crash — re-enable after R2
            ToastNotifications => false,
            PropertyManagerPageWrites => false,
            // v1.1 native sidebar OFF by default — explicit opt-in during cutover so the
            // legacy TaskPaneControl remains the default fallback path. Flip ON via
            // Settings or env var once the new sidebar is verified live in SW.
            NativeSidebar => false,
            _ => false
        };
    }

    /// <summary>
    /// Resolves a gate through the env → config → default chain. Unknown
    /// gates return false (preserves legacy behavior for ad-hoc gate names
    /// callers might pass in).
    /// </summary>
    public static bool IsEnabled(string gateName)
    {
        if (string.IsNullOrWhiteSpace(gateName)) return false;

        // 1. Environment variable override.
        //
        // Read process env FIRST (inherited at process spawn), then fall back
        // to user-scope registry. Process env wins if set, but registry
        // catches the case where a user ran `setx GATE true` AFTER SW (or its
        // 3DEXPERIENCE Launcher parent) was already running — the process
        // inherited the old env at launch time and won't see the new value
        // until full process-tree restart. Registry lookup means setx +
        // SW-only relaunch is enough; killing the launcher is no longer
        // mandatory.
        string? envValue = Environment.GetEnvironmentVariable(gateName);
        if (string.IsNullOrWhiteSpace(envValue))
        {
            try
            {
                envValue = Environment.GetEnvironmentVariable(gateName, EnvironmentVariableTarget.User);
            }
            catch
            {
                // SecurityException on locked-down systems: keep going. Config
                // file or default will resolve below.
            }
        }
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            string normalized = envValue!.Trim().ToLowerInvariant();
            return normalized is "1" or "true" or "yes" or "on";
        }

        // 2. Config file (only for known gates — protects the
        // "IsEnabled_ReturnsFalseForUnsetGate" contract for ad-hoc names)
        if (Array.IndexOf(AllGates, gateName) >= 0)
        {
            Dictionary<string, bool> config = GetConfigSnapshot();
            if (config.TryGetValue(gateName, out bool fromConfig))
            {
                return fromConfig;
            }

            // 3. Baked-in default
            return GetDefault(gateName);
        }

        return false;
    }

    /// <summary>
    /// Returns the current effective state for every known gate.
    /// </summary>
    public static Dictionary<string, bool> GetAllStates()
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string gate in AllGates)
        {
            states[gate] = IsEnabled(gate);
        }

        return states;
    }

    /// <summary>
    /// Clears the in-memory config cache. Next IsEnabled call re-reads the file.
    /// Call this after writing via <see cref="FeatureGateConfigService.SetGate"/>.
    /// </summary>
    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedConfig = null;
        }
    }

    public static string FormatSummary()
    {
        var parts = new List<string>();
        foreach (string gate in AllGates)
        {
            string shortName = gate.Replace("SOLIDWORKS_AI_", "");
            parts.Add(shortName + "=" + (IsEnabled(gate) ? "on" : "off"));
        }

        return string.Join(" | ", parts);
    }

    private static Dictionary<string, bool> GetConfigSnapshot()
    {
        lock (_cacheLock)
        {
            if (_cachedConfig != null) return _cachedConfig;
            _cachedConfig = FeatureGateConfigService.Load();
            return _cachedConfig;
        }
    }
}
