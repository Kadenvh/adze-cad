using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tools.Compute;
using NUnit.Framework;

namespace Adze.Tests.Tools;

[TestFixture]
public sealed class MeasureDistanceToolTests
{
    [Test]
    public void Execute_NoApplication_ReturnsFailure()
    {
        var tool = new MeasureDistanceTool();
        ToolResult result = tool.Execute(
            SessionContextFactory.CreateWithPart(),
            application: null,
            new MeasureDistanceParameters());

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("not connected").Or.Contain("application"));
    }

    [Test]
    public void Execute_NoDocument_ReturnsFailure()
    {
        var tool = new MeasureDistanceTool();
        ToolResult result = tool.Execute(
            SessionContextFactory.CreateMinimal(),
            application: new object(),
            new MeasureDistanceParameters());

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("active document").Or.Contain("Measurement failed"));
    }

    [Test]
    public void ToolName_MatchesContractConstant()
    {
        Assert.That(new MeasureDistanceTool().ToolName, Is.EqualTo(ToolNames.MeasureDistance));
    }
}
