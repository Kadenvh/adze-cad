using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Adze.Contracts.Abstractions;

namespace Adze.UI.V2;

/// <summary>
/// Horizontal row of small "chip" buttons rendered immediately under the
/// prompt input on the Chat tab. Each chip is a one-tap shortcut to a common
/// grounding query (Diagnose, Mates, Dimensions, Properties, Explain) and
/// dispatches through the same <see cref="ITaskPaneHost.SubmitUserQuery"/>
/// path the prompt input uses.
///
/// Layout: <see cref="FlowLayoutPanel"/> with <c>WrapContents = true</c> so
/// chips wrap onto a second row when the sidebar is narrow. Trailing "+"
/// button is a placeholder for a future user-defined-quick-actions feature.
///
/// Visual treatment: subtle border, surface bg, accent text. Hover swaps the
/// background to <see cref="UiPalette.AccentTint"/>. Click feedback is the
/// default <see cref="FlatStyle.Flat"/> press shade.
/// </summary>
public sealed class QuickActionsBar : FlowLayoutPanel
{
    private readonly ITaskPaneHost _host;
    private readonly List<Button> _chips = new();

    /// <summary>Built-in quick-action set surfaced on first launch.</summary>
    public static readonly QuickAction[] DefaultActions =
    {
        new("Diagnose",   "Diagnose this model"),
        new("Mates",      "List all mates with status"),
        new("Dimensions", "Show key dimensions"),
        new("Properties", "Show all custom properties"),
        new("Explain",    "Explain the selection"),
    };

    /// <summary>One quick-action: button label + the prompt text it submits.</summary>
    public readonly struct QuickAction
    {
        public string Label { get; }
        public string Query { get; }
        public QuickAction(string label, string query)
        {
            Label = label ?? string.Empty;
            Query = query ?? string.Empty;
        }
    }

    public QuickActionsBar(ITaskPaneHost host)
        : this(host, DefaultActions)
    {
    }

    public QuickActionsBar(ITaskPaneHost host, IEnumerable<QuickAction> actions)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (actions == null) throw new ArgumentNullException(nameof(actions));

        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = UiPalette.SurfaceBackground;
        Padding = new Padding(8, 6, 8, 6);
        Margin = Padding.Empty;

        foreach (QuickAction action in actions)
        {
            Button chip = BuildChip(action.Label, action.Query);
            _chips.Add(chip);
            Controls.Add(chip);
        }

        // Trailing "+" placeholder for future user-defined chips.
        Button plus = BuildPlusChip();
        Controls.Add(plus);

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
        // Re-tint chips. Chip Tag = query string, so the last control (plus
        // placeholder) is identifiable by its empty Tag and "+" text.
        foreach (Button chip in _chips)
        {
            chip.BackColor = UiPalette.SubtleButtonBackground;
            chip.ForeColor = UiPalette.AccentDark;
            chip.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
            chip.FlatAppearance.MouseOverBackColor = UiPalette.AccentTint;
            chip.FlatAppearance.MouseDownBackColor = UiPalette.AccentTint;
        }
        // Plus placeholder is the last control after the chips.
        if (Controls.Count > _chips.Count && Controls[Controls.Count - 1] is Button plus)
        {
            plus.BackColor = UiPalette.SubtleButtonBackground;
            plus.ForeColor = UiPalette.TextSecondary;
            plus.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
            plus.FlatAppearance.MouseOverBackColor = UiPalette.AccentTint;
        }
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

    /// <summary>Read-only access for tests — verifies chip wiring.</summary>
    public IReadOnlyList<Button> Chips => _chips;

    private Button BuildChip(string label, string query)
    {
        Button btn = new()
        {
            Text = label,
            AutoSize = false,
            Size = new Size(GetChipPreferredWidth(label), 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = UiPalette.SubtleButtonBackground,
            ForeColor = UiPalette.AccentDark,
            Font = new Font(UiPalette.FontFamily, UiPalette.ChipFontSize, FontStyle.Regular),
            Margin = new Padding(0, 0, 6, 6),
            Padding = new Padding(8, 0, 8, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Tag = query
        };
        btn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = UiPalette.AccentTint;
        btn.FlatAppearance.MouseDownBackColor = UiPalette.AccentTint;
        btn.Click += (_, _) => InvokeQuickAction(query);
        return btn;
    }

    private Button BuildPlusChip()
    {
        Button btn = new()
        {
            Text = "+",
            AutoSize = false,
            Size = new Size(28, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = UiPalette.SubtleButtonBackground,
            ForeColor = UiPalette.TextSecondary,
            Font = new Font(UiPalette.FontFamily, UiPalette.HeaderFontSize, FontStyle.Bold),
            Margin = new Padding(0, 0, 6, 6),
            Padding = new Padding(0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        btn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = UiPalette.AccentTint;
        btn.Click += (_, _) =>
        {
            MessageBox.Show(this,
                "Custom quick actions are coming soon.\n\n" +
                "You'll be able to define your own shortcuts here — for now, " +
                "use the built-in chips or type your prompt directly.",
                "Custom quick actions",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        return btn;
    }

    /// <summary>Public entry point used by the chip click handlers and unit tests.</summary>
    public void InvokeQuickAction(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            _host.SubmitUserQuery(query, CancellationToken.None);
        }
        catch
        {
            // Quick-action dispatch failures bubble up via the host's own error path.
        }
    }

    private int GetChipPreferredWidth(string label)
    {
        // Approximate width: ~7px per char + 24px padding. Capped to avoid
        // a single chip eating the entire sidebar width on narrow panes.
        int approx = 24 + (label.Length * 7);
        return Math.Min(140, Math.Max(60, approx));
    }

    /// <summary>Override to draw a soft rounded border on each chip.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // FlowLayoutPanel itself paints its background; the chips draw their
        // own borders. No extra painting needed at the panel level.
    }
}
