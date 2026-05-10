using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tools.Write;
using NUnit.Framework;

namespace Adze.Tests.Tools;

[TestFixture]
public sealed class CreateDrawingDocumentToolTests
{
    [Test]
    public void Preview_NoModelPathAndNoActiveDoc_SurfacesWarning()
    {
        var tool = new CreateDrawingDocumentTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateMinimal(),
            new CreateDrawingDocumentParameters());

        Assert.That(preview.Warnings, Has.Some.Contains("model_path"));
    }

    [Test]
    public void Preview_ActivePart_DefaultsToActiveDocument()
    {
        var tool = new CreateDrawingDocumentTool();
        SessionContext context = SessionContextFactory.CreateWithPart(
            title: "MyPart",
            path: @"C:\models\MyPart.SLDPRT");
        WritePreview preview = tool.Preview(context, new CreateDrawingDocumentParameters());

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreateDrawingDocument));
        Assert.That(preview.Summary, Does.Contain("MyPart"));
        Assert.That(preview.Summary, Does.Contain("standard views"));
    }

    [Test]
    public void Preview_StandardViewsDisabled_BlankDrawing()
    {
        var tool = new CreateDrawingDocumentTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart("MyPart"),
            new CreateDrawingDocumentParameters { IncludeStandardViews = false });

        Assert.That(preview.Summary, Does.Contain("blank"));
    }

    [Test]
    public void Preview_ExplicitModelPath_UsesProvidedPath()
    {
        var tool = new CreateDrawingDocumentTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateMinimal(),
            new CreateDrawingDocumentParameters { ModelPath = "C:\\models\\bracket.SLDPRT" });

        Assert.That(preview.Summary, Does.Contain("bracket"));
    }

    [Test]
    public void BuildUndoLabel_IncludesModelName()
    {
        var tool = new CreateDrawingDocumentTool();
        string label = tool.BuildUndoLabel(new CreateDrawingDocumentParameters { ModelPath = "C:\\models\\widget.SLDPRT" });

        Assert.That(label, Does.Contain("create drawing"));
        Assert.That(label, Does.Contain("widget.SLDPRT"));
    }
}
