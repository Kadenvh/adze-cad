using System;
using System.Runtime.ExceptionServices;
using System.Security;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Adze.Host.Infrastructure;

namespace Adze.Host.AddIn;

/// <summary>
/// Runs a read-only smoke test against the live SOLIDWORKS COM surface at
/// <c>ConnectToSW</c> time to detect interop binary-signature breakage
/// introduced by SW desktop updates (e.g. R2026x 34.1.0.0140 April 2026).
///
/// The probe creates a benign, hidden command group via <c>ICommandManager.AddCommandGroup2</c>
/// and immediately removes it with <c>RemoveCommandGroup2</c>. If any step throws — either
/// a managed exception or a native corrupted-state exception (AccessViolationException
/// and friends) — the probe returns a negative result and callers skip context-menu /
/// ribbon registration so the add-in loads without crashing SOLIDWORKS.
///
/// Corrupted-state exceptions are caught because <see cref="Check"/> is decorated with
/// <see cref="HandleProcessCorruptedStateExceptionsAttribute"/>; on .NET Framework 4.5+
/// this routes CSEs to the method's own catch clauses instead of bubbling to the CLR
/// default "process-dies" handler.
/// </summary>
internal static class CompatibilityProbe
{
    private const int ProbeUserId = 0xA03F;
    private const string ProbeTitle = "Adze Compatibility Probe";

    /// <summary>
    /// Runs the probe. Returns a structured result with the exact step that
    /// failed if the probe determined the surface is not compatible.
    /// </summary>
    [HandleProcessCorruptedStateExceptions]
    [SecurityCritical]
    public static CompatibilityProbeResult Check(ISldWorks application, int cookie)
    {
        if (application == null)
        {
            return new CompatibilityProbeResult(false, "Application handle is null.", "init");
        }

        // Step 1: read-only version fetch. If this throws we've got much bigger problems than context menus.
        string revisionNumber;
        try
        {
            revisionNumber = application.RevisionNumber() ?? "(null)";
        }
        catch (Exception ex)
        {
            FileLogger.Error("CompatibilityProbe: RevisionNumber() threw.", ex);
            return new CompatibilityProbeResult(false, "RevisionNumber failed: " + ex.Message, "revision");
        }

        // Step 2: acquire the command manager. This is the first interop call ContextMenu.Register makes.
        ICommandManager? cmdMgr;
        try
        {
            cmdMgr = application.GetCommandManager(cookie);
            if (cmdMgr == null)
            {
                return new CompatibilityProbeResult(false, "GetCommandManager returned null.", "get-command-manager");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("CompatibilityProbe: GetCommandManager threw.", ex);
            return new CompatibilityProbeResult(false, "GetCommandManager failed: " + ex.Message, "get-command-manager");
        }

        // Step 3: create and immediately remove a benign group. Exercises the same
        // entry points ContextMenu and RibbonTab use without leaving UI state behind.
        int errors = 0;
        ICommandGroup? probeGroup = null;
        try
        {
            probeGroup = cmdMgr.CreateCommandGroup2(
                ProbeUserId,
                ProbeTitle,
                ProbeTitle,
                ProbeTitle,
                -1,
                false,
                ref errors);
            if (probeGroup == null || errors != 0)
            {
                return new CompatibilityProbeResult(false,
                    "CreateCommandGroup2 returned null or error=" + errors + ".",
                    "create-command-group");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("CompatibilityProbe: CreateCommandGroup2 threw.", ex);
            return new CompatibilityProbeResult(false,
                "CreateCommandGroup2 failed: " + ex.Message, "create-command-group");
        }

        // Step 4: tear it down cleanly. If RemoveCommandGroup2 throws we still
        // consider the probe "positive overall" because the risky calls succeeded;
        // the cleanup failure is logged but not fatal.
        try
        {
            cmdMgr.RemoveCommandGroup2(ProbeUserId, true);
        }
        catch (Exception ex)
        {
            FileLogger.Info("CompatibilityProbe: RemoveCommandGroup2 threw during cleanup (non-fatal): " + ex.Message);
        }

        FileLogger.Info("CompatibilityProbe: OK. SW revision=" + revisionNumber);
        return new CompatibilityProbeResult(true, "Compatible.", "ok", revisionNumber);
    }
}

/// <summary>
/// Typed result of <see cref="CompatibilityProbe.Check"/>. Callers gate mutable
/// SW surface registration on <see cref="IsCompatible"/>.
/// </summary>
internal sealed class CompatibilityProbeResult
{
    public bool IsCompatible { get; }
    public string Message { get; }
    public string FailedStep { get; }
    public string RevisionNumber { get; }

    public CompatibilityProbeResult(bool isCompatible, string message, string failedStep, string revisionNumber = "")
    {
        IsCompatible = isCompatible;
        Message = message ?? string.Empty;
        FailedStep = failedStep ?? string.Empty;
        RevisionNumber = revisionNumber ?? string.Empty;
    }
}
