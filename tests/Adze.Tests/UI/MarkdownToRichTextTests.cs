using System.Collections.Generic;
using System.Linq;
using Adze.UI.Rendering;
using NUnit.Framework;

namespace Adze.Tests.UI;

[TestFixture]
public sealed class MarkdownToRichTextTests
{
    [Test]
    public void Parse_PlainParagraph_EmitsSingleTextRun()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output =
            MarkdownToRichText.Parse("Hello world.");

        var textRuns = output.Where(r => r.Kind == MarkdownToRichText.RunKind.Text).ToList();
        Assert.That(textRuns, Has.Count.EqualTo(1));
        Assert.That(textRuns[0].Text, Is.EqualTo("Hello world."));
        Assert.That(textRuns[0].Bold, Is.False);
        Assert.That(textRuns[0].Code, Is.False);
        Assert.That(textRuns[0].Header, Is.EqualTo(MarkdownToRichText.HeaderStyle.None));
    }

    [Test]
    public void Parse_H1Heading_TagsRunAsH1()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output =
            MarkdownToRichText.Parse("# Big title");

        var heading = output.First(r => r.Kind == MarkdownToRichText.RunKind.Text);
        Assert.That(heading.Text, Is.EqualTo("Big title"));
        Assert.That(heading.Header, Is.EqualTo(MarkdownToRichText.HeaderStyle.H1));
    }

    [Test]
    public void Parse_H2AndH3_TagRunsCorrectly()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> h2 = MarkdownToRichText.Parse("## Subtitle");
        IReadOnlyList<MarkdownToRichText.RenderInstruction> h3 = MarkdownToRichText.Parse("### Sub-sub");

        Assert.That(h2.First(r => r.Kind == MarkdownToRichText.RunKind.Text).Header,
            Is.EqualTo(MarkdownToRichText.HeaderStyle.H2));
        Assert.That(h3.First(r => r.Kind == MarkdownToRichText.RunKind.Text).Header,
            Is.EqualTo(MarkdownToRichText.HeaderStyle.H3));
    }

    [Test]
    public void Parse_BoldSpan_EmitsBoldRun()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output =
            MarkdownToRichText.Parse("This is **important** text.");

        // Three text runs expected: "This is ", "important", " text."
        var runs = output.Where(r => r.Kind == MarkdownToRichText.RunKind.Text).ToList();
        Assert.That(runs, Has.Count.EqualTo(3));
        Assert.That(runs[0].Bold, Is.False);
        Assert.That(runs[1].Bold, Is.True);
        Assert.That(runs[1].Text, Is.EqualTo("important"));
        Assert.That(runs[2].Bold, Is.False);
    }

    [Test]
    public void Parse_InlineCode_EmitsCodeRun()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output =
            MarkdownToRichText.Parse("Run `dotnet build` to compile.");

        var runs = output.Where(r => r.Kind == MarkdownToRichText.RunKind.Text).ToList();
        Assert.That(runs, Has.Count.EqualTo(3));
        Assert.That(runs[1].Code, Is.True);
        Assert.That(runs[1].Text, Is.EqualTo("dotnet build"));
    }

    [Test]
    public void Parse_BulletList_EmitsBulletPrefix()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output =
            MarkdownToRichText.Parse("- first\n- second");

        // First text run should be the bullet "• "; second is the item text.
        var runs = output.Where(r => r.Kind == MarkdownToRichText.RunKind.Text).ToList();
        Assert.That(runs.Any(r => r.Text == "• "), Is.True);
        Assert.That(runs.Any(r => r.Text == "first"), Is.True);
        Assert.That(runs.Any(r => r.Text == "second"), Is.True);
    }

    [Test]
    public void Parse_NumberedList_PreservesUserNumbering()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output =
            MarkdownToRichText.Parse("1. apple\n2. banana");

        var runs = output.Where(r => r.Kind == MarkdownToRichText.RunKind.Text).ToList();
        Assert.That(runs.Any(r => r.Text == "1. "), Is.True);
        Assert.That(runs.Any(r => r.Text == "2. "), Is.True);
    }

    [Test]
    public void Parse_BlankLines_EmitParagraphBreak()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output =
            MarkdownToRichText.Parse("First.\n\nSecond.");

        int breaks = output.Count(r => r.Kind == MarkdownToRichText.RunKind.LineBreak);
        Assert.That(breaks, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        IReadOnlyList<MarkdownToRichText.RenderInstruction> output = MarkdownToRichText.Parse(string.Empty);
        Assert.That(output, Is.Empty);
    }

    [Test]
    public void Parse_MixedContent_ProducesAllExpectedRunKinds()
    {
        const string md = "# Title\n\nIntro paragraph with **bold** word.\n\n" +
                          "- bullet one\n- bullet two\n\n" +
                          "Use `code` here.";

        IReadOnlyList<MarkdownToRichText.RenderInstruction> output = MarkdownToRichText.Parse(md);

        var textRuns = output.Where(r => r.Kind == MarkdownToRichText.RunKind.Text).ToList();
        Assert.That(textRuns.Any(r => r.Header == MarkdownToRichText.HeaderStyle.H1 && r.Text == "Title"));
        Assert.That(textRuns.Any(r => r.Bold && r.Text == "bold"));
        Assert.That(textRuns.Any(r => r.Code && r.Text == "code"));
        Assert.That(textRuns.Any(r => r.Text == "• "));
    }
}
