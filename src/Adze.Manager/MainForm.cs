using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Adze.Broker.Configuration;

namespace Adze.Manager;

/// <summary>
/// Single-window installer / manager for Adze. Wraps the existing
/// <c>install-adze.ps1</c> and <c>install-adze.ps1 -Uninstall</c> logic so that
/// end users never have to open a terminal. Also provides "Eject before update"
/// which clears <see cref="SwBuildStateService"/> persisted state and force-unregisters
/// the add-in so SOLIDWORKS updates apply cleanly.
/// </summary>
public sealed class MainForm : Form
{
    // Paths Adze registers to under HKCU. Kept local so the manager does not
    // drag a dependency on Adze.Host just to read its own registry footprint.
    private const string AdzeComCls = @"Software\Classes\CLSID\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}";
    private const string AdzeSwAddIn = @"Software\SolidWorks\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}";
    private const string AdzeBinDirRelative = @"Adze\bin";

    private static readonly Color InstallColorNormal = Color.FromArgb(0, 114, 198);
    private static readonly Color InstallColorDisabled = Color.FromArgb(200, 205, 215);
    private static readonly Color EjectColorNormal = Color.FromArgb(255, 193, 7);
    private static readonly Color EjectColorPromoted = Color.FromArgb(220, 53, 69);
    private const string EjectLabelNormal = "Eject for Update";
    private const string EjectLabelPromoted = "\u26A0  Eject for Update";

    private readonly Label _lblInstallState;
    private readonly Label _lblSwProcess;
    private readonly Label _lblSwBuild;
    private readonly Label _lblConfigPath;
    private readonly Label _lblApiKey;
    private readonly Label _lblProbe;

    private readonly Button _btnInstall;
    private readonly Button _btnUninstall;
    private readonly Button _btnEject;
    private readonly Button _btnRefresh;

    private readonly RichTextBox _logOutput;

    public MainForm()
    {
        Text = "Adze Manager";
        Size = new Size(720, 560);
        MinimumSize = new Size(620, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(247, 248, 250);
        Font = new Font("Segoe UI", 9F);

        // --- Header ---
        var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(27, 58, 107) };
        var title = new Label
        {
            Text = "Adze for SOLIDWORKS",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            Location = new Point(18, 12),
            AutoSize = true
        };
        var subtitle = new Label
        {
            Text = "Install · Update · Eject · Status",
            ForeColor = Color.FromArgb(180, 200, 230),
            Font = new Font("Segoe UI", 9F),
            Location = new Point(20, 36),
            AutoSize = true
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        Controls.Add(header);

        // --- Status panel ---
        var statusGroup = new GroupBox
        {
            Text = "Status",
            Location = new Point(18, 72),
            Size = new Size(670, 190),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(10)
        };

        _lblInstallState = MakeStatusLabel(statusGroup, 20, "Install state: checking...");
        _lblSwProcess    = MakeStatusLabel(statusGroup, 44, "SOLIDWORKS: checking...");
        _lblSwBuild      = MakeStatusLabel(statusGroup, 68, "Last verified SW build: checking...");
        _lblProbe        = MakeStatusLabel(statusGroup, 92, "Last compatibility probe: (no run yet)");
        _lblApiKey       = MakeStatusLabel(statusGroup, 116, "API key store: checking...");
        _lblConfigPath   = MakeStatusLabel(statusGroup, 140, "Config: checking...");

        Controls.Add(statusGroup);

        // --- Action buttons ---
        var buttonPanel = new FlowLayoutPanel
        {
            Location = new Point(18, 270),
            Size = new Size(670, 44),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false
        };

        _btnInstall   = MakeButton("Install / Reinstall", Color.FromArgb(0, 114, 198), Color.White);
        _btnUninstall = MakeButton("Uninstall", Color.FromArgb(240, 240, 240), Color.FromArgb(30, 30, 30));
        _btnEject     = MakeButton("Eject for Update", Color.FromArgb(255, 193, 7), Color.FromArgb(30, 30, 30));
        _btnRefresh   = MakeButton("Refresh", Color.FromArgb(240, 240, 240), Color.FromArgb(30, 30, 30));

        _btnInstall.Click   += (s, e) => RunScriptWithUi("install-adze.ps1", args: string.Empty, label: "Installing Adze...");
        _btnUninstall.Click += (s, e) => RunScriptWithUi("install-adze.ps1", args: "-Uninstall", label: "Uninstalling Adze...");
        _btnEject.Click     += OnEjectClick;
        _btnRefresh.Click   += (s, e) => RefreshStatus();

        buttonPanel.Controls.Add(_btnInstall);
        buttonPanel.Controls.Add(_btnUninstall);
        buttonPanel.Controls.Add(_btnEject);
        buttonPanel.Controls.Add(_btnRefresh);
        Controls.Add(buttonPanel);

        // --- Log output ---
        var logGroup = new GroupBox
        {
            Text = "Log",
            Location = new Point(18, 322),
            Size = new Size(670, 186),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _logOutput = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.5F),
            BackColor = Color.FromArgb(24, 24, 28),
            ForeColor = Color.FromArgb(224, 232, 240),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            DetectUrls = false,
            WordWrap = true
        };
        logGroup.Controls.Add(_logOutput);
        Controls.Add(logGroup);

        Log("Adze Manager v1.0.0 started.");
        Log("State directory: " + GetLocalAppDataRoot());
        RefreshStatus();
    }

    private static Label MakeStatusLabel(Control parent, int y, string initial)
    {
        var lbl = new Label
        {
            Text = initial,
            Location = new Point(14, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(50, 60, 80)
        };
        parent.Controls.Add(lbl);
        return lbl;
    }

    private static Button MakeButton(string text, Color back, Color fore)
    {
        return new Button
        {
            Text = text,
            Size = new Size(152, 34),
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = back, BorderSize = 1 },
            Margin = new Padding(0, 0, 8, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
    }

    // -----------------------------------------------------------------------
    // Status refresh
    // -----------------------------------------------------------------------

    private void RefreshStatus()
    {
        // Install state — does the host DLL exist at the target path?
        string binDir = Path.Combine(GetLocalAppDataRoot(), AdzeBinDirRelative);
        string hostDll = Path.Combine(binDir, "Adze.Host.dll");
        if (File.Exists(hostDll))
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(hostDll);
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

        ApplyUpdaterEmphasis(updaterRunning);

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

        // Probe — we don't have a direct way to re-run from outside Host, so
        // surface the last-run info if available via a dedicated state file later.
        _lblProbe.Text = "Last compatibility probe: (run by Adze.Host on SOLIDWORKS launch — see host.log)";
    }

    // -----------------------------------------------------------------------
    // Script execution
    // -----------------------------------------------------------------------

    private void RunScriptWithUi(string scriptName, string args, string label)
    {
        string? scriptPath = LocateScript(scriptName);
        if (scriptPath == null)
        {
            Log("ERROR: could not find " + scriptName + " alongside Adze.Manager.exe or in ../install. " +
                "If you're running from the repo, set working dir to adze-cad root.");
            return;
        }

        SetButtonsEnabled(false);
        Log("");
        Log(label + " (" + scriptPath + (string.IsNullOrWhiteSpace(args) ? "" : " " + args) + ")");

        Task.Run(() =>
        {
            int exitCode = RunProcess(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" " + args,
                line => BeginInvoke((Action)(() => Log(line))));
            BeginInvoke((Action)(() =>
            {
                Log(exitCode == 0 ? "Success." : "Exited with code " + exitCode + ".");
                SetButtonsEnabled(true);
                RefreshStatus();
            }));
        });
    }

    private void OnEjectClick(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(this,
            "This will:\n" +
            "  1. Uninstall Adze from SOLIDWORKS (COM registration + installed DLLs removed)\n" +
            "  2. Clear the last-verified SW build record so the next install re-verifies\n" +
            "  3. Leave your config and stored API key intact (under %LOCALAPPDATA%\\Adze)\n\n" +
            "Use this before a major SOLIDWORKS / 3DEXPERIENCE update. Continue?",
            "Eject Adze for SW update",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (result != DialogResult.OK) return;

        Log("");
        Log("Ejecting Adze for SOLIDWORKS update...");
        SwBuildStateService.ClearLastVerifiedBuild();
        Log("Cleared last-verified SW build record.");
        RunScriptWithUi("install-adze.ps1", args: "-Uninstall", label: "Running uninstall...");
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _btnInstall.Enabled = enabled;
        _btnUninstall.Enabled = enabled;
        _btnEject.Enabled = enabled;
        _btnRefresh.Enabled = enabled;
    }

    /// <summary>
    /// When swxdesktopupdate.exe is running, installing Adze on top of a
    /// pending update is a footgun — the updater will clobber the COM
    /// registration half-way through. Swap the visual emphasis so Eject is
    /// the obvious action and Install is visibly unavailable.
    /// </summary>
    private void ApplyUpdaterEmphasis(bool updaterRunning)
    {
        if (updaterRunning)
        {
            _btnInstall.Enabled = false;
            _btnInstall.BackColor = InstallColorDisabled;
            _btnInstall.FlatAppearance.BorderColor = InstallColorDisabled;

            _btnEject.BackColor = EjectColorPromoted;
            _btnEject.ForeColor = Color.White;
            _btnEject.FlatAppearance.BorderColor = EjectColorPromoted;
            _btnEject.Text = EjectLabelPromoted;
            _btnEject.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        }
        else
        {
            _btnInstall.Enabled = true;
            _btnInstall.BackColor = InstallColorNormal;
            _btnInstall.FlatAppearance.BorderColor = InstallColorNormal;

            _btnEject.BackColor = EjectColorNormal;
            _btnEject.ForeColor = Color.FromArgb(30, 30, 30);
            _btnEject.FlatAppearance.BorderColor = EjectColorNormal;
            _btnEject.Text = EjectLabelNormal;
            _btnEject.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        }
    }

    private string? LocateScript(string scriptName)
    {
        // 1. Same folder as Adze.Manager.exe (release zip layout)
        string beside = Path.Combine(Application.StartupPath, scriptName);
        if (File.Exists(beside)) return beside;

        // 2. Repo layout — src/Adze.Manager/bin/Debug → ../../../../install/<script>
        string repoGuess = Path.GetFullPath(Path.Combine(Application.StartupPath,
            "..", "..", "..", "..", "install", scriptName));
        if (File.Exists(repoGuess)) return repoGuess;

        return null;
    }

    private static int RunProcess(string fileName, string arguments, Action<string> onOutput)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi);
        if (p == null) return -1;
        p.OutputDataReceived += (s, e) => { if (e.Data != null) onOutput(e.Data); };
        p.ErrorDataReceived  += (s, e) => { if (e.Data != null) onOutput("[err] " + e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string GetLocalAppDataRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private void Log(string message)
    {
        if (_logOutput.InvokeRequired)
        {
            BeginInvoke((Action<string>)Log, message);
            return;
        }
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _logOutput.AppendText("[" + timestamp + "] " + message + Environment.NewLine);
        _logOutput.SelectionStart = _logOutput.TextLength;
        _logOutput.ScrollToCaret();
    }
}
