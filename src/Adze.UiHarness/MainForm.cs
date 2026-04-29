using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Adze.Contracts.Models;
using Adze.UI.V2;

namespace Adze.UiHarness;

/// <summary>
/// Phase 1.5 dev harness window. Mounts:
///   - Snapshot picker (loads SessionContext from %LOCALAPPDATA%\Adze\snapshots\)
///   - Status strip showing current snapshot label + document type
///   - The new <see cref="NativeTaskPaneControl"/> bound against
///     <see cref="StubHostState"/> for hot iteration without SOLIDWORKS
/// </summary>
public sealed class MainForm : Form
{
    private readonly StubHostState _state = new();

    private readonly ComboBox _snapshotPicker;
    private readonly Button _refreshButton;
    private readonly Button _clearButton;
    private readonly Button _injectWriteButton;
    private readonly Label _sourceLabel;
    private readonly Label _docLabel;
    private readonly Panel _surfaceMount;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly NativeTaskPaneControl _sidebar;

    public MainForm()
    {
        Text = "Adze UI Harness — v1.1 sidebar preview";
        Size = new Size(1080, 780);
        MinimumSize = new Size(720, 540);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(247, 248, 250);
        Font = new Font("Segoe UI", 9F);

        // --- Top toolbar: snapshot picker + actions ---
        Panel toolbar = new()
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Color.FromArgb(27, 58, 107),
            Padding = new Padding(12, 12, 12, 12)
        };

        Label pickLabel = new()
        {
            Text = "Snapshot:",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Location = new Point(12, 18),
            AutoSize = true
        };

        _snapshotPicker = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(82, 14),
            Size = new Size(560, 24)
        };
        _snapshotPicker.SelectedIndexChanged += OnSnapshotSelected;

        _refreshButton = new Button
        {
            Text = "Refresh List",
            Location = new Point(652, 13),
            Size = new Size(100, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 114, 198),
            ForeColor = Color.White
        };
        _refreshButton.FlatAppearance.BorderSize = 0;
        _refreshButton.Click += (_, _) => RefreshSnapshotList();

        _clearButton = new Button
        {
            Text = "Clear",
            Location = new Point(760, 13),
            Size = new Size(70, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(108, 117, 125),
            ForeColor = Color.White
        };
        _clearButton.FlatAppearance.BorderSize = 0;
        _clearButton.Click += (_, _) =>
        {
            _state.Clear();
            UpdateStatus();
        };

        _injectWriteButton = new Button
        {
            Text = "Inject Write",
            Location = new Point(840, 13),
            Size = new Size(100, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(245, 158, 11),
            ForeColor = Color.White
        };
        _injectWriteButton.FlatAppearance.BorderSize = 0;
        _injectWriteButton.Click += (_, _) => InjectFakePendingWrite();

        toolbar.Controls.AddRange(new Control[] { pickLabel, _snapshotPicker, _refreshButton, _clearButton, _injectWriteButton });

        // --- Sidebar info strip ---
        Panel infoStrip = new()
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = Color.FromArgb(232, 236, 240),
            Padding = new Padding(14, 8, 14, 8)
        };

        _sourceLabel = new Label
        {
            Text = "Source: (no snapshot loaded)",
            Location = new Point(14, 8),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _docLabel = new Label
        {
            Text = "Document: (none)",
            Location = new Point(14, 26),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(80, 90, 110)
        };

        infoStrip.Controls.AddRange(new Control[] { _sourceLabel, _docLabel });

        // --- Surface mount: NativeTaskPaneControl bound to StubHostState ---
        _surfaceMount = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None
        };

        _sidebar = new NativeTaskPaneControl(_state) { Dock = DockStyle.Fill };
        _surfaceMount.Controls.Add(_sidebar);

        // --- Status strip ---
        _statusLabel = new ToolStripStatusLabel("Ready");
        _statusStrip = new StatusStrip();
        _statusStrip.Items.Add(_statusLabel);

        // Order matters for docking — add in reverse z-order
        Controls.Add(_surfaceMount);
        Controls.Add(infoStrip);
        Controls.Add(toolbar);
        Controls.Add(_statusStrip);

        Load += (_, _) => RefreshSnapshotList();
    }

    private void RefreshSnapshotList()
    {
        _snapshotPicker.Items.Clear();
        string[] files = SnapshotLoader.EnumerateRecent(50);
        foreach (string path in files)
        {
            _snapshotPicker.Items.Add(new SnapshotItem(path));
        }

        if (_snapshotPicker.Items.Count == 0)
        {
            _statusLabel.Text = "No snapshots found in " + SnapshotLoader.DefaultSnapshotDir +
                                ". Run Adze in SOLIDWORKS once to generate one.";
        }
        else
        {
            _statusLabel.Text = "Loaded " + _snapshotPicker.Items.Count + " snapshot(s) from disk.";
        }
    }

    private void OnSnapshotSelected(object? sender, EventArgs e)
    {
        if (_snapshotPicker.SelectedItem is not SnapshotItem item)
        {
            return;
        }

        SessionContext? ctx = SnapshotLoader.LoadFromFile(item.FullPath, out string? error);
        if (ctx == null)
        {
            MessageBox.Show(this, "Failed to load snapshot:\n" + error, "Snapshot load error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _state.LoadContext(ctx, item.DisplayLabel);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _sourceLabel.Text = "Source: " + _state.SourceLabel;
        if (_state.CurrentContext == null)
        {
            _docLabel.Text = "Document: (none)";
            _statusLabel.Text = "State cleared.";
            return;
        }

        _docLabel.Text = "Document: " + _state.DocumentSummary;
        _statusLabel.Text = "Snapshot loaded: " + _state.SourceLabel;
    }

    private void InjectFakePendingWrite()
    {
        var write = new Adze.Contracts.Models.PendingWriteAction
        {
            WriteId = Guid.NewGuid().ToString("N").Substring(0, 8),
            ToolName = "set_dimension_value",
            Preview = new WritePreview
            {
                ToolName = "set_dimension_value",
                Summary = "Change D1@Sketch1 from 25.000mm to 30.000mm",
                Changes = new List<WriteChangeItem>
                {
                    new() { TargetLabel = "D1@Sketch1", BeforeValue = "25.000mm", AfterValue = "30.000mm" }
                },
                Warnings = new List<string> { "This dimension may affect downstream features (Boss-Extrude1)." }
            },
            UndoLabel = "Modify Dimension"
        };
        _state.InjectPendingWrite(write);
    }

    private sealed class SnapshotItem
    {
        public string FullPath { get; }
        public string DisplayLabel { get; }

        public SnapshotItem(string fullPath)
        {
            FullPath = fullPath;
            FileInfo info = new(fullPath);
            DisplayLabel = info.Name + "  (" + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + ")";
        }

        public override string ToString() => DisplayLabel;
    }
}
