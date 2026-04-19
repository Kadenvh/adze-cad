using System.Diagnostics;
using Adze.Host.AddIn;
using Adze.Host.Infrastructure;
using NUnit.Framework;

namespace Adze.Tests.Host;

/// <summary>
/// Verifies <see cref="HostState.SetProbeResult(bool, string?, string?, string?)"/>
/// and <see cref="HostState.GetProbeFailure"/> round-trip cleanly and handle
/// the two meaningful transitions (success → banner-null; failure → banner-populated).
/// </summary>
[TestFixture]
public class HostProbeStateTests
{
    [TearDown]
    public void Reset()
    {
        // Other fixtures in the suite may rely on a clean HostState probe slot.
        HostState.SetProbeResult(true, null, null, null);
    }

    [Test]
    public void CompatibleProbe_ClearsFailureMessage()
    {
        HostState.SetProbeResult(false, "create-command-group", "CreateCommandGroup2 returned null.", "34.1.0.0140");
        HostState.SetProbeResult(true, null, null, "34.1.0.0140");

        var (message, failedStep, revision) = HostState.GetProbeFailure();
        Assert.That(message, Is.Null);
        Assert.That(failedStep, Is.Null);
        Assert.That(revision, Is.EqualTo("34.1.0.0140"));
    }

    [Test]
    public void IncompatibleProbe_ExposesFailureDetail()
    {
        HostState.SetProbeResult(false, "create-command-group", "CreateCommandGroup2 returned null.", "34.1.0.0140");

        var (message, failedStep, revision) = HostState.GetProbeFailure();
        Assert.That(message, Does.Contain("CreateCommandGroup2"));
        Assert.That(failedStep, Is.EqualTo("create-command-group"));
        Assert.That(revision, Is.EqualTo("34.1.0.0140"));
    }

    [Test]
    public void CompatibleProbeWithoutMessage_YieldsNoBanner()
    {
        HostState.SetProbeResult(true, null, null, "34.1.0.0140");

        var (message, _, _) = HostState.GetProbeFailure();
        Assert.That(message, Is.Null, "compatible probe must not surface a banner message");
    }

    [Test]
    public void RevisionPreserved_AcrossFailureThenSuccess()
    {
        HostState.SetProbeResult(false, "create-command-group", "broken", "34.1.0.0140");
        HostState.SetProbeResult(true, null, null, "34.1.0.0141");

        var (_, _, revision) = HostState.GetProbeFailure();
        Assert.That(revision, Is.EqualTo("34.1.0.0141"));
    }
}
