using System;
using System.Diagnostics;
using Adze.Broker.Configuration;
using Adze.Host.Infrastructure;

namespace Adze.Host.AddIn;

/// <summary>
/// R3.1 — pre-update eject coordinator. When the 3DX desktop updater
/// (<c>swxdesktopupdate.exe</c>) is running at disconnect time, SOLIDWORKS is
/// almost certainly closing in preparation for an update. Clear the persisted
/// "last verified" build so the next launch re-runs the compatibility probe
/// against whatever binaries the updater leaves behind.
///
/// Extracted from <see cref="AdzeAddIn"/> for unit testability — the
/// production <see cref="ProcessesByNameProvider"/> defers to
/// <see cref="Process.GetProcessesByName(string)"/>; tests swap in a fake.
/// </summary>
internal static class PreUpdateEjectService
{
    private const string UpdaterProcessName = "swxdesktopupdate";

    /// <summary>
    /// Test hook. Null in production = use <see cref="Process.GetProcessesByName(string)"/>.
    /// Tests set this to a deterministic factory.
    /// </summary>
    internal static Func<string, Process[]>? ProcessesByNameProvider;

    /// <summary>
    /// Test hook. Null in production = use
    /// <see cref="SwBuildStateService.ClearLastVerifiedBuild"/>. Tests capture invocation.
    /// </summary>
    internal static Action? ClearBuildStateOverride;

    /// <summary>
    /// True when the 3DX desktop updater process is running.
    /// </summary>
    public static bool IsUpdaterRunning()
    {
        var getter = ProcessesByNameProvider ?? Process.GetProcessesByName;
        var processes = getter(UpdaterProcessName);
        return processes != null && processes.Length > 0;
    }

    /// <summary>
    /// If the updater is running, clears the persisted SW build and logs a
    /// distinctive "Pre-update eject" line so post-incident log triage can
    /// distinguish planned ejects from unrelated disconnects. Failure is
    /// swallowed with an error log so disconnect never blocks on this.
    /// </summary>
    public static void RunIfNeeded()
    {
        try
        {
            if (!IsUpdaterRunning()) return;

            int? pid = null;
            try
            {
                var getter = ProcessesByNameProvider ?? Process.GetProcessesByName;
                var processes = getter(UpdaterProcessName);
                if (processes.Length > 0) pid = processes[0].Id;
            }
            catch
            {
                // Non-fatal: logging without a PID is fine.
            }

            string pidSuffix = pid.HasValue ? " (PID " + pid.Value + ")" : string.Empty;
            FileLogger.Info("Pre-update eject: swxdesktopupdate.exe detected" + pidSuffix +
                ". Clearing last-verified SW build so the next launch re-runs the compatibility probe.");

            (ClearBuildStateOverride ?? SwBuildStateService.ClearLastVerifiedBuild)();
        }
        catch (Exception ex)
        {
            FileLogger.Error("Pre-update eject check failed; continuing with normal disconnect.", ex);
        }
    }
}
