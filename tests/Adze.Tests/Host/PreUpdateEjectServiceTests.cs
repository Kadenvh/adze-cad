using System;
using System.Diagnostics;
using Adze.Host.AddIn;
using NUnit.Framework;

namespace Adze.Tests.Host;

/// <summary>
/// Verifies R3.1 <see cref="PreUpdateEjectService"/> detection + clear-state
/// behavior. Uses the internal test hooks on the service (injectable
/// <c>ProcessesByNameProvider</c> and <c>ClearBuildStateOverride</c>) so the
/// real <c>Process.GetProcessesByName</c> and <c>SwBuildStateService</c> are
/// never exercised by unit tests.
/// </summary>
[TestFixture]
public class PreUpdateEjectServiceTests
{
    [TearDown]
    public void Reset()
    {
        PreUpdateEjectService.ProcessesByNameProvider = null;
        PreUpdateEjectService.ClearBuildStateOverride = null;
    }

    [Test]
    public void IsUpdaterRunning_ReturnsFalse_WhenNoMatchingProcess()
    {
        PreUpdateEjectService.ProcessesByNameProvider = _ => Array.Empty<Process>();

        Assert.That(PreUpdateEjectService.IsUpdaterRunning(), Is.False);
    }

    [Test]
    public void IsUpdaterRunning_ReturnsTrue_WhenProcessListNonEmpty()
    {
        PreUpdateEjectService.ProcessesByNameProvider = _ => new[] { Process.GetCurrentProcess() };

        Assert.That(PreUpdateEjectService.IsUpdaterRunning(), Is.True);
    }

    [Test]
    public void RunIfNeeded_DoesNotClearState_WhenUpdaterAbsent()
    {
        int clearCount = 0;
        PreUpdateEjectService.ProcessesByNameProvider = _ => Array.Empty<Process>();
        PreUpdateEjectService.ClearBuildStateOverride = () => clearCount++;

        PreUpdateEjectService.RunIfNeeded();

        Assert.That(clearCount, Is.EqualTo(0));
    }

    [Test]
    public void RunIfNeeded_ClearsState_WhenUpdaterPresent()
    {
        int clearCount = 0;
        PreUpdateEjectService.ProcessesByNameProvider = _ => new[] { Process.GetCurrentProcess() };
        PreUpdateEjectService.ClearBuildStateOverride = () => clearCount++;

        PreUpdateEjectService.RunIfNeeded();

        Assert.That(clearCount, Is.EqualTo(1));
    }

    [Test]
    public void RunIfNeeded_SwallowsExceptions_SoDisconnectNeverBlocks()
    {
        // Production behavior contract: disconnect must never blow up on a
        // transient WMI / process-enumeration failure.
        PreUpdateEjectService.ProcessesByNameProvider = _ => throw new InvalidOperationException("WMI transient failure");

        Assert.DoesNotThrow(() => PreUpdateEjectService.RunIfNeeded());
    }

    [Test]
    public void RunIfNeeded_DoesNotCallClearState_WhenProcessLookupFails()
    {
        int clearCount = 0;
        PreUpdateEjectService.ProcessesByNameProvider = _ => throw new InvalidOperationException("fail");
        PreUpdateEjectService.ClearBuildStateOverride = () => clearCount++;

        PreUpdateEjectService.RunIfNeeded();

        Assert.That(clearCount, Is.EqualTo(0));
    }
}
