using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tools.Compute;
using NUnit.Framework;

namespace Adze.Tests.Tools;

/// <summary>
/// Mass properties is a compute tool that requires live SW COM. Unit tests
/// cover the no-application and no-document fallback paths; the
/// happy-path COM execution is exercised by live host validation.
/// </summary>
[TestFixture]
public sealed class GetMassPropertiesToolTests
{
    [Test]
    public void Execute_NoApplication_ReturnsFailure()
    {
        var tool = new GetMassPropertiesTool();
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = tool.Execute(context, application: null, new GetMassPropertiesParameters());

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("not connected").Or.Contain("application"));
    }

    [Test]
    public void Execute_NoDocument_ReturnsFailure()
    {
        var tool = new GetMassPropertiesTool();
        SessionContext context = SessionContextFactory.CreateMinimal();

        // Pass a non-null sentinel for application so the no-doc path is exercised.
        ToolResult result = tool.Execute(context, application: new object(), new GetMassPropertiesParameters());

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("active document").Or.Contain("Mass properties failed"));
    }

    [Test]
    public void ToolName_MatchesContractConstant()
    {
        Assert.That(new GetMassPropertiesTool().ToolName, Is.EqualTo(ToolNames.GetMassProperties));
    }
}
