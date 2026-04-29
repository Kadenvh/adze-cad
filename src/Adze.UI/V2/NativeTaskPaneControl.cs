using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.UI.V2;

/// <summary>
/// v1.1 native sidebar control. Replaces the old <c>TaskPaneControl</c> WebBrowser
/// monolith with a pure-WinForms surface.
///
/// Layout (top → bottom):
///   - Banner host (probe / health / budget — placeholders for future chunks)
///   - Top tab strip (Chat / Plan / Settings)
///   - Active tab content fills the remainder
///
/// Chat tab (the daily-driver Claude/ChatGPT-shape):
///   - Document header (neutral strip — no more dark navy)
///   - Conversation thread (FlowLayoutPanel, scroll-to-bottom on new content)
///   - Optional pending-write cards above the prompt
///   - Run-state strip (Refine intent + status)
///   - Prompt input row (multiline, Enter submits, Shift+Enter newlines)
///   - Quick-actions chip row pinned BELOW the prompt input — per founder's call
///
/// Plan tab:
///   - Read-only RichTextBox showing agent reasoning
///   - ListView showing tool execution sequence (Step / Tool / Status / Duration)
///
/// Settings tab:
///   - Provider section (read-only label + "Open Manager" hint)
///   - Quick toggle CheckBoxes for the 3 most-used feature gates
///   - "Refine intent" placeholder button (rebuild deferred to chunk 3)
/// </summary>
public sealed class NativeTaskPaneControl : UserControl
{
    private readonly ITaskPaneHost _host;

    // ─── Banner host ───
    private readonly Panel _bannerHost;
    // Placeholder slots — future chunk wires probe/health/budget banners here.
    private readonly Panel _probeBannerHost;
    private readonly Panel _healthBannerHost;
    private readonly Panel _budgetBannerHost;

    // ─── Tabs ───
    private readonly TabControl _tabs;
    private readonly TabPage _chatTab;
    private readonly TabPage _planTab;
    private readonly TabPage _settingsTab;

    // ─── Chat tab ───
    private readonly Label _docHeader;
    // Reserved for the chunk-3 doc-header strip rebuild — kept as a hosted
    // layout slot. Suppress unused-field warning so the slot can be wired
    // without churn elsewhere.
#pragma warning disable CS0169
    private readonly Panel? _docHeaderRow;
#pragma warning restore CS0169
    private readonly FlowLayoutPanel _conversation;
    private readonly FlowLayoutPanel _pendingWritesPanel;
    private readonly Panel _writePlanHeader;
    private readonly Button _applyAllBtn;
    private readonly Button _cancelAllBtn;
    private readonly TextBox _promptInput;
    private readonly Button _submitBtn;
    private readonly Button _cancelRunBtn;
    private readonly Label _runStateLabel;
    private readonly Button _refineIntentBtn;
    // Quick-actions chip row pinned below the prompt input — wired in ctor.
    private readonly QuickActionsBar _quickActions;

    // ─── Plan tab ───
    private readonly RichTextBox _planReasoning;
    private readonly ListView _toolExecutionList;

    // ─── Settings tab ───
    private readonly Label _providerLabel;
    private readonly LinkLabel _openManagerLink;
    private readonly CheckBox _streamingToggle;
    private readonly CheckBox _agentLoopToggle;
    private readonly CheckBox _writesToggle;
    private readonly Label _settingsNote;
    private readonly RadioButton _appearanceLight;
    private readonly RadioButton _appearanceDark;
    private readonly RadioButton _appearanceSystem;

    // ─── Refinement panel (new in chunk 3) ───
    private readonly RefinementPanel _refinementPanel;

    // ─── State ───
    private CancellationTokenSource? _runCts;
    private ChatMessageView? _activeStreamingView;
    private int _toolStepCount;

    public NativeTaskPaneControl(ITaskPaneHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));

        Dock = DockStyle.Fill;
        BackColor = UiPalette.SurfaceBackground;
        Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize);
        ForeColor = UiPalette.TextPrimary;

        // ───────── Banner host (top) ─────────
        _probeBannerHost = new Panel { Dock = DockStyle.Top, Height = 0, Visible = false };
        _healthBannerHost = new Panel { Dock = DockStyle.Top, Height = 0, Visible = false };
        _budgetBannerHost = new Panel { Dock = DockStyle.Top, Height = 0, Visible = false };

        _bannerHost = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.SurfaceBackground
        };
        _bannerHost.Controls.Add(_budgetBannerHost);
        _bannerHost.Controls.Add(_healthBannerHost);
        _bannerHost.Controls.Add(_probeBannerHost);

        // ───────── Tabs (clean strip — accent indicator on active) ─────────
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Alignment = TabAlignment.Top,
            Appearance = TabAppearance.Normal,
            ItemSize = new Size(110, 28),
            SizeMode = TabSizeMode.Fixed,
            Padding = new Point(14, 6),
            DrawMode = TabDrawMode.OwnerDrawFixed
        };
        _tabs.DrawItem += DrawTab;
        _chatTab = new TabPage("Chat") { BackColor = UiPalette.SurfaceBackground, Padding = new Padding(0) };
        _planTab = new TabPage("Plan") { BackColor = UiPalette.SurfaceBackground, Padding = new Padding(8) };
        _settingsTab = new TabPage("Settings") { BackColor = UiPalette.SurfaceBackground, Padding = new Padding(14) };
        _tabs.TabPages.Add(_chatTab);
        _tabs.TabPages.Add(_planTab);
        _tabs.TabPages.Add(_settingsTab);

        // ───────── Chat tab ─────────

        // Quick-actions chip row sits at the very bottom of the chat tab,
        // BENEATH the prompt input (founder's explicit placement call).
        _quickActions = new QuickActionsBar(_host)
        {
            Dock = DockStyle.Bottom
        };

        // Prompt input row
        Panel promptRow = new()
        {
            Dock = DockStyle.Bottom,
            Height = 96,
            BackColor = UiPalette.SurfaceBackground,
            Padding = new Padding(12, 8, 12, 8)
        };

        // Subtle 1px border around the textbox via a wrapping panel.
        Panel inputBorder = new()
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.InputBorder,
            Padding = new Padding(1)
        };

        _promptInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = false,   // we handle Enter ourselves
            AcceptsTab = false,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
            BorderStyle = BorderStyle.None,
            BackColor = UiPalette.InputBackground,
            ForeColor = UiPalette.TextPrimary
        };
        _promptInput.KeyDown += OnPromptKeyDown;
        inputBorder.Controls.Add(_promptInput);

        Panel buttonStack = new()
        {
            Dock = DockStyle.Right,
            Width = 104,
            BackColor = UiPalette.SurfaceBackground,
            Padding = new Padding(8, 0, 0, 0)
        };

        _submitBtn = new Button
        {
            Text = "Submit",
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = UiPalette.Accent,
            ForeColor = UiPalette.AccentForeground,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(UiPalette.FontFamily, UiPalette.ButtonFontSize, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _submitBtn.FlatAppearance.BorderSize = 0;
        _submitBtn.FlatAppearance.MouseOverBackColor = UiPalette.AccentDark;
        _submitBtn.Click += (_, _) => SubmitPrompt();

        _cancelRunBtn = new Button
        {
            Text = "Cancel",
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = UiPalette.SubtleButtonBackground,
            ForeColor = UiPalette.SubtleButtonForeground,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(UiPalette.FontFamily, UiPalette.ButtonFontSize),
            Visible = false,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _cancelRunBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _cancelRunBtn.FlatAppearance.BorderSize = 1;
        _cancelRunBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;
        _cancelRunBtn.Click += (_, _) => CancelCurrentRun();

        buttonStack.Controls.Add(_cancelRunBtn);
        buttonStack.Controls.Add(_submitBtn);

        promptRow.Controls.Add(inputBorder);
        promptRow.Controls.Add(buttonStack);

        // Refine-intent + run state above the prompt row
        Panel runStatePanel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = UiPalette.SurfaceBackground,
            Padding = new Padding(12, 4, 12, 4)
        };

        _runStateLabel = new Label
        {
            Text = "Ready.",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiPalette.TextSecondary,
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize)
        };

        _refineIntentBtn = new Button
        {
            Text = "Refine intent",
            Dock = DockStyle.Right,
            Width = 110,
            Height = 22,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiPalette.SubtleButtonBackground,
            ForeColor = UiPalette.SubtleButtonForeground,
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _refineIntentBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _refineIntentBtn.FlatAppearance.BorderSize = 1;
        _refineIntentBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;
        _refineIntentBtn.Click += (_, _) => ToggleRefinementPanel();

        // Refinement panel — pinned ABOVE prompt input (between QuickActions
        // chips and the prompt row). Hidden by default; the "Refine intent"
        // button toggles visibility. See RefinementPanel for layout details.
        _refinementPanel = new RefinementPanel { Dock = DockStyle.Bottom };

        runStatePanel.Controls.Add(_runStateLabel);
        runStatePanel.Controls.Add(_refineIntentBtn);

        // Pending writes (above prompt + run state)
        _pendingWritesPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiPalette.SurfaceBackground,
            Height = 0,
            Visible = false
        };

        _writePlanHeader = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 0,
            BackColor = UiPalette.AccentTint,
            Padding = new Padding(12, 6, 12, 6),
            Visible = false
        };

        Label planTitle = new()
        {
            Text = "Write Plan",
            Dock = DockStyle.Left,
            AutoSize = true,
            Font = new Font(UiPalette.FontFamily, UiPalette.HeaderFontSize, FontStyle.Bold),
            ForeColor = UiPalette.AccentDark,
            Margin = new Padding(0, 4, 0, 0)
        };

        _applyAllBtn = new Button
        {
            Text = "Apply All",
            Dock = DockStyle.Right,
            Width = 96,
            BackColor = UiPalette.Accent,
            ForeColor = UiPalette.AccentForeground,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(UiPalette.FontFamily, UiPalette.ButtonFontSize, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _applyAllBtn.FlatAppearance.BorderSize = 0;
        _applyAllBtn.FlatAppearance.MouseOverBackColor = UiPalette.AccentDark;
        _applyAllBtn.Click += (_, _) => ApplyAllPendingWrites();

        _cancelAllBtn = new Button
        {
            Text = "Cancel All",
            Dock = DockStyle.Right,
            Width = 96,
            BackColor = UiPalette.SubtleButtonBackground,
            ForeColor = UiPalette.SubtleButtonForeground,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(UiPalette.FontFamily, UiPalette.ButtonFontSize),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _cancelAllBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _cancelAllBtn.FlatAppearance.BorderSize = 1;
        _cancelAllBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;
        _cancelAllBtn.Click += (_, _) => CancelAllPendingWrites();

        _writePlanHeader.Controls.Add(_cancelAllBtn);
        _writePlanHeader.Controls.Add(_applyAllBtn);
        _writePlanHeader.Controls.Add(planTitle);

        // Top: document header (neutral strip + 1px hairline beneath)
        Panel docHeaderRow = new()
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = UiPalette.HeaderBackground,
            Padding = new Padding(0, 0, 0, 1)
        };
        Panel headerBottomBorder = new()
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = UiPalette.HeaderBorder
        };
        _docHeader = new Label
        {
            Dock = DockStyle.Fill,
            Text = "(no document)",
            Font = new Font(UiPalette.FontFamily, UiPalette.DocHeaderFontSize, FontStyle.Bold),
            ForeColor = UiPalette.HeaderForeground,
            BackColor = UiPalette.HeaderBackground,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 14, 0),
            AutoEllipsis = true
        };
        docHeaderRow.Controls.Add(_docHeader);
        docHeaderRow.Controls.Add(headerBottomBorder);

        // Middle: conversation thread
        _conversation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = UiPalette.SurfaceBackground,
            Padding = new Padding(0, 6, 0, 6)
        };

        // Z-order for chat tab — visual top→bottom is:
        //   Doc header → Conversation (Fill) → Pending writes → Refinement
        //   panel (when expanded) → Run state → Prompt input → Quick-action
        //   chips.
        // Bottom-docked controls layer last-added-on-top: chips MUST be added
        // BEFORE promptRow so chips end up *below* promptRow in the visual
        // stack. Refinement panel sits between promptRow and runStatePanel so
        // expanding it pushes the conversation up rather than covering the
        // input or the chips.
        _chatTab.Controls.Add(_conversation);
        _chatTab.Controls.Add(_writePlanHeader);
        _chatTab.Controls.Add(_pendingWritesPanel);
        _chatTab.Controls.Add(_quickActions);
        _chatTab.Controls.Add(promptRow);
        _chatTab.Controls.Add(_refinementPanel);
        _chatTab.Controls.Add(runStatePanel);
        _chatTab.Controls.Add(docHeaderRow);

        // ───────── Plan tab ─────────
        _planReasoning = new RichTextBox
        {
            Dock = DockStyle.Top,
            Height = 168,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
            BackColor = UiPalette.CardBackground,
            ForeColor = UiPalette.TextPrimary,
            Text = "(no agent run yet)"
        };

        _toolExecutionList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UiPalette.CardBackground,
            ForeColor = UiPalette.TextPrimary,
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize)
        };
        _toolExecutionList.Columns.Add("Step", 44);
        _toolExecutionList.Columns.Add("Tool", 200);
        _toolExecutionList.Columns.Add("Status", 90);
        _toolExecutionList.Columns.Add("Duration", 80);

        _planTab.Controls.Add(_toolExecutionList);
        _planTab.Controls.Add(_planReasoning);

        // ───────── Settings tab ─────────
        Label sectionProvider = new()
        {
            Text = "Provider",
            Font = new Font(UiPalette.FontFamily, UiPalette.HeaderFontSize, FontStyle.Bold),
            ForeColor = UiPalette.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0)
        };

        _providerLabel = new Label
        {
            Text = "Provider: " + ResolveProviderName(),
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
            AutoSize = true,
            Location = new Point(0, 26),
            ForeColor = UiPalette.TextSecondary
        };

        _openManagerLink = new LinkLabel
        {
            Text = "Open Manager → Settings for full configuration",
            Font = new Font(UiPalette.FontFamily, UiPalette.BodyFontSize),
            AutoSize = true,
            Location = new Point(0, 52),
            LinkColor = UiPalette.Accent,
            ActiveLinkColor = UiPalette.AccentDark,
            VisitedLinkColor = UiPalette.AccentDark
        };
        _openManagerLink.LinkClicked += (_, _) =>
        {
            MessageBox.Show(this,
                "Launch Adze.Manager.exe to access full configuration. " +
                "Auto-launch from the sidebar is wired in a future chunk.",
                "Open Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        Label sectionGates = new()
        {
            Text = "Quick toggles",
            Font = new Font(UiPalette.FontFamily, UiPalette.HeaderFontSize, FontStyle.Bold),
            ForeColor = UiPalette.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 92)
        };

        _streamingToggle = new CheckBox
        {
            Text = "Stream final answer text",
            AutoSize = true,
            Location = new Point(0, 120),
            ForeColor = UiPalette.TextPrimary,
            Checked = FeatureGateRegistry.IsEnabled(FeatureGateRegistry.StreamFinalText)
        };
        _streamingToggle.CheckedChanged += (_, _) => SaveGate(FeatureGateRegistry.StreamFinalText, _streamingToggle.Checked);

        _agentLoopToggle = new CheckBox
        {
            Text = "Agent loop (model-driven tool calling)",
            AutoSize = true,
            Location = new Point(0, 144),
            ForeColor = UiPalette.TextPrimary,
            Checked = FeatureGateRegistry.IsEnabled(FeatureGateRegistry.AgentLoop)
        };
        _agentLoopToggle.CheckedChanged += (_, _) => SaveGate(FeatureGateRegistry.AgentLoop, _agentLoopToggle.Checked);

        _writesToggle = new CheckBox
        {
            Text = "First-wave write tools enabled",
            AutoSize = true,
            Location = new Point(0, 168),
            ForeColor = UiPalette.TextPrimary,
            Checked = FeatureGateRegistry.IsEnabled(FeatureGateRegistry.FirstWaveWrites)
        };
        _writesToggle.CheckedChanged += (_, _) => SaveGate(FeatureGateRegistry.FirstWaveWrites, _writesToggle.Checked);

        // ── Appearance group (chunk 3) — Light / Dark / System ──
        Label sectionAppearance = new()
        {
            Text = "Appearance",
            Font = new Font(UiPalette.FontFamily, UiPalette.HeaderFontSize, FontStyle.Bold),
            ForeColor = UiPalette.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 204)
        };

        _appearanceLight = new RadioButton
        {
            Text = "Light",
            AutoSize = true,
            Location = new Point(0, 232),
            ForeColor = UiPalette.TextPrimary,
            Checked = UiPalette.CurrentMode == UiPalette.UiMode.Light
        };
        _appearanceDark = new RadioButton
        {
            Text = "Dark",
            AutoSize = true,
            Location = new Point(80, 232),
            ForeColor = UiPalette.TextPrimary,
            Checked = UiPalette.CurrentMode == UiPalette.UiMode.Dark
        };
        _appearanceSystem = new RadioButton
        {
            Text = "System",
            AutoSize = true,
            Location = new Point(160, 232),
            ForeColor = UiPalette.TextPrimary,
            Checked = UiPalette.CurrentMode == UiPalette.UiMode.System
        };
        _appearanceLight.CheckedChanged += (_, _) => { if (_appearanceLight.Checked) UiPalette.SetMode(UiPalette.UiMode.Light); };
        _appearanceDark.CheckedChanged += (_, _) => { if (_appearanceDark.Checked) UiPalette.SetMode(UiPalette.UiMode.Dark); };
        _appearanceSystem.CheckedChanged += (_, _) => { if (_appearanceSystem.Checked) UiPalette.SetMode(UiPalette.UiMode.System); };

        _settingsNote = new Label
        {
            Text = "Full settings live in Adze.Manager. Changes here require SOLIDWORKS reload to take " +
                   "effect (known issue: settings-save-requires-reload).",
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize, FontStyle.Italic),
            ForeColor = UiPalette.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Location = new Point(0, 264)
        };

        _settingsTab.Controls.Add(sectionProvider);
        _settingsTab.Controls.Add(_providerLabel);
        _settingsTab.Controls.Add(_openManagerLink);
        _settingsTab.Controls.Add(sectionGates);
        _settingsTab.Controls.Add(_streamingToggle);
        _settingsTab.Controls.Add(_agentLoopToggle);
        _settingsTab.Controls.Add(_writesToggle);
        _settingsTab.Controls.Add(sectionAppearance);
        _settingsTab.Controls.Add(_appearanceLight);
        _settingsTab.Controls.Add(_appearanceDark);
        _settingsTab.Controls.Add(_appearanceSystem);
        _settingsTab.Controls.Add(_settingsNote);

        // ───────── Final assembly ─────────
        Controls.Add(_tabs);
        Controls.Add(_bannerHost);

        // ───────── Wire host events ─────────
        _host.StateChanged += OnHostStateChanged;
        _host.StreamChunkReceived += OnStreamChunk;
        _host.ToolProgress += OnToolProgress;

        // ───────── Wire palette / mode events ─────────
        UiPalette.ModeChanged += OnPaletteModeChanged;

        // Initial render
        Load += (_, _) => RefreshFromHost();
    }

    /// <summary>
    /// Re-applies palette colours to every painted surface in the sidebar.
    /// Triggered when the user flips Light / Dark / System in either the
    /// sidebar Settings tab or the Manager. Child controls (chat bubbles,
    /// write cards, refinement panel, quick-actions) subscribe individually
    /// and repaint themselves; this method handles the top-level chrome.
    /// </summary>
    private void OnPaletteModeChanged(object? sender, EventArgs e)
    {
        InvokeOnUiThread(ApplyPalette);
    }

    private void ApplyPalette()
    {
        BackColor = UiPalette.SurfaceBackground;
        ForeColor = UiPalette.TextPrimary;
        _bannerHost.BackColor = UiPalette.SurfaceBackground;

        _chatTab.BackColor = UiPalette.SurfaceBackground;
        _planTab.BackColor = UiPalette.SurfaceBackground;
        _settingsTab.BackColor = UiPalette.SurfaceBackground;

        _conversation.BackColor = UiPalette.SurfaceBackground;
        _pendingWritesPanel.BackColor = UiPalette.SurfaceBackground;
        _writePlanHeader.BackColor = UiPalette.AccentTint;

        _docHeader.BackColor = UiPalette.HeaderBackground;
        _docHeader.ForeColor = UiPalette.HeaderForeground;

        _runStateLabel.ForeColor = UiPalette.TextSecondary;

        _promptInput.BackColor = UiPalette.InputBackground;
        _promptInput.ForeColor = UiPalette.TextPrimary;

        _submitBtn.BackColor = UiPalette.Accent;
        _submitBtn.ForeColor = UiPalette.AccentForeground;
        _submitBtn.FlatAppearance.MouseOverBackColor = UiPalette.AccentDark;

        _cancelRunBtn.BackColor = UiPalette.SubtleButtonBackground;
        _cancelRunBtn.ForeColor = UiPalette.SubtleButtonForeground;
        _cancelRunBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _cancelRunBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;

        _refineIntentBtn.BackColor = UiPalette.SubtleButtonBackground;
        _refineIntentBtn.ForeColor = UiPalette.SubtleButtonForeground;
        _refineIntentBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _refineIntentBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;

        _applyAllBtn.BackColor = UiPalette.Accent;
        _applyAllBtn.ForeColor = UiPalette.AccentForeground;
        _applyAllBtn.FlatAppearance.MouseOverBackColor = UiPalette.AccentDark;
        _cancelAllBtn.BackColor = UiPalette.SubtleButtonBackground;
        _cancelAllBtn.ForeColor = UiPalette.SubtleButtonForeground;
        _cancelAllBtn.FlatAppearance.BorderColor = UiPalette.SubtleButtonBorder;
        _cancelAllBtn.FlatAppearance.MouseOverBackColor = UiPalette.SubtleButtonHover;

        _planReasoning.BackColor = UiPalette.CardBackground;
        _planReasoning.ForeColor = UiPalette.TextPrimary;
        _toolExecutionList.BackColor = UiPalette.CardBackground;
        _toolExecutionList.ForeColor = UiPalette.TextPrimary;

        _providerLabel.ForeColor = UiPalette.TextSecondary;
        _openManagerLink.LinkColor = UiPalette.Accent;
        _openManagerLink.ActiveLinkColor = UiPalette.AccentDark;
        _openManagerLink.VisitedLinkColor = UiPalette.AccentDark;
        _streamingToggle.ForeColor = UiPalette.TextPrimary;
        _agentLoopToggle.ForeColor = UiPalette.TextPrimary;
        _writesToggle.ForeColor = UiPalette.TextPrimary;
        _appearanceLight.ForeColor = UiPalette.TextPrimary;
        _appearanceDark.ForeColor = UiPalette.TextPrimary;
        _appearanceSystem.ForeColor = UiPalette.TextPrimary;
        _settingsNote.ForeColor = UiPalette.TextSecondary;

        Invalidate(true);
        _tabs.Invalidate(true);
    }

    /// <summary>
    /// Toggle the inline refinement panel visibility. Called from the
    /// "Refine intent" button click handler. Adjusts the button label and
    /// persists expanded state via <see cref="RefinementPanel.Toggle"/>.
    /// </summary>
    private void ToggleRefinementPanel()
    {
        bool nowVisible = _refinementPanel.Toggle();
        _refineIntentBtn.Text = nowVisible ? "Hide intent" : "Refine intent";
    }

    /// <summary>
    /// Read-only access to the embedded refinement panel — exposed so unit
    /// tests can probe panel state and verify prefix-build output.
    /// </summary>
    public RefinementPanel RefinementPanelControl => _refinementPanel;

    /// <summary>
    /// Read-only access to the quick-actions chip row mounted under the prompt
    /// input. Exposed so unit tests and the harness can introspect chip wiring.
    /// </summary>
    public QuickActionsBar QuickActions => _quickActions;

    // ─────────────────────────── Owner-draw tab strip ───────────────────────────

    /// <summary>
    /// Owner-draw handler for the top tab strip — renders active/inactive states
    /// with the v1.1 indigo accent indicator under the active tab. Invoked by
    /// <see cref="TabControl.DrawItem"/> on each tab repaint.
    /// </summary>
    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (_tabs == null || e.Index < 0 || e.Index >= _tabs.TabPages.Count) return;
        TabPage page = _tabs.TabPages[e.Index];
        Rectangle bounds = _tabs.GetTabRect(e.Index);
        bool isActive = e.Index == _tabs.SelectedIndex;

        using (SolidBrush bg = new(isActive ? UiPalette.TabActiveBackground : UiPalette.TabStripBackground))
        {
            e.Graphics.FillRectangle(bg, bounds);
        }

        Color fg = isActive ? UiPalette.TabActiveForeground : UiPalette.TabInactiveForeground;
        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            new Font(UiPalette.FontFamily, UiPalette.BodyFontSize, isActive ? FontStyle.Bold : FontStyle.Regular),
            bounds,
            fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (isActive)
        {
            using SolidBrush indicator = new(UiPalette.TabIndicator);
            e.Graphics.FillRectangle(indicator, bounds.Left, bounds.Bottom - 3, bounds.Width, 3);
        }
    }

    // ─────────────────────────── Host event handlers ───────────────────────────

    private void OnHostStateChanged(object? sender, EventArgs e)
    {
        InvokeOnUiThread(RefreshFromHost);
    }

    private void OnStreamChunk(object? sender, StreamChunkEventArgs e)
    {
        InvokeOnUiThread(() =>
        {
            EnsureStreamingView();
            if (e.IsFinal)
            {
                // Atomic finalize when the host signals the run is done. If
                // the chunk carries the full final markdown, use it; otherwise
                // leave the buffered streamed text in place. Either way we
                // dismiss the streaming view + reset run mode here so we
                // don't show a brief raw-text flash before StateChanged
                // re-renders the conversation thread from ChatHistory.
                if (!string.IsNullOrEmpty(e.Chunk))
                {
                    _activeStreamingView?.FinalizeStream(e.Chunk);
                }
                _runStateLabel.Text = "Streaming complete.";
                _activeStreamingView = null;
                ToggleRunMode(false);
            }
            else
            {
                _activeStreamingView?.AppendStreamChunk(e.Chunk);
            }
            ScrollConversationToBottom();
        });
    }

    private void OnToolProgress(object? sender, ToolProgressEventArgs e)
    {
        InvokeOnUiThread(() =>
        {
            string desc = string.IsNullOrEmpty(e.ToolName)
                ? e.Description
                : (e.Status + ": " + e.ToolName);
            _runStateLabel.Text = desc;

            // Update or insert ListView row for this step
            ListViewItem? existing = null;
            foreach (ListViewItem item in _toolExecutionList.Items)
            {
                if (item.Tag is int step && step == e.Step)
                {
                    existing = item;
                    break;
                }
            }
            if (existing == null)
            {
                _toolStepCount = Math.Max(_toolStepCount, e.Step);
                existing = new ListViewItem(e.Step.ToString())
                {
                    Tag = e.Step
                };
                existing.SubItems.Add(e.ToolName ?? "(thinking)");
                existing.SubItems.Add(e.Status);
                existing.SubItems.Add(e.DurationMs.HasValue ? e.DurationMs.Value + "ms" : string.Empty);
                _toolExecutionList.Items.Add(existing);
            }
            else
            {
                existing.SubItems[1].Text = e.ToolName ?? "(thinking)";
                existing.SubItems[2].Text = e.Status;
                existing.SubItems[3].Text = e.DurationMs.HasValue ? e.DurationMs.Value + "ms" : string.Empty;
            }
        });
    }

    // ─────────────────────────── Refresh from host ───────────────────────────

    private void RefreshFromHost()
    {
        _docHeader.Text = string.IsNullOrWhiteSpace(_host.DocumentSummary)
            ? "(no document)"
            : _host.DocumentSummary;

        RebuildConversation();
        RebuildPendingWrites();
    }

    private void RebuildConversation()
    {
        _conversation.SuspendLayout();
        try
        {
            _conversation.Controls.Clear();
            foreach (ChatEntry entry in _host.ChatHistory)
            {
                if (!string.IsNullOrEmpty(entry.UserMessage))
                {
                    var userView = new ChatMessageView(ChatMessageView.MessageRole.User, entry.UserMessage)
                    {
                        Width = _conversation.ClientSize.Width - 8
                    };
                    _conversation.Controls.Add(userView);
                }
                if (!string.IsNullOrEmpty(entry.AssistantMessage))
                {
                    var asstView = new ChatMessageView(
                        ChatMessageView.MessageRole.Assistant,
                        entry.AssistantMessage,
                        entry.Footer)
                    {
                        Width = _conversation.ClientSize.Width - 8
                    };
                    _conversation.Controls.Add(asstView);
                }
            }
        }
        finally
        {
            _conversation.ResumeLayout();
            ScrollConversationToBottom();
        }
    }

    private void RebuildPendingWrites()
    {
        _pendingWritesPanel.SuspendLayout();
        _pendingWritesPanel.Controls.Clear();
        var actionable = new List<PendingWriteAction>();
        foreach (PendingWriteAction action in _host.PendingWrites)
        {
            if (action.Applied || action.Cancelled) continue;
            actionable.Add(action);
        }
        foreach (PendingWriteAction action in actionable)
        {
            var card = new WriteCardView(action) { Width = _pendingWritesPanel.ClientSize.Width - 8 };
            card.ApplyRequested += (_, id) => _host.ApplyPendingWrite(id);
            card.CancelRequested += (_, id) => _host.CancelPendingWrite(id);
            _pendingWritesPanel.Controls.Add(card);
        }
        _pendingWritesPanel.ResumeLayout();

        bool hasAny = actionable.Count > 0;
        bool hasMany = actionable.Count >= 2;
        _pendingWritesPanel.Visible = hasAny;
        _pendingWritesPanel.Height = hasAny ? 220 : 0;
        _writePlanHeader.Visible = hasMany;
        _writePlanHeader.Height = hasMany ? 32 : 0;
    }

    // ─────────────────────────── Prompt submit / cancel ───────────────────────────

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            if (e.Shift)
            {
                // Shift+Enter inserts a newline manually since AcceptsReturn is false.
                int caret = _promptInput.SelectionStart;
                _promptInput.Text = _promptInput.Text.Insert(caret, Environment.NewLine);
                _promptInput.SelectionStart = caret + Environment.NewLine.Length;
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            SubmitPrompt();
        }
    }

    private void SubmitPrompt()
    {
        string query = (_promptInput.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(query)) return;

        // Prepend clarification prefix from the refinement panel (when expanded
        // and at least one option is set). The prefix is the same
        // [clarification]intent=...,scope=...,output=...,diagnostics=...
        // [/clarification] block the broker's KeywordBrokerOrchestrator already
        // parses (it reads intent= and ignores unknown keys).
        string finalQuery = ComposeQueryWithPrefix(query);

        _promptInput.Clear();
        ToggleRunMode(true);
        _runStateLabel.Text = "Thinking…";
        _toolExecutionList.Items.Clear();
        _toolStepCount = 0;

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        try
        {
            _host.SubmitUserQuery(finalQuery, _runCts.Token);
        }
        catch (Exception ex)
        {
            ToggleRunMode(false);
            _runStateLabel.Text = "Error: " + ex.Message;
        }
    }

    /// <summary>
    /// Composes the final user query by prepending the refinement panel's
    /// clarification prefix (when one is built). Public so tests can validate
    /// prefix concatenation independently of the host wiring.
    /// </summary>
    public string ComposeQueryWithPrefix(string rawQuery)
    {
        if (rawQuery == null) return string.Empty;
        if (!_refinementPanel.Visible) return rawQuery;
        string prefix = _refinementPanel.BuildPrefix();
        if (string.IsNullOrEmpty(prefix)) return rawQuery;
        return prefix + " " + rawQuery;
    }

    private void CancelCurrentRun()
    {
        try
        {
            _runCts?.Cancel();
            _host.CancelCurrentRun();
        }
        catch (Exception ex)
        {
            _runStateLabel.Text = "Cancel error: " + ex.Message;
            return;
        }
        ToggleRunMode(false);
        _runStateLabel.Text = "Cancelled.";
    }

    private void ToggleRunMode(bool running)
    {
        _submitBtn.Enabled = !running;
        _cancelRunBtn.Visible = running;
        _promptInput.ReadOnly = running;
    }

    private void EnsureStreamingView()
    {
        if (_activeStreamingView != null) return;
        var view = new ChatMessageView(ChatMessageView.MessageRole.Assistant, string.Empty)
        {
            Width = _conversation.ClientSize.Width - 8
        };
        _conversation.Controls.Add(view);
        _activeStreamingView = view;
    }

    private void ScrollConversationToBottom()
    {
        if (_conversation.Controls.Count == 0) return;
        Control last = _conversation.Controls[_conversation.Controls.Count - 1];
        _conversation.ScrollControlIntoView(last);
    }

    // ─────────────────────────── Pending-write batch actions ───────────────────────────

    private void ApplyAllPendingWrites()
    {
        foreach (Control c in _pendingWritesPanel.Controls)
        {
            if (c is WriteCardView card && !card.Action.Applied && !card.Action.Cancelled)
            {
                _host.ApplyPendingWrite(card.Action.WriteId);
            }
        }
    }

    private void CancelAllPendingWrites()
    {
        foreach (Control c in _pendingWritesPanel.Controls)
        {
            if (c is WriteCardView card && !card.Action.Applied && !card.Action.Cancelled)
            {
                _host.CancelPendingWrite(card.Action.WriteId);
            }
        }
    }

    // ─────────────────────────── Settings helpers ───────────────────────────

    private static string ResolveProviderName()
    {
        string? p = Environment.GetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER");
        if (string.IsNullOrWhiteSpace(p)) return "(auto-detect)";
        return p;
    }

    private static void SaveGate(string gateName, bool value)
    {
        try
        {
            var current = FeatureGateConfigService.Load();
            current[gateName] = value;
            FeatureGateConfigService.Save(current);
            FeatureGateRegistry.InvalidateCache();
        }
        catch
        {
            // Save failure is non-fatal — feature gate UI is best-effort.
        }
    }

    // ─────────────────────────── Banner setters ───────────────────────────

    /// <summary>
    /// Renders the compatibility-probe failure banner. Pass null/empty to hide.
    /// Wired by HostState whenever the probe state changes (post-update gate
    /// for ribbon/context-menu).
    /// </summary>
    public void SetProbeBanner(string? message)
    {
        InvokeOnUiThread(() => RenderBanner(_probeBannerHost, message,
            backColor: Color.FromArgb(255, 244, 220),
            foreColor: Color.FromArgb(120, 80, 0),
            iconText: "!"));
    }

    /// <summary>
    /// Renders the local-endpoint health banner (Ollama / LM Studio readiness).
    /// Pass null to hide. Status drives the banner color and message.
    /// </summary>
    public void SetHealthBanner(LocalHealthResult? health)
    {
        if (health == null || health.Status == LocalHealthStatus.NotApplicable)
        {
            InvokeOnUiThread(() => RenderBanner(_healthBannerHost, null, Color.White, Color.Black, ""));
            return;
        }

        Color bg, fg;
        string icon;
        switch (health.Status)
        {
            case LocalHealthStatus.Ready:
                bg = Color.FromArgb(228, 244, 226); fg = Color.FromArgb(20, 90, 20); icon = "OK"; break;
            case LocalHealthStatus.Reachable:
            case LocalHealthStatus.NoModels:
            case LocalHealthStatus.ModelNotFound:
                bg = Color.FromArgb(255, 244, 220); fg = Color.FromArgb(120, 80, 0); icon = "!"; break;
            default:
                bg = Color.FromArgb(252, 226, 226); fg = Color.FromArgb(140, 30, 30); icon = "X"; break;
        }
        string message = health.Message ?? string.Empty;
        InvokeOnUiThread(() => RenderBanner(_healthBannerHost, message, bg, fg, icon));
    }

    /// <summary>
    /// Renders the cost-budget banner. Hides when status is null or comfortably
    /// under budget. Warning-styled near the limit, error-styled when exhausted.
    /// </summary>
    public void SetBudgetBanner(BudgetStatus? status)
    {
        if (status == null)
        {
            InvokeOnUiThread(() => RenderBanner(_budgetBannerHost, null, Color.White, Color.Black, ""));
            return;
        }

        if (status.IsOverBudget)
        {
            string msg = "Token budget exhausted. " + status.FormatSummary();
            InvokeOnUiThread(() => RenderBanner(_budgetBannerHost, msg,
                Color.FromArgb(252, 226, 226),
                Color.FromArgb(140, 30, 30),
                "X"));
            return;
        }
        if (status.IsNearLimit(80))
        {
            string msg = "Approaching token budget. " + status.FormatSummary();
            InvokeOnUiThread(() => RenderBanner(_budgetBannerHost, msg,
                Color.FromArgb(255, 244, 220),
                Color.FromArgb(120, 80, 0),
                "!"));
            return;
        }
        InvokeOnUiThread(() => RenderBanner(_budgetBannerHost, null, Color.White, Color.Black, ""));
    }

    private static void RenderBanner(Panel host, string? message, Color backColor, Color foreColor, string iconText)
    {
        host.SuspendLayout();
        host.Controls.Clear();
        if (string.IsNullOrWhiteSpace(message))
        {
            host.Visible = false;
            host.Height = 0;
            host.ResumeLayout();
            return;
        }
        host.BackColor = backColor;
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = (string.IsNullOrEmpty(iconText) ? "" : "[" + iconText + "] ") + message,
            ForeColor = foreColor,
            BackColor = backColor,
            Padding = new Padding(8, 6, 8, 6),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize)
        };
        host.Controls.Add(label);
        host.Visible = true;
        host.Height = 28;
        host.ResumeLayout();
    }

    // ─────────────────────────── Streaming finalization ───────────────────────────

    /// <summary>
    /// Atomically replaces the in-flight streaming text with the fully-rendered
    /// final markdown. Called by HostState immediately before firing
    /// StateChanged so the UI doesn't show a brief raw-text flash before the
    /// re-render from ChatHistory completes. Idempotent — safe to call when
    /// no streaming view is active (becomes a no-op).
    /// </summary>
    public void FinalizeStream(string finalMarkdown)
    {
        InvokeOnUiThread(() =>
        {
            if (_activeStreamingView == null) return;
            _activeStreamingView.FinalizeStream(finalMarkdown ?? string.Empty);
            _activeStreamingView = null;
            ToggleRunMode(false);
        });
    }

    // ─────────────────────────── UI thread marshaling ───────────────────────────

    private void InvokeOnUiThread(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch (InvalidOperationException) { /* shutting down */ }
        }
        else
        {
            action();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.StateChanged -= OnHostStateChanged;
            _host.StreamChunkReceived -= OnStreamChunk;
            _host.ToolProgress -= OnToolProgress;
            UiPalette.ModeChanged -= OnPaletteModeChanged;
            _runCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
