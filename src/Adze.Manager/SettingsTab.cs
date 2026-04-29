using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using Adze.Broker.Configuration;

namespace Adze.Manager;

/// <summary>
/// Settings tab. Exposes the previously env-var-only knobs through a UI:
///   * Provider / API key / model / endpoint (persisted via ApiKeyStore)
///   * Tuning knobs (max tokens, timeouts, temperature) — currently env-var only;
///     numeric values are surfaced for visibility and "Save All" warns the user
///     these still require env-var setup at SW launch time (parity with v1.0 behavior).
///   * Feature gates (persisted via FeatureGateConfigService)
///
/// Manager runs in a separate process from Adze.Host. Anything written here
/// only takes effect after the host re-reads configuration on next ConnectToSW
/// (or, for env-var-only knobs, after a SW relaunch with the env var set).
/// </summary>
public sealed class SettingsTab : UserControl
{
    private static readonly string[] ProviderOptions = { "anthropic", "openai", "openrouter", "ollama", "lmstudio" };

    // Provider section
    private readonly ComboBox _providerCombo;
    private readonly TextBox _apiKeyBox;
    private readonly Button _btnSaveKey;
    private readonly Button _btnClearKey;
    private readonly TextBox _modelBox;
    private readonly TextBox _endpointBox;
    private readonly Label _keyStatus;

    // Tuning section
    private readonly NumericUpDown _maxTokens;
    private readonly NumericUpDown _synthMaxTokens;
    private readonly NumericUpDown _timeoutMs;
    private readonly NumericUpDown _synthTimeoutMs;
    private readonly TrackBar _temperature;
    private readonly Label _temperatureValue;

    // Feature gates
    private readonly CheckedListBox _gateList;
    private Dictionary<string, bool> _gateConfigSnapshot = new(StringComparer.OrdinalIgnoreCase);

    // Appearance (chunk 3) — Light / Dark / System radio group writes to UiPreferences.
    private readonly RadioButton _appLight;
    private readonly RadioButton _appDark;
    private readonly RadioButton _appSystem;
    private bool _suppressAppearancePersist;

    private readonly Button _btnSaveAll;

    public SettingsTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 248, 250);
        AutoScroll = true;
        Padding = new Padding(12, 8, 12, 8);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(247, 248, 250),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        // ---- Provider group ----
        var providerGroup = new GroupBox
        {
            Text = "Provider",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 6, 10, 10),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 60, 80)
        };
        var providerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        providerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        providerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        providerGroup.Controls.Add(providerLayout);

        _providerCombo = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 4, 0, 4)
        };
        _providerCombo.Items.AddRange(ProviderOptions);
        _providerCombo.SelectedIndexChanged += (s, e) => OnProviderChanged();
        providerLayout.Controls.Add(MakeFieldLabel("Provider:"), 0, 0);
        providerLayout.Controls.Add(_providerCombo, 1, 0);

        var keyRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0)
        };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        _apiKeyBox = new TextBox
        {
            Dock = DockStyle.Top,
            UseSystemPasswordChar = true,
            Margin = new Padding(0, 4, 4, 4)
        };
        _btnSaveKey = MakeButton("Save Key");
        _btnSaveKey.Click += (s, e) => OnSaveKey();
        _btnClearKey = MakeButton("Clear Key");
        _btnClearKey.Click += (s, e) => OnClearKey();
        keyRow.Controls.Add(_apiKeyBox, 0, 0);
        keyRow.Controls.Add(_btnSaveKey, 1, 0);
        keyRow.Controls.Add(_btnClearKey, 2, 0);
        providerLayout.Controls.Add(MakeFieldLabel("API Key:"), 0, 1);
        providerLayout.Controls.Add(keyRow, 1, 1);

        _keyStatus = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.FromArgb(80, 90, 110),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
            Margin = new Padding(0, 0, 0, 4)
        };
        providerLayout.Controls.Add(new Label { Width = 1 }, 0, 2);
        providerLayout.Controls.Add(_keyStatus, 1, 2);

        _modelBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
        providerLayout.Controls.Add(MakeFieldLabel("Model:"), 0, 3);
        providerLayout.Controls.Add(_modelBox, 1, 3);

        _endpointBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
        providerLayout.Controls.Add(MakeFieldLabel("Endpoint:"), 0, 4);
        providerLayout.Controls.Add(_endpointBox, 1, 4);

        var providerNote = new Label
        {
            Text = "Model and endpoint shown for visibility. They are still set by environment variable at SW launch (SOLIDWORKS_AI_<PROVIDER>_MODEL / _ENDPOINT). Save All will remind you which env vars to set.",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.FromArgb(110, 120, 140),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
            Margin = new Padding(0, 4, 0, 0)
        };
        providerLayout.Controls.Add(new Label { Width = 1 }, 0, 5);
        providerLayout.Controls.Add(providerNote, 1, 5);
        root.Controls.Add(providerGroup);

        // ---- Tuning group ----
        var tuningGroup = new GroupBox
        {
            Text = "Tuning",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 6, 10, 10),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 60, 80),
            Margin = new Padding(0, 8, 0, 0)
        };
        var tuningLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        tuningLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        tuningLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tuningGroup.Controls.Add(tuningLayout);

        _maxTokens = MakeNumeric(50, 4096, 700);
        tuningLayout.Controls.Add(MakeFieldLabel("Max tokens (broker):"), 0, 0);
        tuningLayout.Controls.Add(_maxTokens, 1, 0);

        _synthMaxTokens = MakeNumeric(50, 4096, 1100);
        tuningLayout.Controls.Add(MakeFieldLabel("Max tokens (synthesis):"), 0, 1);
        tuningLayout.Controls.Add(_synthMaxTokens, 1, 1);

        _timeoutMs = MakeNumeric(5000, 120000, 20000);
        _timeoutMs.Increment = 1000;
        tuningLayout.Controls.Add(MakeFieldLabel("Timeout ms (broker):"), 0, 2);
        tuningLayout.Controls.Add(_timeoutMs, 1, 2);

        _synthTimeoutMs = MakeNumeric(5000, 120000, 30000);
        _synthTimeoutMs.Increment = 1000;
        tuningLayout.Controls.Add(MakeFieldLabel("Timeout ms (synthesis):"), 0, 3);
        tuningLayout.Controls.Add(_synthTimeoutMs, 1, 3);

        _temperature = new TrackBar
        {
            // Slider: 0..20 maps to 0.00..1.00 in steps of 0.05
            Minimum = 0,
            Maximum = 20,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 4,
            Dock = DockStyle.Top,
            Margin = new Padding(0)
        };
        _temperatureValue = new Label
        {
            AutoSize = true,
            Margin = new Padding(8, 6, 0, 0),
            Font = new Font("Consolas", 9F)
        };
        _temperature.ValueChanged += (s, e) => UpdateTemperatureLabel();
        var tempRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        tempRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tempRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        tempRow.Controls.Add(_temperature, 0, 0);
        tempRow.Controls.Add(_temperatureValue, 1, 0);
        tuningLayout.Controls.Add(MakeFieldLabel("Temperature:"), 0, 4);
        tuningLayout.Controls.Add(tempRow, 1, 4);

        var tuningNote = new Label
        {
            Text = "Tuning knobs are environment-variable driven (SOLIDWORKS_AI_MAX_TOKENS, SOLIDWORKS_AI_TIMEOUT_MS, etc). Values shown reflect current effective settings. Persistent UI-driven tuning is on the v1.1+ roadmap.",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.FromArgb(110, 120, 140),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
            Margin = new Padding(0, 6, 0, 0)
        };
        tuningLayout.Controls.Add(new Label { Width = 1 }, 0, 5);
        tuningLayout.Controls.Add(tuningNote, 1, 5);
        root.Controls.Add(tuningGroup);

        // ---- Feature gates group ----
        var gatesGroup = new GroupBox
        {
            Text = "Feature gates",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 6, 10, 10),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 60, 80),
            Margin = new Padding(0, 8, 0, 0)
        };
        _gateList = new CheckedListBox
        {
            Dock = DockStyle.Top,
            Height = 220,
            CheckOnClick = true,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font = new Font("Consolas", 9F)
        };
        gatesGroup.Controls.Add(_gateList);
        root.Controls.Add(gatesGroup);

        // ---- Appearance group ----
        var appearanceGroup = new GroupBox
        {
            Text = "Appearance",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(10, 6, 10, 10),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 60, 80),
            Margin = new Padding(0, 8, 0, 0)
        };
        var appearanceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appearanceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        appearanceLayout.Controls.Add(MakeFieldLabel("UI mode:"), 0, 0);
        _appLight = new RadioButton { Text = "Light", AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
        _appDark = new RadioButton { Text = "Dark", AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
        _appSystem = new RadioButton { Text = "System", AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
        _appLight.CheckedChanged += (s, e) => { if (_appLight.Checked && !_suppressAppearancePersist) PersistUiMode("light"); };
        _appDark.CheckedChanged += (s, e) => { if (_appDark.Checked && !_suppressAppearancePersist) PersistUiMode("dark"); };
        _appSystem.CheckedChanged += (s, e) => { if (_appSystem.Checked && !_suppressAppearancePersist) PersistUiMode("system"); };
        appearanceLayout.Controls.Add(_appLight, 1, 0);
        appearanceLayout.Controls.Add(_appDark, 2, 0);
        appearanceLayout.Controls.Add(_appSystem, 3, 0);

        var appearanceNote = new Label
        {
            Text = "Sidebar picks up theme changes on next reload (close + reopen the Adze pane). Live preview applies inside the sidebar process when toggled there.",
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.FromArgb(110, 120, 140),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
            Margin = new Padding(0, 6, 0, 0)
        };
        // Add note FIRST then layout — DockStyle.Top stacks last-added on top,
        // so the radio row ends up above the explanation note.
        appearanceGroup.Controls.Add(appearanceNote);
        appearanceGroup.Controls.Add(appearanceLayout);
        root.Controls.Add(appearanceGroup);

        // ---- Save All button ----
        _btnSaveAll = new Button
        {
            Text = "Save All",
            AutoSize = true,
            Padding = new Padding(20, 6, 20, 6),
            Margin = new Padding(0, 12, 0, 4),
            BackColor = Color.FromArgb(0, 114, 198),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _btnSaveAll.FlatAppearance.BorderSize = 0;
        _btnSaveAll.Click += (s, e) => OnSaveAll();
        root.Controls.Add(_btnSaveAll);

        Load += (s, e) => LoadAll();
    }

    // -------------------------------------------------------------------
    // Public — refresh on tab activation
    // -------------------------------------------------------------------

    public void Reload()
    {
        LoadAll();
    }

    // -------------------------------------------------------------------
    // Load
    // -------------------------------------------------------------------

    private void LoadAll()
    {
        BrokerModelSettings settings;
        try { settings = BrokerModelSettings.LoadFromEnvironment(); }
        catch { settings = new BrokerModelSettings(); }

        // Provider — env wins; otherwise stored provider; otherwise LoadFromEnvironment's resolution.
        string provider = settings.Provider;
        if (Array.IndexOf(ProviderOptions, provider) < 0) provider = "anthropic";
        _providerCombo.SelectedItem = provider;

        // Model + endpoint (read-only-effective)
        _modelBox.Text = settings.Model;
        _endpointBox.Text = settings.Endpoint;

        // Tuning
        _maxTokens.Value = Clamp(settings.MaxTokens, _maxTokens.Minimum, _maxTokens.Maximum);
        _synthMaxTokens.Value = Clamp(settings.SynthesisMaxTokens, _synthMaxTokens.Minimum, _synthMaxTokens.Maximum);
        _timeoutMs.Value = Clamp(settings.TimeoutMilliseconds, _timeoutMs.Minimum, _timeoutMs.Maximum);
        _synthTimeoutMs.Value = Clamp(settings.SynthesisTimeoutMilliseconds, _synthTimeoutMs.Minimum, _synthTimeoutMs.Maximum);
        int tempSlider = (int)Math.Round(settings.Temperature * 20.0);
        _temperature.Value = Math.Max(_temperature.Minimum, Math.Min(_temperature.Maximum, tempSlider));
        UpdateTemperatureLabel();

        UpdateApiKeyDisplay(settings);

        LoadFeatureGates();
        LoadAppearance();
    }

    private void LoadAppearance()
    {
        _suppressAppearancePersist = true;
        try
        {
            UiPreferences prefs = UiPreferences.Load();
            string mode = (prefs.UiMode ?? "light").Trim().ToLowerInvariant();
            _appLight.Checked = mode == "light";
            _appDark.Checked = mode == "dark";
            _appSystem.Checked = mode == "system";
            // If somehow none matched, fall back to light.
            if (!_appLight.Checked && !_appDark.Checked && !_appSystem.Checked)
            {
                _appLight.Checked = true;
            }
        }
        catch
        {
            _appLight.Checked = true;
        }
        finally
        {
            _suppressAppearancePersist = false;
        }
    }

    private static void PersistUiMode(string mode)
    {
        try
        {
            UiPreferences prefs = UiPreferences.Load();
            prefs.UiMode = mode;
            prefs.Save();
        }
        catch
        {
            // Best-effort.
        }
    }

    private void LoadFeatureGates()
    {
        _gateList.Items.Clear();
        _gateConfigSnapshot = FeatureGateConfigService.Load();

        foreach (string gate in FeatureGateRegistry.KnownGates)
        {
            bool current = FeatureGateRegistry.IsEnabled(gate);
            string display = gate.Replace("SOLIDWORKS_AI_", "") + "  (" + gate + ")";
            _gateList.Items.Add(display, current);
        }
    }

    private void UpdateApiKeyDisplay(BrokerModelSettings settings)
    {
        string provider = _providerCombo.SelectedItem as string ?? settings.Provider;
        string stored = ApiKeyStore.GetKey(provider);
        if (!string.IsNullOrEmpty(stored))
        {
            _apiKeyBox.Text = stored;
            _keyStatus.Text = "Key stored for '" + provider + "' (DPAPI-encrypted, " + stored.Length + " chars).";
            _keyStatus.ForeColor = Color.FromArgb(10, 110, 60);
        }
        else
        {
            _apiKeyBox.Text = string.Empty;
            _keyStatus.Text = "(no key stored for '" + provider + "')";
            _keyStatus.ForeColor = Color.FromArgb(140, 80, 30);
        }
    }

    // -------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------

    private void OnProviderChanged()
    {
        // Reload effective model/endpoint for the newly selected provider so
        // the UI reflects what Adze.Host would resolve.
        BrokerModelSettings settings = BuildSettingsFromForm();
        // Manually re-resolve defaults for the provider we just selected.
        settings = SettingsForProvider(settings.Provider);
        _modelBox.Text = settings.Model;
        _endpointBox.Text = settings.Endpoint;
        UpdateApiKeyDisplay(settings);
    }

    private static BrokerModelSettings SettingsForProvider(string provider)
    {
        // Trick: temporarily set SOLIDWORKS_AI_PROVIDER and let LoadFromEnvironment
        // do its thing. We restore the old value immediately. This is the
        // simplest way to reuse the existing resolution logic without copying
        // it into the UI.
        string? prior = Environment.GetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER");
        try
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", provider);
            return BrokerModelSettings.LoadFromEnvironment();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOLIDWORKS_AI_PROVIDER", prior);
        }
    }

    private void OnSaveKey()
    {
        string? provider = _providerCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(provider))
        {
            MessageBox.Show(this, "Select a provider first.", "Adze Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string key = _apiKeyBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(key))
        {
            MessageBox.Show(this, "API key field is empty. Use Clear Key to remove the stored key.",
                "Adze Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApiKeyStore.Save(provider!, key);
            _keyStatus.Text = "Key saved for '" + provider + "'. SW reload required to take effect.";
            _keyStatus.ForeColor = Color.FromArgb(10, 110, 60);
            MessageBox.Show(this,
                "API key saved (DPAPI-encrypted) for provider '" + provider + "'.\n\n" +
                "Adze.Host reads this on next ConnectToSW. If SOLIDWORKS is running with the add-in loaded, " +
                "uninstall + reinstall (or close + reopen SW) to pick up the new key.",
                "Adze Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to save key: " + ex.Message, "Adze Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnClearKey()
    {
        var result = MessageBox.Show(this,
            "Clear the stored API key? Adze.Host will fall back to environment variables or the deterministic broker until a new key is saved.",
            "Adze Manager", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (result != DialogResult.OK) return;

        try
        {
            ApiKeyStore.Clear();
            _apiKeyBox.Text = string.Empty;
            _keyStatus.Text = "Cleared. SW reload required to take effect.";
            _keyStatus.ForeColor = Color.FromArgb(140, 80, 30);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to clear key: " + ex.Message, "Adze Manager",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSaveAll()
    {
        // What changed (vs current on-disk state)?
        var changes = new List<string>();
        var caveats = new List<string>();

        // Feature gates — we own a real config file here.
        var gateChanges = ComputeGateChanges();
        if (gateChanges.Count > 0)
        {
            changes.Add("Feature gates: " + gateChanges.Count + " change(s)");
            try
            {
                var nextConfig = new Dictionary<string, bool>(_gateConfigSnapshot, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in gateChanges) nextConfig[kvp.Key] = kvp.Value;
                FeatureGateConfigService.Save(nextConfig);
                FeatureGateRegistry.InvalidateCache();
                _gateConfigSnapshot = nextConfig;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to save feature gates: " + ex.Message, "Adze Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        // API key — only flagged here if the textbox text is non-empty AND
        // differs from what's currently stored. Saving is via the Save Key
        // button; we don't auto-save on Save All to avoid a surprise overwrite.
        string provider = _providerCombo.SelectedItem as string ?? "anthropic";
        string typedKey = _apiKeyBox.Text?.Trim() ?? string.Empty;
        string storedKey = ApiKeyStore.GetKey(provider);
        if (!string.IsNullOrEmpty(typedKey) && typedKey != storedKey)
        {
            caveats.Add("API key: changed in form but NOT saved (use 'Save Key' button — Save All never auto-overwrites a stored key).");
        }

        // Tuning — env-only for v1.0; we surface the deltas as TODO env-var snippets.
        BrokerModelSettings effective = BrokerModelSettings.LoadFromEnvironment();
        var envSnippets = new List<string>();
        if ((int)_maxTokens.Value != effective.MaxTokens)
            envSnippets.Add("setx SOLIDWORKS_AI_MAX_TOKENS " + (int)_maxTokens.Value);
        if ((int)_synthMaxTokens.Value != effective.SynthesisMaxTokens)
            envSnippets.Add("setx SOLIDWORKS_AI_SYNTHESIS_MAX_TOKENS " + (int)_synthMaxTokens.Value);
        if ((int)_timeoutMs.Value != effective.TimeoutMilliseconds)
            envSnippets.Add("setx SOLIDWORKS_AI_TIMEOUT_MS " + (int)_timeoutMs.Value);
        if ((int)_synthTimeoutMs.Value != effective.SynthesisTimeoutMilliseconds)
            envSnippets.Add("setx SOLIDWORKS_AI_SYNTHESIS_TIMEOUT_MS " + (int)_synthTimeoutMs.Value);
        double formTemp = _temperature.Value / 20.0;
        if (Math.Abs(formTemp - effective.Temperature) > 0.001)
            envSnippets.Add("setx SOLIDWORKS_AI_TEMPERATURE " + formTemp.ToString("0.00", CultureInfo.InvariantCulture));

        if (envSnippets.Count > 0)
        {
            caveats.Add("Tuning knobs are env-var-only in v1.0. Run these in a new shell, then relaunch SOLIDWORKS:\n  " +
                string.Join("\n  ", envSnippets));
        }

        // Provider selection — also env-var-only at SW launch unless an API key
        // is stored under that provider name (ApiKeyStore.GetConfiguredProvider
        // is the secondary fallback).
        if (!string.Equals(provider, effective.Provider, StringComparison.OrdinalIgnoreCase))
        {
            caveats.Add("Provider changed to '" + provider + "'. This takes effect when an API key is stored for it (Save Key) OR when SOLIDWORKS_AI_PROVIDER=" + provider + " is set before SW launch.");
        }

        var sb = new StringBuilder();
        if (changes.Count == 0 && caveats.Count == 0)
        {
            sb.AppendLine("No changes detected.");
        }
        else
        {
            if (changes.Count > 0)
            {
                sb.AppendLine("Saved:");
                foreach (string c in changes) sb.Append("  - ").AppendLine(c);
                sb.AppendLine();
            }
            if (caveats.Count > 0)
            {
                sb.AppendLine("Notes:");
                foreach (string c in caveats) sb.Append("  - ").AppendLine(c);
                sb.AppendLine();
            }
            sb.AppendLine("Adze.Host runs in a separate process. Settings written here apply on the next ConnectToSW (uninstall + reinstall, or relaunch SOLIDWORKS).");
        }

        MessageBox.Show(this, sb.ToString(), "Adze Manager — Save All",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private Dictionary<string, bool> ComputeGateChanges()
    {
        var changes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _gateList.Items.Count; i++)
        {
            string gate = FeatureGateRegistry.KnownGates[i];
            bool checkedNow = _gateList.GetItemChecked(i);
            bool effectiveBefore = FeatureGateRegistry.IsEnabled(gate);
            if (checkedNow != effectiveBefore)
            {
                changes[gate] = checkedNow;
            }
        }
        return changes;
    }

    private BrokerModelSettings BuildSettingsFromForm()
    {
        return new BrokerModelSettings
        {
            Provider = _providerCombo.SelectedItem as string ?? "anthropic",
            Model = _modelBox.Text?.Trim() ?? string.Empty,
            Endpoint = _endpointBox.Text?.Trim() ?? string.Empty,
            MaxTokens = (int)_maxTokens.Value,
            SynthesisMaxTokens = (int)_synthMaxTokens.Value,
            TimeoutMilliseconds = (int)_timeoutMs.Value,
            SynthesisTimeoutMilliseconds = (int)_synthTimeoutMs.Value,
            Temperature = _temperature.Value / 20.0
        };
    }

    private void UpdateTemperatureLabel()
    {
        _temperatureValue.Text = (_temperature.Value / 20.0).ToString("0.00", CultureInfo.InvariantCulture);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static Label MakeFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0),
            ForeColor = Color.FromArgb(50, 60, 80),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
    }

    private static Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 4, 0, 4),
            FlatStyle = FlatStyle.System,
            Font = new Font("Segoe UI", 9F)
        };
    }

    private static NumericUpDown MakeNumeric(int min, int max, int initial)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = initial,
            Increment = 1,
            ThousandsSeparator = false,
            Width = 120,
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Consolas", 9F)
        };
    }

    private static decimal Clamp(int v, decimal min, decimal max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }
}
