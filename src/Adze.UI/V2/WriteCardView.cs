using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;
using Adze.Contracts.Models;

namespace Adze.UI.V2;

/// <summary>
/// Inline approval card for one <see cref="PendingWriteAction"/>.
///
/// v1.1 visual treatment:
///   - Card sits on the surface with a 1px hairline border + soft rounded corners.
///   - Header row: bold tool name + small "Apply pending" / "Elevated" tag chip.
///   - Body: monospace diff in <see cref="RichTextBox"/> with colour-coded
///     before/after lines (red for "before", green for "after").
///   - Footer: Apply (accent button, primary) + Cancel (subtle button,
///     secondary), right-aligned.
/// </summary>
public sealed class WriteCardView : UserControl
{
    private const int CornerRadius = 10;

    public PendingWriteAction Action { get; }

    public event EventHandler<string>? ApplyRequested;
    public event EventHandler<string>? CancelRequested;

    private readonly Button _applyBtn;
    private readonly Button _cancelBtn;
    private readonly bool _elevated;
    private readonly Label _header;
    private readonly Label _tag;
    private readonly Label _undoLabel;
    private readonly Label _summary;
    private readonly RichTextBox _diff;
    private readonly Label _warnings;

    public WriteCardView(PendingWriteAction action)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
        _elevated = action.IsElevated;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Padding = new Padding(14, 12, 14, 12);
        Margin = new Padding(8, 6, 8, 6);
        BackColor = UiPalette.SurfaceBackground;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;

        // ─── Header row: tool name + tag chip ────────────────────────────
        Panel headerRow = new()
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Color.Transparent,
            Padding = new Padding(0)
        };

        _header = new Label
        {
            Text = action.ToolName,
            Font = new Font(UiPalette.FontFamily, UiPalette.HeaderFontSize, FontStyle.Bold),
            ForeColor = UiPalette.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 4),
            BackColor = Color.Transparent
        };

        _tag = BuildTag(_elevated ? "ELEVATED" : "Apply pending", _elevated);
        // Tag is positioned after layout — see the SizeChanged hookup below.
        headerRow.Controls.Add(_header);
        headerRow.Controls.Add(_tag);
        Label tagRef = _tag;
        headerRow.Resize += (_, _) =>
        {
            // Right-align the tag inside the header row.
            tagRef.Location = new Point(headerRow.Width - tagRef.Width - 2, 4);
        };

        // ─── Undo label (sub-header) ─────────────────────────────────────
        _undoLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(action.UndoLabel) ? string.Empty : "Undo: " + action.UndoLabel,
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize, FontStyle.Italic),
            ForeColor = UiPalette.TextSecondary,
            Dock = DockStyle.Top,
            Height = 18,
            BackColor = Color.Transparent,
            Visible = !string.IsNullOrWhiteSpace(action.UndoLabel),
            Padding = new Padding(0, 2, 0, 0)
        };

        // ─── Summary line ────────────────────────────────────────────────
        _summary = new Label
        {
            Text = action.Preview?.Summary ?? string.Empty,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
            ForeColor = UiPalette.TextPrimary,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 18,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 2, 0, 0),
            AutoEllipsis = true,
            Visible = !string.IsNullOrWhiteSpace(action.Preview?.Summary)
        };

        // ─── Diff (colour-coded RichTextBox) ─────────────────────────────
        _diff = new RichTextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font(UiPalette.MonoFontFamily, UiPalette.BodyFontSize - 0.5f),
            BackColor = UiPalette.CardBackground,
            ForeColor = UiPalette.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Top,
            Height = 96,
            DetectUrls = false,
            TabStop = false
        };
        PopulateDiff(_diff, action.Preview);

        // ─── Warnings ────────────────────────────────────────────────────
        bool hasWarnings = action.Preview?.Warnings != null && action.Preview.Warnings.Count > 0;
        _warnings = new Label
        {
            Text = BuildWarningText(action.Preview),
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize),
            ForeColor = UiPalette.BannerWarningForeground,
            BackColor = UiPalette.BannerWarningBackground,
            AutoSize = false,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 6, 8, 6),
            Visible = hasWarnings,
            Height = hasWarnings ? 0 : 0,  // measured below
            MaximumSize = new Size(0, 0)
        };
        if (hasWarnings)
        {
            Size warnPref = TextRenderer.MeasureText(
                _warnings.Text, _warnings.Font,
                new Size(360, int.MaxValue),
                TextFormatFlags.WordBreak);
            _warnings.Height = Math.Max(28, warnPref.Height + 12);
        }

        // ─── Footer (Apply / Cancel) ─────────────────────────────────────
        Panel footerRow = new()
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };

        _applyBtn = new Button
        {
            Text = "Apply",
            BackColor = _elevated ? UiPalette.WriteCardElevatedBorder : UiPalette.Accent,
            ForeColor = UiPalette.AccentForeground,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(96, 28),
            Font = new Font(UiPalette.FontFamily, UiPalette.ButtonFontSize, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _applyBtn.FlatAppearance.BorderSize = 0;
        _applyBtn.FlatAppearance.MouseOverBackColor =
            _elevated ? Color.FromArgb(217, 119, 6) : UiPalette.AccentDark;
        _applyBtn.Click += (_, _) => ApplyRequested?.Invoke(this, action.WriteId);

        _cancelBtn = new Button
        {
            Text = "Cancel",
            BackColor = UiPalette.SubtleButtonBackground,
            ForeColor = UiPalette.SubtleButtonForeground,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(80, 28),
            Font = new Font(UiPalette.FontFamily, UiPalette.ButtonFontSize),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _cancelBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _cancelBtn.FlatAppearance.BorderSize = 1;
        _cancelBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;
        _cancelBtn.Click += (_, _) => CancelRequested?.Invoke(this, action.WriteId);

        footerRow.Controls.Add(_applyBtn);
        footerRow.Controls.Add(_cancelBtn);
        footerRow.Resize += (_, _) =>
        {
            // Right-align: Cancel rightmost, Apply to its left.
            _cancelBtn.Location = new Point(footerRow.Width - _cancelBtn.Width, 8);
            _applyBtn.Location = new Point(footerRow.Width - _cancelBtn.Width - _applyBtn.Width - 8, 8);
        };

        // Add bottom-up so DockStyle.Top stacks correctly (last-added = top).
        Controls.Add(footerRow);
        if (_warnings.Visible) Controls.Add(_warnings);
        Controls.Add(_diff);
        if (_summary.Visible) Controls.Add(_summary);
        if (_undoLabel.Visible) Controls.Add(_undoLabel);
        Controls.Add(headerRow);

        // Theme tracking — re-apply card colours on every Light/Dark/System swap.
        UiPalette.ModeChanged += OnPaletteModeChanged;
    }

    private void OnPaletteModeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(ReapplyTheme)); } catch (InvalidOperationException) { }
            return;
        }
        ReapplyTheme();
    }

    private void ReapplyTheme()
    {
        BackColor = UiPalette.SurfaceBackground;
        _header.ForeColor = UiPalette.TextPrimary;
        _undoLabel.ForeColor = UiPalette.TextSecondary;
        _summary.ForeColor = UiPalette.TextPrimary;
        _warnings.ForeColor = UiPalette.BannerWarningForeground;
        _warnings.BackColor = UiPalette.BannerWarningBackground;
        _diff.BackColor = UiPalette.CardBackground;
        _diff.ForeColor = UiPalette.TextPrimary;
        PopulateDiff(_diff, Action.Preview);

        // Tag re-tint
        Color tagBack = _elevated ? UiPalette.BannerWarningBackground : UiPalette.WriteCardTagBackground;
        Color tagFore = _elevated ? UiPalette.BannerWarningForeground : UiPalette.WriteCardTagForeground;
        _tag.BackColor = tagBack;
        _tag.ForeColor = tagFore;

        // Buttons
        _applyBtn.BackColor = _elevated ? UiPalette.WriteCardElevatedBorder : UiPalette.Accent;
        _applyBtn.ForeColor = UiPalette.AccentForeground;
        _applyBtn.FlatAppearance.MouseOverBackColor =
            _elevated ? Color.FromArgb(217, 119, 6) : UiPalette.AccentDark;
        _cancelBtn.BackColor = UiPalette.SubtleButtonBackground;
        _cancelBtn.ForeColor = UiPalette.SubtleButtonForeground;
        _cancelBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _cancelBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;

        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UiPalette.ModeChanged -= OnPaletteModeChanged;
        }
        base.Dispose(disposing);
    }

    public void Disable(string? finalMessage = null)
    {
        _applyBtn.Enabled = false;
        _cancelBtn.Enabled = false;
        if (!string.IsNullOrWhiteSpace(finalMessage))
        {
            Label result = new()
            {
                Text = finalMessage,
                AutoSize = true,
                Dock = DockStyle.Top,
                ForeColor = UiPalette.BannerSuccessForeground,
                BackColor = Color.Transparent,
                Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize, FontStyle.Italic),
                Padding = new Padding(0, 4, 0, 0)
            };
            Controls.Add(result);
        }
    }

    /// <summary>Paint a soft rounded card with optional elevated border.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle r = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = BuildRoundedPath(r, CornerRadius);

        using (SolidBrush fill = new(UiPalette.WriteCardBackground))
        {
            g.FillPath(fill, path);
        }
        Color borderColor = _elevated ? UiPalette.WriteCardElevatedBorder : UiPalette.WriteCardBorder;
        int borderWidth = _elevated ? 2 : 1;
        using (Pen pen = new(borderColor, borderWidth))
        {
            g.DrawPath(pen, path);
        }
    }

    private static GraphicsPath BuildRoundedPath(Rectangle r, int radius)
    {
        int d = radius * 2;
        GraphicsPath p = new();
        if (radius <= 0)
        {
            p.AddRectangle(r);
            p.CloseFigure();
            return p;
        }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Label BuildTag(string text, bool elevated)
    {
        Color back = elevated ? UiPalette.BannerWarningBackground : UiPalette.WriteCardTagBackground;
        Color fore = elevated ? UiPalette.BannerWarningForeground : UiPalette.WriteCardTagForeground;
        Label tag = new()
        {
            Text = text,
            BackColor = back,
            ForeColor = fore,
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0)
        };
        return tag;
    }

    private static void PopulateDiff(RichTextBox target, WritePreview? preview)
    {
        target.Clear();
        if (preview?.Changes == null || preview.Changes.Count == 0)
        {
            target.SelectionColor = UiPalette.TextSecondary;
            target.AppendText("(no field-level changes captured)");
            return;
        }

        foreach (WriteChangeItem item in preview.Changes)
        {
            // Target label — neutral
            target.SelectionColor = UiPalette.TextPrimary;
            target.SelectionFont = new Font(UiPalette.MonoFontFamily, UiPalette.BodyFontSize - 0.5f, FontStyle.Bold);
            target.AppendText(item.TargetLabel);
            target.AppendText(Environment.NewLine);

            // Before — red
            target.SelectionFont = new Font(UiPalette.MonoFontFamily, UiPalette.BodyFontSize - 0.5f);
            target.SelectionColor = UiPalette.DiffRemovedForeground;
            target.SelectionBackColor = UiPalette.DiffRemovedBackground;
            target.AppendText("- " + (item.BeforeValue ?? string.Empty));
            target.AppendText(Environment.NewLine);

            // After — green
            target.SelectionColor = UiPalette.DiffAddedForeground;
            target.SelectionBackColor = UiPalette.DiffAddedBackground;
            target.AppendText("+ " + (item.AfterValue ?? string.Empty));
            target.AppendText(Environment.NewLine);

            // Reset for next group
            target.SelectionBackColor = target.BackColor;
            target.AppendText(Environment.NewLine);
        }
        target.Select(0, 0);
    }

    private static string BuildWarningText(WritePreview? preview)
    {
        if (preview?.Warnings == null || preview.Warnings.Count == 0) return string.Empty;
        var sb = new StringBuilder("Warnings:");
        foreach (string w in preview.Warnings)
        {
            sb.AppendLine().Append("  • ").Append(w);
        }
        return sb.ToString();
    }
}
