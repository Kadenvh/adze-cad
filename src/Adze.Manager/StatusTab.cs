using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Adze.Broker.Configuration;

namespace Adze.Manager;

/// <summary>
/// Status tab. Existing Status surface relocated from the single MainForm —
/// install state, SOLIDWORKS process state, last-verified build, compatibility
/// probe, API key store, and config path. MainForm calls <see cref="Refresh"/>
/// on load, on script completion, and on Form.Activated.
/// </summary>
public sealed class StatusTab : UserControl
{
    private const string AdzeBinDirRelative = @"Adze\bin";

    private readonly Label _lblInstallState;
    private readonly Label _lblSwProcess;
    private readonly Label _lblSwBuild;
    private readonly Label _lblConfigPath;
    private readonly Label _lblApiKey;
    private readonly Label _lblProbe;

    /// <summary>Fired whenever we observe a 3DX updater state change so MainForm can re-emphasise buttons.</summary>
    public event EventHandler<bool>? UpdaterRunningChanged;

    private bool _lastUpdaterRunning;
    private bool _firstRefresh = true;

    public StatusTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 248, 250);
        Padding = new Padding(12, 12, 12, 12);

        var group = new GroupBox
        {
            Text = "Status",
            Dock = DockStyle.Top,
            Height = 200,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 60, 80)
        };

        _lblInstallState = MakeStatusLabel(group, 24, "Install state: checking...");
        _lblSwProcess    = MakeStatusLabel(group, 48, "SOLIDWORKS: checking...");
        _lblSwBuild      = MakeStatusLabel(group, 72, "Last verified SW build: checking...");
        _lblProbe        = MakeStatusLabel(group, 96, "Last compatibility probe: (no run yet)");
        _lblApiKey       = MakeStatusLabel(group, 120, "API key store: checking...");
        _lblConfigPath   = MakeStatusLabel(group, 144, "Config: checking...");

        Controls.Add(group);

        var hint = new Label
        {
            Text = "Status auto-refreshes when this window regains focus. Click 'Refresh' in the header to force a poll.",
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 0),
            AutoSize = false,
            Height = 32,
            ForeColor = Color.FromArgb(110, 120, 140),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic)
        };
        Controls.Add(hint);
    }

    public new void Refresh()
    {
        // Install state — does the host DLL exist at the target path?
        string binDir = Path.Combine(GetLocalAppDataRoot(), AdzeBinDirRelative);
        string hostDll = Path.Combine(binDir, "Adze.Host.dll");
        if (File.Exists(hostDll))
        {
            var info = FileVersionInfo.GetVersionInfo(hostDll);
            _lblInstallState.Text = "Install state: INSTALLED at " + binDir +
                " (v" + (info.FileVersion ?? "?") + ", built " + File.GetLastWriteTime(hostDll).ToString("yyyy-MM-dd HH:mm") + ")";
            _lblInstallState.ForeColor = Color.FromArgb(10, 110, 60);
        }
        else
        {
            _lblInstallState.Text = "Install state: NOT INSTALLED";
            _lblInstallState.ForeColor = Color.FromArgb(180, 30, 30);
        }

        // SOLIDWORKS running?
        Process[] sw = Process.GetProcessesByName("sldworks");
        Process[] updater = Process.GetProcessesByName("swxdesktopupdate");
        bool updaterRunning = updater.Length > 0;

        if (sw.Length > 0 && updaterRunning)
        {
            _lblSwProcess.Text = "SOLIDWORKS: RUNNING (PID " + sw[0].Id + ") · Updater also running — do not install/uninstall now";
            _lblSwProcess.ForeColor = Color.FromArgb(180, 30, 30);
        }
        else if (sw.Length > 0)
        {
            _lblSwProcess.Text = "SOLIDWORKS: RUNNING (PID " + sw[0].Id + ") — close it before install/uninstall";
            _lblSwProcess.ForeColor = Color.FromArgb(200, 140, 0);
        }
        else if (updaterRunning)
        {
            _lblSwProcess.Text = "SOLIDWORKS: not running · 3DX updater is running (PID " + updater[0].Id + ") — Eject before the updater applies changes";
            _lblSwProcess.ForeColor = Color.FromArgb(180, 30, 30);
        }
        else
        {
            _lblSwProcess.Text = "SOLIDWORKS: not running";
            _lblSwProcess.ForeColor = Color.FromArgb(50, 60, 80);
        }

        if (_firstRefresh || updaterRunning != _lastUpdaterRunning)
        {
            _lastUpdaterRunning = updaterRunning;
            UpdaterRunningChanged?.Invoke(this, updaterRunning);
        }
        _firstRefresh = false;

        // Last verified build
        string verified = SwBuildStateService.GetLastVerifiedBuild();
        _lblSwBuild.Text = "Last verified SW build: " + (string.IsNullOrWhiteSpace(verified) ? "(never verified)" : verified);

        // API key store
        bool hasKey = ApiKeyStore.HasStoredKey();
        string? provider = ApiKeyStore.GetConfiguredProvider();
        _lblApiKey.Text = hasKey
            ? "API key store: key stored for provider '" + provider + "' (DPAPI-encrypted)"
            : "API key store: no key stored (deterministic broker will be used)";

        // Config path
        _lblConfigPath.Text = "Config: " + FeatureGateConfigService.GetConfigPath();

        // Compatibility probe — parse the most recent CompatibilityProbe line
        // from host.log. The probe runs once per ConnectToSW with one of three
        // outcomes that we surface here:
        //   * "CompatibilityProbe: OK. SW revision=..." → green
        //   * "CompatibilityProbe: ... threw" / "CompatibilityProbe: failed..." → red
        //   * "CompatibilityProbe: ... non-fatal..." → muted (cleanup info, not failure)
        var probe = TryReadLastProbeLine();
        if (probe == null)
        {
            _lblProbe.Text = "Last compatibility probe: (no probe line in host.log yet)";
            _lblProbe.ForeColor = Color.FromArgb(110, 120, 140);
        }
        else
        {
            string ts = probe.Value.Timestamp ?? "(unknown time)";
            string msg = probe.Value.Message;
            _lblProbe.Text = "Last compatibility probe: " + msg + "  [" + ts + "]";
            if (msg.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _lblProbe.ForeColor = Color.FromArgb(10, 110, 60); // green
            }
            else if (msg.IndexOf("non-fatal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _lblProbe.ForeColor = Color.FromArgb(110, 120, 140); // muted
            }
            else
            {
                _lblProbe.ForeColor = Color.FromArgb(180, 30, 30); // red
            }
        }
    }

    /// <summary>
    /// Tail-scans %LOCALAPPDATA%\Adze\logs\host.log for the most recent line
    /// containing "CompatibilityProbe: " and returns its timestamp prefix +
    /// the probe portion of the message. Cheap (last 200 lines max). Returns
    /// null if host.log is missing or no probe line is present.
    /// </summary>
    private static (string? Timestamp, string Message)? TryReadLastProbeLine()
    {
        string path = Path.Combine(GetLocalAppDataRoot(), "Adze", "logs", "host.log");
        if (!File.Exists(path)) return null;

        try
        {
            // Read all lines but keep only the tail in memory. host.log is
            // generally well under 20 MB; the simple full-read is fine.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
            string? line;
            string? lastProbe = null;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.IndexOf("CompatibilityProbe: ", StringComparison.Ordinal) >= 0)
                {
                    lastProbe = line;
                }
            }
            if (lastProbe == null) return null;

            // host.log lines look like: "2026-05-04 10:24:13.123 INFO CompatibilityProbe: OK. SW revision=..."
            // Split off a leading ISO-ish timestamp + level token if present.
            int probeIdx = lastProbe.IndexOf("CompatibilityProbe: ", StringComparison.Ordinal);
            string probeMsg = lastProbe.Substring(probeIdx + "CompatibilityProbe: ".Length).TrimEnd();
            string? ts = null;
            if (probeIdx > 0)
            {
                // Trim trailing space + level token before the probe phrase.
                string head = lastProbe.Substring(0, probeIdx).TrimEnd();
                int lastSpace = head.LastIndexOf(' ');
                if (lastSpace > 0) head = head.Substring(0, lastSpace).TrimEnd();
                if (head.Length >= 10) ts = head;
            }
            return (ts, probeMsg);
        }
        catch
        {
            return null;
        }
    }

    private static Label MakeStatusLabel(Control parent, int y, string initial)
    {
        var lbl = new Label
        {
            Text = initial,
            Location = new Point(14, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(50, 60, 80),
            Font = new Font("Segoe UI", 9F)
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    private static string GetLocalAppDataRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
}
