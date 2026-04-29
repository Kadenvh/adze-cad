using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Adze.Broker.Configuration;
using Adze.UI.V2;
using NUnit.Framework;

namespace Adze.Tests.UI;

/// <summary>
/// Tests for the rebuilt clarification (Refine intent) panel — chunk 3 of
/// the v1.1 UI rebuild. Covers:
///   - Default state emits no prefix
///   - Picking an Intent emits <c>intent=foo</c>
///   - Multi-selecting Scope emits <c>scope=a;b</c>
///   - Setting Output / Diagnostics emits the rest of the clarification block
///   - Persisted defaults round-trip through <see cref="UiPreferences"/>
///
/// We back up and restore the prefs file so each run is hermetic.
/// </summary>
[TestFixture]
public sealed class RefinementPanelTests
{
    private string _backupPath = string.Empty;
    private bool _hadOriginal;

    [SetUp]
    public void SetUp()
    {
        string path = UiPreferences.GetPath();
        _backupPath = path + ".refine-testbackup-" + Guid.NewGuid().ToString("N");
        if (File.Exists(path))
        {
            File.Move(path, _backupPath);
            _hadOriginal = true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        string path = UiPreferences.GetPath();
        if (File.Exists(path)) File.Delete(path);
        if (_hadOriginal && File.Exists(_backupPath)) File.Move(_backupPath, path);
    }

    [Test]
    public void Defaults_BuildPrefix_IsEmpty()
    {
        using var panel = new RefinementPanel();
        Assert.That(panel.BuildPrefix(), Is.EqualTo(string.Empty),
            "An untouched panel must NOT prepend a no-op prefix.");
    }

    [Test]
    public void IntentSelected_BuildPrefix_EmitsIntentClause()
    {
        using var panel = new RefinementPanel();
        // Find the intent dropdown via reflection-free traversal — the panel
        // exposes structured choices by index. "Diagnostic" is index 1.
        SetIntent(panel, "diagnostic");

        string prefix = panel.BuildPrefix();
        Assert.That(prefix, Does.StartWith("[clarification]"));
        Assert.That(prefix, Does.Contain("intent=diagnostic"));
        Assert.That(prefix, Does.EndWith("[/clarification]"));
    }

    [Test]
    public void ScopesAndOutputAndDiagnostics_AllFlow_IntoPrefix()
    {
        using var panel = new RefinementPanel();
        SetIntent(panel, "modify");
        CheckScope(panel, "active_doc");
        CheckScope(panel, "selected_feature");
        SetOutput(panel, "concise");
        CheckDiagnostic(panel, "tool_calls");

        string prefix = panel.BuildPrefix();
        Assert.That(prefix, Does.Contain("intent=modify"));
        Assert.That(prefix, Does.Contain("scope=active_doc;selected_feature"));
        Assert.That(prefix, Does.Contain("output=concise"));
        Assert.That(prefix, Does.Contain("diagnostics=tool_calls"));
    }

    [Test]
    public void Persisted_Defaults_RoundTripThroughUiPreferences()
    {
        // First panel: select some options. Persistence is implicit on each change.
        using (var panel = new RefinementPanel())
        {
            SetIntent(panel, "inspect");
            CheckScope(panel, "whole_assembly");
            SetOutput(panel, "table");
        }

        // Re-read prefs directly. Each interactive change persists, so the
        // last state is what we expect.
        UiPreferences prefs = UiPreferences.Load();
        Assert.That(prefs.ClarificationIntent, Is.EqualTo("inspect"));
        Assert.That(prefs.ClarificationScopes, Does.Contain("whole_assembly"));
        Assert.That(prefs.ClarificationOutput, Is.EqualTo("table"));

        // Second panel: should pick up the persisted defaults at construction.
        using var panel2 = new RefinementPanel();
        Assert.That(panel2.SelectedIntentValue(), Is.EqualTo("inspect"));
        Assert.That(panel2.SelectedScopeValues(), Does.Contain("whole_assembly"));
        Assert.That(panel2.SelectedOutputValue(), Is.EqualTo("table"));
    }

    [Test]
    public void Toggle_FlipsVisibilityAndPersists()
    {
        using var panel = new RefinementPanel();
        Assert.That(panel.Visible, Is.False, "Panel starts hidden by design.");

        bool first = panel.Toggle();
        Assert.That(first, Is.True);
        Assert.That(panel.Visible, Is.True);

        UiPreferences prefs = UiPreferences.Load();
        Assert.That(prefs.ClarificationExpanded, Is.True,
            "Toggle visible state must persist through UiPreferences.");
    }

    // ─── Helpers — reach into the panel's own combo/listbox children ───
    // The panel exposes Selected* getters; for the setter side we drive the
    // underlying WinForms controls directly via Controls traversal.

    private static void SetIntent(RefinementPanel panel, string value)
    {
        ComboBox combo = FindIntentCombo(panel);
        for (int i = 0; i < RefinementPanel.IntentChoices.Length; i++)
        {
            if (string.Equals(RefinementPanel.IntentChoices[i].Value, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        Assert.Fail("Unknown intent value: " + value);
    }

    private static void SetOutput(RefinementPanel panel, string value)
    {
        ComboBox combo = FindOutputCombo(panel);
        for (int i = 0; i < RefinementPanel.OutputChoices.Length; i++)
        {
            if (string.Equals(RefinementPanel.OutputChoices[i].Value, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        Assert.Fail("Unknown output value: " + value);
    }

    private static void CheckScope(RefinementPanel panel, string value)
    {
        CheckedListBox box = FindScopeList(panel);
        for (int i = 0; i < RefinementPanel.ScopeChoices.Length; i++)
        {
            if (string.Equals(RefinementPanel.ScopeChoices[i].Value, value, StringComparison.OrdinalIgnoreCase))
            {
                box.SetItemChecked(i, true);
                return;
            }
        }
        Assert.Fail("Unknown scope value: " + value);
    }

    private static void CheckDiagnostic(RefinementPanel panel, string value)
    {
        CheckedListBox box = FindDiagnosticsList(panel);
        for (int i = 0; i < RefinementPanel.DiagnosticChoices.Length; i++)
        {
            if (string.Equals(RefinementPanel.DiagnosticChoices[i].Value, value, StringComparison.OrdinalIgnoreCase))
            {
                box.SetItemChecked(i, true);
                return;
            }
        }
        Assert.Fail("Unknown diagnostic value: " + value);
    }

    // ── GroupBox lookup by Text ──
    private static GroupBox FindGroup(RefinementPanel panel, string title)
    {
        // Panel layout: TableLayoutPanel → 4 GroupBoxes (Intent / Scope / Output / Diagnostics)
        TableLayoutPanel? grid = panel.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        Assert.That(grid, Is.Not.Null, "RefinementPanel must own a TableLayoutPanel grid.");
        GroupBox? g = grid!.Controls.OfType<GroupBox>().FirstOrDefault(b => b.Text == title);
        Assert.That(g, Is.Not.Null, $"Group '{title}' missing from refinement panel.");
        return g!;
    }

    private static ComboBox FindIntentCombo(RefinementPanel panel)
        => FindGroup(panel, "Intent").Controls.OfType<ComboBox>().First();

    private static ComboBox FindOutputCombo(RefinementPanel panel)
        => FindGroup(panel, "Output").Controls.OfType<ComboBox>().First();

    private static CheckedListBox FindScopeList(RefinementPanel panel)
        => FindGroup(panel, "Scope").Controls.OfType<CheckedListBox>().First();

    private static CheckedListBox FindDiagnosticsList(RefinementPanel panel)
        => FindGroup(panel, "Diagnostics").Controls.OfType<CheckedListBox>().First();
}
