using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Adze.Broker.Configuration;

namespace Adze.UI.V2;

/// <summary>
/// Pre-prompt clarification UI. Replaces the placeholder MessageBox the
/// "Refine intent" button used to pop. Lets the user nudge intent / scope /
/// output / diagnostics before submitting.
///
/// Why rebuilt and not lifted from the old <c>TaskPaneControl</c>:
///   - The old version was 4 controls in a 160px fixed panel, sandwiched
///     between a WebBrowser host and a TextBox — fiddly DPI math, no theme
///     support, no persistence. v1.1 restarts cleanly: a self-contained
///     <see cref="UserControl"/> with a 2x2 GroupBox grid, theme-aware paint,
///     and persisted defaults via <see cref="UiPreferences"/>.
///
/// Layout (when expanded):
///   ┌──────────────────────────┬──────────────────────────┐
///   │ Intent  (ComboBox)        │ Scope   (CheckedListBox) │
///   ├──────────────────────────┼──────────────────────────┤
///   │ Output  (ComboBox)        │ Diagnostics (CheckedList)│
///   └──────────────────────────┴──────────────────────────┘
///
/// Submit-time integration: <see cref="BuildPrefix"/> emits a clarification
/// block compatible with the broker's existing <c>ExtractClarificationIntent</c>
/// parser (which keys off <c>[clarification]intent=...[/clarification]</c>).
/// We also include scope/output/diag fields for downstream consumers; the
/// current broker simply ignores unknown keys (verified in
/// <c>KeywordBrokerOrchestrator.ExtractClarificationIntent</c>).
/// </summary>
public sealed class RefinementPanel : UserControl
{
    /// <summary>Built-in intent dropdown values. <c>none</c> emits no intent= clause.</summary>
    public static readonly (string Display, string Value)[] IntentChoices =
    {
        ("(none)",      "none"),
        ("Diagnostic",  "diagnostic"),
        ("Modify",      "modify"),
        ("Inspect",     "inspect"),
        ("Plan",        "plan"),
    };

    /// <summary>Scope tags shown in the multi-select list.</summary>
    public static readonly (string Display, string Value)[] ScopeChoices =
    {
        ("Active doc only",      "active_doc"),
        ("Selected feature",     "selected_feature"),
        ("Selected mate",        "selected_mate"),
        ("Whole assembly",       "whole_assembly"),
        ("Drawing views",        "drawing_views"),
    };

    /// <summary>Output format hint values.</summary>
    public static readonly (string Display, string Value)[] OutputChoices =
    {
        ("(default)",       "default"),
        ("Concise",         "concise"),
        ("Step-by-step",    "step"),
        ("Bullet list",     "bullet"),
        ("Table",           "table"),
    };

    /// <summary>Diagnostic toggles.</summary>
    public static readonly (string Display, string Value)[] DiagnosticChoices =
    {
        ("Show tool calls",       "tool_calls"),
        ("Show timings",          "timings"),
        ("Show context size",     "context_size"),
        ("Show fallback reason",  "fallback_reason"),
    };

    private readonly ComboBox _intent;
    private readonly CheckedListBox _scope;
    private readonly ComboBox _output;
    private readonly CheckedListBox _diagnostics;
    private readonly GroupBox _gIntent;
    private readonly GroupBox _gScope;
    private readonly GroupBox _gOutput;
    private readonly GroupBox _gDiag;

    public RefinementPanel()
    {
        Dock = DockStyle.Bottom;
        Height = 200;
        Visible = false;
        BackColor = UiPalette.SurfaceBackground;
        Padding = new Padding(12, 8, 12, 8);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        // ── Intent group ──
        _gIntent = BuildGroup("Intent");
        _intent = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
        };
        foreach (var c in IntentChoices) _intent.Items.Add(c.Display);
        _intent.SelectedIndex = 0;
        _gIntent.Controls.Add(_intent);

        // ── Scope group ──
        _gScope = BuildGroup("Scope");
        _scope = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
            IntegralHeight = false,
        };
        foreach (var c in ScopeChoices) _scope.Items.Add(c.Display);
        _gScope.Controls.Add(_scope);

        // ── Output group ──
        _gOutput = BuildGroup("Output");
        _output = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
        };
        foreach (var c in OutputChoices) _output.Items.Add(c.Display);
        _output.SelectedIndex = 0;
        _gOutput.Controls.Add(_output);

        // ── Diagnostics group ──
        _gDiag = BuildGroup("Diagnostics");
        _diagnostics = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
            IntegralHeight = false,
        };
        foreach (var c in DiagnosticChoices) _diagnostics.Items.Add(c.Display);
        _gDiag.Controls.Add(_diagnostics);

        grid.Controls.Add(_gIntent, 0, 0);
        grid.Controls.Add(_gScope, 1, 0);
        grid.Controls.Add(_gOutput, 0, 1);
        grid.Controls.Add(_gDiag, 1, 1);
        Controls.Add(grid);

        LoadDefaults();

        // Persist on every change so the next session opens with the user's
        // last selections — matches Claude/ChatGPT pattern.
        _intent.SelectedIndexChanged += (_, _) => Persist();
        _output.SelectedIndexChanged += (_, _) => Persist();
        // ItemCheck fires BEFORE the underlying state mutation completes — we
        // marshal Persist() to fire after the mutation. When the panel hasn't
        // been parented yet (e.g. unit tests), there's no handle and BeginInvoke
        // throws; fall back to direct call in that case. The "before vs after"
        // discrepancy is acceptable here because Persist re-reads the live
        // CheckedIndices each call.
        _scope.ItemCheck += (_, e) => SafeAfterCheck(_scope, e);
        _diagnostics.ItemCheck += (_, e) => SafeAfterCheck(_diagnostics, e);

        UiPalette.ModeChanged += OnModeChanged;
        ApplyTheme();
    }

    private static GroupBox BuildGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Fill,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(2),
        Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize, FontStyle.Bold),
        ForeColor = UiPalette.TextPrimary,
        BackColor = UiPalette.SurfaceBackground,
    };

    /// <summary>Toggle the panel's visibility. Returns the new visibility state.</summary>
    public bool Toggle()
    {
        Visible = !Visible;
        Persist();
        return Visible;
    }

    /// <summary>
    /// Build the prefix string to prepend to the user's prompt at submit time.
    /// Empty string when nothing is selected (so we don't paste a no-op prefix).
    /// Format mirrors the broker's existing parser:
    /// <c>[clarification] intent=foo, scope=a;b, output=concise, diagnostics=tool_calls;timings [/clarification]</c>
    /// </summary>
    public string BuildPrefix()
    {
        var parts = new List<string>();

        string intentValue = SelectedIntentValue();
        if (!string.IsNullOrEmpty(intentValue) && intentValue != "none")
            parts.Add("intent=" + intentValue);

        var scopes = SelectedScopeValues();
        if (scopes.Count > 0)
            parts.Add("scope=" + string.Join(";", scopes));

        string outputValue = SelectedOutputValue();
        if (!string.IsNullOrEmpty(outputValue) && outputValue != "default")
            parts.Add("output=" + outputValue);

        var diags = SelectedDiagnosticValues();
        if (diags.Count > 0)
            parts.Add("diagnostics=" + string.Join(";", diags));

        if (parts.Count == 0) return string.Empty;
        return "[clarification] " + string.Join(", ", parts) + " [/clarification]";
    }

    /// <summary>The internal value (e.g. "diagnostic") of the selected intent — "none" when no override.</summary>
    public string SelectedIntentValue()
    {
        int idx = _intent.SelectedIndex;
        if (idx < 0 || idx >= IntentChoices.Length) return "none";
        return IntentChoices[idx].Value;
    }

    public string SelectedOutputValue()
    {
        int idx = _output.SelectedIndex;
        if (idx < 0 || idx >= OutputChoices.Length) return "default";
        return OutputChoices[idx].Value;
    }

    public IReadOnlyList<string> SelectedScopeValues()
    {
        var result = new List<string>();
        foreach (int i in _scope.CheckedIndices)
        {
            if (i >= 0 && i < ScopeChoices.Length) result.Add(ScopeChoices[i].Value);
        }
        return result;
    }

    public IReadOnlyList<string> SelectedDiagnosticValues()
    {
        var result = new List<string>();
        foreach (int i in _diagnostics.CheckedIndices)
        {
            if (i >= 0 && i < DiagnosticChoices.Length) result.Add(DiagnosticChoices[i].Value);
        }
        return result;
    }

    /// <summary>Restore last-used selections from <see cref="UiPreferences"/>.</summary>
    public void LoadDefaults()
    {
        try
        {
            UiPreferences prefs = UiPreferences.Load();
            int intentIdx = Array.FindIndex(IntentChoices, c => string.Equals(c.Value, prefs.ClarificationIntent, StringComparison.OrdinalIgnoreCase));
            _intent.SelectedIndex = intentIdx >= 0 ? intentIdx : 0;

            int outputIdx = Array.FindIndex(OutputChoices, c => string.Equals(c.Value, prefs.ClarificationOutput, StringComparison.OrdinalIgnoreCase));
            _output.SelectedIndex = outputIdx >= 0 ? outputIdx : 0;

            for (int i = 0; i < ScopeChoices.Length; i++)
            {
                _scope.SetItemChecked(i, prefs.ClarificationScopes.Contains(ScopeChoices[i].Value, StringComparer.OrdinalIgnoreCase));
            }

            for (int i = 0; i < DiagnosticChoices.Length; i++)
            {
                _diagnostics.SetItemChecked(i, prefs.ClarificationDiagnostics.Contains(DiagnosticChoices[i].Value, StringComparer.OrdinalIgnoreCase));
            }

            Visible = prefs.ClarificationExpanded;
        }
        catch
        {
            // Defensive — bad prefs file shouldn't break the panel.
        }
    }

    /// <summary>
    /// Persist clarification state in response to a CheckedListBox.ItemCheck
    /// event. ItemCheck fires BEFORE the underlying check-state commit, so we
    /// reconstruct the post-event check set ourselves rather than reading
    /// CheckedIndices (which still reflects the pre-event state). Works the
    /// same in production and unit tests — no UI-thread marshaling needed.
    /// </summary>
    private void SafeAfterCheck(CheckedListBox box, ItemCheckEventArgs e)
    {
        try
        {
            // Snapshot post-event check set: existing CheckedIndices except
            // the index that's about to flip; flipping replays the event arg.
            var post = new List<int>();
            foreach (int i in box.CheckedIndices)
            {
                if (i != e.Index) post.Add(i);
            }
            if (e.NewValue == CheckState.Checked) post.Add(e.Index);
            // Save through Persist with the predicted post-event indices.
            PersistWithOverride(box, post);
        }
        catch
        {
            // Best-effort.
        }
    }

    private void PersistWithOverride(CheckedListBox box, List<int> postIndices)
    {
        try
        {
            UiPreferences prefs = UiPreferences.Load();
            prefs.ClarificationIntent = SelectedIntentValue();
            prefs.ClarificationOutput = SelectedOutputValue();

            // Translate predicted indices for the live box; preserve the other.
            if (ReferenceEquals(box, _scope))
            {
                prefs.ClarificationScopes = TranslateIndices(postIndices, ScopeChoices);
                prefs.ClarificationDiagnostics = new List<string>(SelectedDiagnosticValues());
            }
            else
            {
                prefs.ClarificationScopes = new List<string>(SelectedScopeValues());
                prefs.ClarificationDiagnostics = TranslateIndices(postIndices, DiagnosticChoices);
            }

            prefs.ClarificationExpanded = Visible;
            prefs.Save();
        }
        catch
        {
            // Best-effort.
        }
    }

    private static List<string> TranslateIndices(List<int> indices, (string Display, string Value)[] table)
    {
        var result = new List<string>();
        foreach (int i in indices)
        {
            if (i >= 0 && i < table.Length) result.Add(table[i].Value);
        }
        return result;
    }

    private void Persist()
    {
        try
        {
            UiPreferences prefs = UiPreferences.Load();
            prefs.ClarificationIntent = SelectedIntentValue();
            prefs.ClarificationOutput = SelectedOutputValue();
            prefs.ClarificationScopes = new List<string>(SelectedScopeValues());
            prefs.ClarificationDiagnostics = new List<string>(SelectedDiagnosticValues());
            prefs.ClarificationExpanded = Visible;
            prefs.Save();
        }
        catch
        {
            // Best-effort — persistence failure must not crash the UI.
        }
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(ApplyTheme)); } catch { } return; }
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        BackColor = UiPalette.SurfaceBackground;
        ForeColor = UiPalette.TextPrimary;
        foreach (GroupBox g in new[] { _gIntent, _gScope, _gOutput, _gDiag })
        {
            g.BackColor = UiPalette.SurfaceBackground;
            g.ForeColor = UiPalette.TextPrimary;
        }
        foreach (Control c in new Control[] { _intent, _output })
        {
            c.BackColor = UiPalette.InputBackground;
            c.ForeColor = UiPalette.TextPrimary;
        }
        foreach (Control c in new Control[] { _scope, _diagnostics })
        {
            c.BackColor = UiPalette.InputBackground;
            c.ForeColor = UiPalette.TextPrimary;
        }
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UiPalette.ModeChanged -= OnModeChanged;
        }
        base.Dispose(disposing);
    }
}
