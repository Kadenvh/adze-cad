using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Adze.UI.Rendering;

/// <summary>
/// Renders the same markdown subset the legacy <c>TaskPaneControl.ConvertTextToHtmlBody</c>
/// supported, but applies formatting directly to a <see cref="RichTextBox"/> via
/// Selection* properties — no HTML round-trip.
///
/// Supported syntax (mirrors old converter, lines 1659-1734):
///   <c># heading</c>, <c>## heading</c>, <c>### heading</c> — h1/h2/h3 sized + bold
///   <c>- item</c>, <c>* item</c> — bullet list
///   <c>1. item</c>, <c>2. item</c> — numbered list (preserves the user's number)
///   <c>**bold**</c> — bold span
///   <c>`code`</c> — Consolas + light-grey background span
///   blank lines — paragraph break
///   anything else — plain paragraph text
///
/// The converter is parser-light by design: we walk lines top-down and emit runs
/// (text + style flags) into the target. No nested lists, no tables, no images.
/// Behavior matches the old HTML body precisely, so existing markdown produced
/// by the broker renders identically in the new native sidebar.
/// </summary>
public static class MarkdownToRichText
{
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex CodeRegex = new(@"`(.+?)`", RegexOptions.Compiled);

    /// <summary>
    /// Replaces the entire content of the target RichTextBox with the rendered markdown.
    /// </summary>
    public static void Render(RichTextBox target, string markdown)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        target.Clear();
        AppendRendered(target, markdown);
    }

    /// <summary>
    /// Appends rendered markdown to the end of the target RichTextBox without
    /// clearing existing content. Used for streaming and incremental updates.
    /// </summary>
    public static void AppendRendered(RichTextBox target, string markdown)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (string.IsNullOrEmpty(markdown)) return;

        IReadOnlyList<RenderInstruction> instructions = Parse(markdown);
        foreach (RenderInstruction instr in instructions)
        {
            ApplyInstruction(target, instr);
        }
    }

    /// <summary>
    /// Pure-logic markdown parser: returns the sequence of styled runs that
    /// would be appended for the given input. Public so unit tests can verify
    /// the parse without spinning up a full WinForms control.
    /// </summary>
    public static IReadOnlyList<RenderInstruction> Parse(string markdown)
    {
        var output = new List<RenderInstruction>();
        if (string.IsNullOrEmpty(markdown)) return output;

        string normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalized.Split('\n');

        bool firstBlock = true;
        foreach (string raw in lines)
        {
            string trimmed = raw.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                // Paragraph break — collapse runs of blanks.
                if (output.Count > 0 && output[output.Count - 1].Kind != RunKind.LineBreak)
                {
                    output.Add(new RenderInstruction(RunKind.LineBreak));
                }
                continue;
            }

            if (!firstBlock && (output.Count == 0 || output[output.Count - 1].Kind != RunKind.LineBreak))
            {
                output.Add(new RenderInstruction(RunKind.LineBreak));
            }
            firstBlock = false;

            // Headings
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                EmitInline(output, trimmed.Substring(4), HeaderStyle.H3);
                continue;
            }
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                EmitInline(output, trimmed.Substring(3), HeaderStyle.H2);
                continue;
            }
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                EmitInline(output, trimmed.Substring(2), HeaderStyle.H1);
                continue;
            }

            // Bullet list
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                output.Add(new RenderInstruction(RunKind.Text, "• ", false, false, false, HeaderStyle.None));
                EmitInline(output, trimmed.Substring(2), HeaderStyle.None);
                continue;
            }

            // Numbered list (e.g. "1. ", "12. ")
            int dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
            if (dot > 0 && dot <= 3 && int.TryParse(trimmed.Substring(0, dot), out int _))
            {
                output.Add(new RenderInstruction(RunKind.Text, trimmed.Substring(0, dot + 2), false, false, false, HeaderStyle.None));
                EmitInline(output, trimmed.Substring(dot + 2), HeaderStyle.None);
                continue;
            }

            // Plain paragraph
            EmitInline(output, trimmed, HeaderStyle.None);
        }

        return output;
    }

    private static void EmitInline(List<RenderInstruction> output, string text, HeaderStyle header)
    {
        if (string.IsNullOrEmpty(text)) return;

        // We process bold and code as overlapping runs would not be supported
        // by RichTextBox SelectionFont anyway. We do a simple two-pass split:
        // tokenize the input into segments separated by **bold** and `code`.

        var segments = TokenizeInline(text);
        foreach (InlineSegment seg in segments)
        {
            output.Add(new RenderInstruction(
                RunKind.Text,
                seg.Text,
                seg.Bold,
                false,
                seg.Code,
                header));
        }
    }

    private static List<InlineSegment> TokenizeInline(string text)
    {
        // Find all bold + code matches, sort by index, walk left-to-right
        // emitting plain segments between them.
        var matches = new List<(int Start, int End, bool Bold, bool Code, string Inner)>();

        foreach (Match m in BoldRegex.Matches(text))
        {
            matches.Add((m.Index, m.Index + m.Length, true, false, m.Groups[1].Value));
        }
        foreach (Match m in CodeRegex.Matches(text))
        {
            // Skip if this code match is inside a bold span (rare; choose bold).
            bool overlapped = false;
            foreach (var existing in matches)
            {
                if (m.Index >= existing.Start && m.Index < existing.End)
                {
                    overlapped = true;
                    break;
                }
            }
            if (!overlapped)
            {
                matches.Add((m.Index, m.Index + m.Length, false, true, m.Groups[1].Value));
            }
        }

        matches.Sort((a, b) => a.Start.CompareTo(b.Start));

        var result = new List<InlineSegment>();
        int cursor = 0;
        foreach (var m in matches)
        {
            if (m.Start > cursor)
            {
                result.Add(new InlineSegment(text.Substring(cursor, m.Start - cursor), false, false));
            }
            result.Add(new InlineSegment(m.Inner, m.Bold, m.Code));
            cursor = m.End;
        }
        if (cursor < text.Length)
        {
            result.Add(new InlineSegment(text.Substring(cursor), false, false));
        }
        return result;
    }

    private static void ApplyInstruction(RichTextBox target, RenderInstruction instr)
    {
        if (instr.Kind == RunKind.LineBreak)
        {
            target.AppendText(Environment.NewLine);
            return;
        }

        int start = target.TextLength;
        target.AppendText(instr.Text);
        int end = target.TextLength;

        target.Select(start, end - start);

        Font baseFont = target.Font;
        FontStyle style = FontStyle.Regular;
        float size = baseFont.Size;
        string family = baseFont.FontFamily.Name;
        Color back = target.BackColor;

        if (instr.Bold) style |= FontStyle.Bold;
        switch (instr.Header)
        {
            case HeaderStyle.H1:
                style |= FontStyle.Bold;
                size = baseFont.Size + 4f;
                break;
            case HeaderStyle.H2:
                style |= FontStyle.Bold;
                size = baseFont.Size + 2.5f;
                break;
            case HeaderStyle.H3:
                style |= FontStyle.Bold;
                size = baseFont.Size + 1f;
                break;
        }

        if (instr.Code)
        {
            family = "Consolas";
            back = Color.FromArgb(240, 242, 245);
        }

        target.SelectionFont = new Font(family, size, style);
        target.SelectionBackColor = back;

        // Reset selection past the inserted run.
        target.Select(end, 0);
    }

    /// <summary>One styled run produced by <see cref="Parse"/>.</summary>
    public sealed class RenderInstruction
    {
        public RunKind Kind { get; }
        public string Text { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public bool Code { get; }
        public HeaderStyle Header { get; }

        public RenderInstruction(RunKind kind)
            : this(kind, string.Empty, false, false, false, HeaderStyle.None) { }

        public RenderInstruction(RunKind kind, string text, bool bold, bool italic, bool code, HeaderStyle header)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Bold = bold;
            Italic = italic;
            Code = code;
            Header = header;
        }
    }

    public enum RunKind
    {
        Text,
        LineBreak
    }

    public enum HeaderStyle
    {
        None,
        H1,
        H2,
        H3
    }

    private readonly struct InlineSegment
    {
        public InlineSegment(string text, bool bold, bool code)
        {
            Text = text;
            Bold = bold;
            Code = code;
        }
        public string Text { get; }
        public bool Bold { get; }
        public bool Code { get; }
    }
}
