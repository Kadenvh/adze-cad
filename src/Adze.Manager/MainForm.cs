using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Adze.Broker.Configuration;

namespace Adze.Manager;

/// <summary>
/// Tabbed power-user control panel for Adze. Header + button row stay at the
/// top (Install / Uninstall / Eject / Refresh) and a TabControl below hosts:
///   * Logs (host.log tail + chat JSONL browser + script log)
///   * Settings (provider, key, tuning, feature gates)
///   * Agent Profile (deferred to v1.1+)
///   * Status (install state, SW process state, probe, key store, config path)
/// </summary>
public sealed class MainForm : Form
{
    // Paths Adze registers to under HKCU. Kept local so the manager does not
    // drag in a runtime dependency on Adze.Host just to read its own footprint.
    private const string AdzeComCls = @"Software\Classes\CLSID\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}";
    private const string AdzeSwAddIn = @"Software\SolidWorks\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}";

    private static readonly Color InstallColorNormal = Color.FromArgb(0, 114, 198);
    private static readonly Color InstallColorDisabled = Color.FromArgb(200, 205, 215);
    private static readonly Color EjectColorNormal = Color.FromArgb(255, 193, 7);
    private static readonly Color EjectColorPromoted = Color.FromArgb(220, 53, 69);
    private static readonly Color LaunchColorNormal = Color.FromArgb(34, 139, 90);   // green
    private static readonly Color LaunchColorFocus  = Color.FromArgb(70, 130, 180);  // steel blue (already running)
    private static readonly Color LaunchColorBlocked = Color.FromArgb(180, 30, 30);  // red (updater running)
    private const string EjectLabelNormal = "Eject for Update";
    private const string EjectLabelPromoted = "\u26A0  Eject for Update";
    private const string LaunchLabelLaunch = "Launch SOLIDWORKS";
    private const string LaunchLabelFocus  = "Focus SOLIDWORKS";
    private const string LaunchLabelBlocked = "SW (updater running)";

    // Known launcher blocker patterns \u2014 mirrors scripts/setup/launch-and-check-host.ps1
    private static readonly (string Pattern, string Reason, string Recovery)[] LauncherBlockerPatterns = new[]
    {
        ("Login | 3DEXPERIENCE ID",       "3DEXPERIENCE desktop login required before SOLIDWORKS can start.", "Dismiss the 3DEXPERIENCE login window, then click Launch again."),
        ("3DEXPERIENCE Update",            "3DEXPERIENCE update window is blocking SOLIDWORKS from starting.",  "Complete or dismiss the 3DEXPERIENCE update, then click Launch again."),
        ("3DEXPERIENCE Platform",          "3DEXPERIENCE platform window is blocking SOLIDWORKS from starting.","Close the 3DEXPERIENCE Platform window, then click Launch again.")
    };

    private readonly Button _btnInstall;
    private readonly Button _btnUninstall;
    private readonly Button _btnEject;
    private readonly Button _btnLaunchSw;
    private readonly Button _btnVerify;
    private readonly Button _btnRefresh;
    private System.Windows.Forms.Timer? _launchWatcher;

    private readonly TabControl _tabs;
    private readonly LogsTab _logsTab;
    private readonly SettingsTab _settingsTab;
    private readonly AgentProfileTab _agentProfileTab;
    private readonly StatusTab _statusTab;

    public MainForm()
    {
        Text = "Adze Manager";
        Size = new Size(880, 720);
        MinimumSize = new Size(720, 560);
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
            Text = "Install · Update · Eject · Logs · Settings",
            ForeColor = Color.FromArgb(180, 200, 230),
            Font = new Font("Segoe UI", 9F),
            Location = new Point(20, 36),
            AutoSize = true
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        Controls.Add(header);

        // --- Action buttons ---
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(14, 8, 14, 8),
            BackColor = Color.FromArgb(247, 248, 250)
        };

        _btnInstall   = MakeButton("Install / Reinstall", Color.FromArgb(0, 114, 198), Color.White);
        _btnUninstall = MakeButton("Uninstall", Color.FromArgb(240, 240, 240), Color.FromArgb(30, 30, 30));
        _btnEject     = MakeButton("Eject for Update", Color.FromArgb(255, 193, 7), Color.FromArgb(30, 30, 30));
        _btnLaunchSw  = MakeButton(LaunchLabelLaunch, LaunchColorNormal, Color.White);
        _btnVerify    = MakeButton("Verify Setup", Color.FromArgb(79, 70, 229), Color.White);
        _btnRefresh   = MakeButton("Refresh", Color.FromArgb(240, 240, 240), Color.FromArgb(30, 30, 30));

        _btnInstall.Click   += (s, e) => RunScriptWithUi(
            "install-adze.ps1", args: string.Empty, label: "Installing Adze...",
            onComplete: exitCode =>
            {
                if (exitCode != 0) return;
                Log("");
                Log("=== Install complete — auto-verifying setup ===");
                RunVerifyChecksOnly();
            });
        _btnUninstall.Click += OnUninstallClick;
        _btnEject.Click     += OnEjectClick;
        _btnLaunchSw.Click  += OnLaunchSwClick;
        _btnVerify.Click    += OnVerifySetupClick;
        _btnRefresh.Click   += (s, e) => RefreshAll();

        buttonPanel.Controls.Add(_btnInstall);
        buttonPanel.Controls.Add(_btnUninstall);
        buttonPanel.Controls.Add(_btnEject);
        buttonPanel.Controls.Add(_btnLaunchSw);
        buttonPanel.Controls.Add(_btnVerify);
        buttonPanel.Controls.Add(_btnRefresh);
        Controls.Add(buttonPanel);

        // --- Tabs (must be added BEFORE header/buttons in z-order so Dock=Fill works correctly) ---
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 4),
            Font = new Font("Segoe UI", 9F)
        };

        _logsTab = new LogsTab();
        _settingsTab = new SettingsTab();
        _agentProfileTab = new AgentProfileTab();
        _statusTab = new StatusTab();
        _statusTab.UpdaterRunningChanged += (s, updaterRunning) => ApplyUpdaterEmphasis(updaterRunning);

        var tpLogs = new TabPage("Logs") { BackColor = Color.FromArgb(247, 248, 250) };
        var tpSettings = new TabPage("Settings") { BackColor = Color.FromArgb(247, 248, 250) };
        var tpAgent = new TabPage("Agent Profile") { BackColor = Color.FromArgb(247, 248, 250) };
        var tpStatus = new TabPage("Status") { BackColor = Color.FromArgb(247, 248, 250) };

        tpLogs.Controls.Add(_logsTab);
        tpSettings.Controls.Add(_settingsTab);
        tpAgent.Controls.Add(_agentProfileTab);
        tpStatus.Controls.Add(_statusTab);

        _tabs.TabPages.Add(tpLogs);
        _tabs.TabPages.Add(tpSettings);
        _tabs.TabPages.Add(tpAgent);
        _tabs.TabPages.Add(tpStatus);
        Controls.Add(_tabs);

        // Top-docked siblings dock in REVERSE child-index order: highest
        // index docks first (outermost = top edge), lowest index docks last
        // (innermost = below). For "header on top, buttons below header,
        // tabs filling rest" we need header to have the HIGHEST index among
        // top-docked, buttons in the middle, tabs (Fill) lowest so it fills
        // what remains.
        Controls.SetChildIndex(_tabs, 0);
        Controls.SetChildIndex(buttonPanel, 1);
        Controls.SetChildIndex(header, 2);

        Activated += (s, e) => OnFormActivated();

        _tabs.SelectedIndexChanged += (s, e) =>
        {
            // Keep the relevant tab in sync when the user switches in.
            if (_tabs.SelectedTab == tpStatus) _statusTab.Refresh();
            else if (_tabs.SelectedTab == tpSettings) _settingsTab.Reload();
        };

        Load += (s, e) =>
        {
            string mgrVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "(unknown)";
            Log("Adze Manager v" + mgrVersion + " started.");
            Log("State directory: " + GetLocalAppDataRoot());
            RefreshAll();
        };
    }

    // -----------------------------------------------------------------------
    // Activation refresh
    // -----------------------------------------------------------------------

    private void OnFormActivated()
    {
        _statusTab.Refresh();
        // Settings reload too — env state can change while Manager is open
        // (user runs setx in a shell, then Alt+Tabs back).
        _settingsTab.Reload();
    }

    private void RefreshAll()
    {
        _statusTab.Refresh();
        _settingsTab.Reload();
        ApplyLaunchButtonState();
    }

    // -----------------------------------------------------------------------
    // Script execution (Install / Uninstall / Eject)
    // -----------------------------------------------------------------------

    private void RunScriptWithUi(string scriptName, string args, string label, Action<int>? onComplete = null)
    {
        string? scriptPath = LocateScript(scriptName);
        if (scriptPath == null)
        {
            Log("ERROR: could not find " + scriptName + " alongside Adze.Manager.exe or in ../install. " +
                "If you're running from the repo, set working dir to adze-cad root.");
            return;
        }

        // Auto-show Logs tab so the user sees the live script output.
        _tabs.SelectedTab = _tabs.TabPages[0];
        SetButtonsEnabled(false);
        Log(string.Empty);
        Log(label + " (" + scriptPath + (string.IsNullOrWhiteSpace(args) ? "" : " " + args) + ")");

        // Forensic trail: log the resolved script's hash + last-write-time so a
        // user reporting "install behaved differently than expected" can post
        // these from the Logs tab and we can immediately tell whether they ran
        // a stale zip vs the live repo script (the Session 6 stale-zip class
        // of regression).
        try
        {
            var fi = new FileInfo(scriptPath);
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var fs = fi.OpenRead();
            string hashHex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
            Log(string.Format("    Script hash (SHA256): {0}", hashHex));
            Log(string.Format("    Script last-write   : {0:o} ({1:N0} bytes)", fi.LastWriteTimeUtc, fi.Length));
        }
        catch (Exception hashEx)
        {
            Log("    (could not hash script: " + hashEx.Message + ")");
        }

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
                RefreshAll();
                onComplete?.Invoke(exitCode);
            }));
        });
    }

    /// <summary>
    /// Uninstall with an explicit choice about user data. The default
    /// (No on the prompt) keeps everything under %LOCALAPPDATA%\Adze\
    /// (config.json, keys.dat, traces, snapshots, recipes, ui-prefs.json,
    /// chat history) so reinstalling later restores the user's setup.
    /// Choosing Yes passes -RemoveUserData to uninstall-adze.ps1, which
    /// wipes the entire %LOCALAPPDATA%\Adze\ directory. Used when the
    /// user wants a true clean slate, or when stale config is suspected
    /// of blocking new behavior (e.g. cached feature-gate values that
    /// override env-var changes).
    /// </summary>
    private void OnUninstallClick(object? sender, EventArgs e)
    {
        var prompt = MessageBox.Show(this,
            "Uninstall Adze.\n\n" +
            "Remove user data too? This deletes:\n" +
            "  - Stored API keys (DPAPI %LOCALAPPDATA%\\Adze\\keys.dat)\n" +
            "  - Saved settings (%LOCALAPPDATA%\\Adze\\config.json + ui-prefs.json)\n" +
            "  - Traces, snapshots, recipes, chat history, host.log\n\n" +
            "Click Yes for a clean-slate uninstall (recommended if reinstalling to apply changes).\n" +
            "Click No to keep your settings and reinstall later.\n" +
            "Click Cancel to abort.",
            "Uninstall Adze",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (prompt == DialogResult.Cancel) return;

        string scriptArgs = (prompt == DialogResult.Yes) ? "-Uninstall -RemoveUserData" : "-Uninstall";
        string label = (prompt == DialogResult.Yes)
            ? "Uninstalling Adze (clean slate — removing user data)..."
            : "Uninstalling Adze (preserving user data)...";

        RunScriptWithUi("install-adze.ps1", args: scriptArgs, label: label,
            onComplete: exitCode =>
            {
                if (exitCode != 0) return;
                Log("");
                Log("=== Uninstall complete — auto-verifying cleanup ===");
                RunVerifyChecksOnly();
            });
    }

    private void OnEjectClick(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(this,
            "This will:\n" +
            "  1. Uninstall Adze from SOLIDWORKS (COM registration + installed DLLs removed)\n" +
            "  2. Stop SW child processes that hold file locks (sldworks_fs, SW-bundled WebView2)\n" +
            "  3. Clear the last-verified SW build record so the next install re-verifies\n" +
            "  4. Leave your config and stored API key intact (under %LOCALAPPDATA%\\Adze)\n\n" +
            "Use this before a major SOLIDWORKS / 3DEXPERIENCE update. Continue?",
            "Eject Adze for SW update",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (result != DialogResult.OK) return;

        Log(string.Empty);
        Log("Ejecting Adze for SOLIDWORKS update...");
        SwBuildStateService.ClearLastVerifiedBuild();
        Log("Cleared last-verified SW build record.");
        RunScriptWithUi("install-adze.ps1", args: "-Uninstall", label: "Running uninstall...",
            onComplete: exitCode =>
            {
                if (exitCode != 0) return;
                Log(string.Empty);
                Log("=== Eject complete ===");
                Log("Next: open 3DEXPERIENCE Launcher and click Update Now to apply the SW update.");
                Log("After the update finishes, return here and click Install to re-register Adze on the new SW build.");
            });
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _btnInstall.Enabled = enabled;
        _btnUninstall.Enabled = enabled;
        _btnEject.Enabled = enabled;
        _btnLaunchSw.Enabled = enabled;
        _btnVerify.Enabled = enabled;
        _btnRefresh.Enabled = enabled;
    }

    // -----------------------------------------------------------------------
    // Launch SOLIDWORKS — smart state: focus running window, block during 3DX
    // updater, otherwise spawn the public-desktop shortcut and watch launcher
    // window titles for known blockers (mirrors launch-and-check-host.ps1).
    // -----------------------------------------------------------------------

    private void OnLaunchSwClick(object? sender, EventArgs e)
    {
        // 3DX updater running? Hard block — installing/launching during update is a footgun.
        var updater = Process.GetProcessesByName("swxdesktopupdate");
        if (updater.Length > 0)
        {
            MessageBox.Show(this,
                "3DEXPERIENCE updater is running (PID " + updater[0].Id + ").\n\n" +
                "Launching SOLIDWORKS now will collide with the in-progress update. " +
                "Wait for the updater to finish, then try again.",
                "Updater in progress",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // Already running? Bring main window forward instead of starting another instance.
        var swProcs = Process.GetProcessesByName("sldworks")
            .Concat(Process.GetProcessesByName("SLDWORKS"))
            .ToArray();
        if (swProcs.Length > 0)
        {
            var withWindow = swProcs.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (withWindow != null)
            {
                Log("");
                Log("SOLIDWORKS already running (PID " + withWindow.Id + ") — focusing existing window.");
                NativeFocus.SetForegroundWindow(withWindow.MainWindowHandle);
            }
            else
            {
                Log("");
                Log("SOLIDWORKS process detected (PID " + swProcs[0].Id + ") but main window not yet ready — wait a moment and try again.");
            }
            return;
        }

        // Locate the launcher target. Prefer the public-desktop shortcut (matches
        // launch-and-check-host.ps1); fall back to direct sldworks.exe.
        string target = ResolveSolidWorksLaunchTarget();
        if (string.IsNullOrEmpty(target))
        {
            MessageBox.Show(this,
                "Could not find a SOLIDWORKS launch target.\n\n" +
                "Tried:\n" +
                "  - C:\\Users\\Public\\Desktop\\SOLIDWORKS Design.lnk\n" +
                "  - %ProgramFiles%\\Dassault Systemes\\SOLIDWORKS 3DEXPERIENCE R2026x\\SOLIDWORKS\\sldworks.exe\n\n" +
                "Confirm SOLIDWORKS is installed, then launch it from the Windows Start menu.",
                "SOLIDWORKS not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Auto-show Logs tab so the user sees the launch trail.
        _tabs.SelectedTab = _tabs.TabPages[0];
        Log("");
        Log("Launching SOLIDWORKS via: " + target);

        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log("    Launch failed: " + ex.Message);
            MessageBox.Show(this, "Launch failed: " + ex.Message,
                "Adze Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Disable button while we watch — prevents double-launch.
        _btnLaunchSw.Enabled = false;
        _btnLaunchSw.Text = "Launching...";

        StartLaunchWatcher(DateTime.UtcNow);
    }

    private static string ResolveSolidWorksLaunchTarget()
    {
        string shortcut = @"C:\Users\Public\Desktop\SOLIDWORKS Design.lnk";
        if (File.Exists(shortcut)) return shortcut;

        foreach (string candidate in new[]
        {
            @"C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\sldworks.exe",
            @"C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2025x\SOLIDWORKS\sldworks.exe",
            @"C:\Program Files\Dassault Systemes\SolidWorks Corp\SOLIDWORKS\sldworks.exe"
        })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return string.Empty;
    }

    private void StartLaunchWatcher(DateTime startedUtc)
    {
        _launchWatcher?.Stop();
        _launchWatcher?.Dispose();

        _launchWatcher = new System.Windows.Forms.Timer { Interval = 1500 };
        bool blockerLogged = false;
        _launchWatcher.Tick += (s, _) =>
        {
            try
            {
                // Success: sldworks.exe is up.
                if (Process.GetProcessesByName("sldworks").Length > 0
                    || Process.GetProcessesByName("SLDWORKS").Length > 0)
                {
                    StopLaunchWatcher();
                    Log("    SOLIDWORKS process detected — launch successful.");
                    RefreshAll();
                    return;
                }

                // Blocker scan: launcher window titles for known patterns.
                if (!blockerLogged)
                {
                    foreach (string procName in new[] { "SWXDesktopLauncher", "CATSTART" })
                    {
                        foreach (var p in Process.GetProcessesByName(procName))
                        {
                            string title = p.MainWindowTitle ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(title)) continue;
                            foreach (var bp in LauncherBlockerPatterns)
                            {
                                if (title.IndexOf(bp.Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    Log("    BLOCKER: " + bp.Reason);
                                    Log("    Recovery: " + bp.Recovery);
                                    Log("    Window title: " + title);
                                    blockerLogged = true;
                                    break;
                                }
                            }
                            if (blockerLogged) break;
                        }
                        if (blockerLogged) break;
                    }
                }

                // 60-second wall: if neither success nor blocker, give up watching.
                if ((DateTime.UtcNow - startedUtc).TotalSeconds > 60)
                {
                    StopLaunchWatcher();
                    Log("    Watcher timed out at 60s. SOLIDWORKS may still be initialising.");
                    Log("    Check %LOCALAPPDATA%\\Adze\\logs\\host.log once SW finishes loading.");
                    RefreshAll();
                }
            }
            catch (Exception ex)
            {
                StopLaunchWatcher();
                Log("    Watcher error: " + ex.Message);
            }
        };
        _launchWatcher.Start();
    }

    private void StopLaunchWatcher()
    {
        if (_launchWatcher != null)
        {
            _launchWatcher.Stop();
            _launchWatcher.Dispose();
            _launchWatcher = null;
        }
        _btnLaunchSw.Enabled = true;
        ApplyLaunchButtonState();
    }

    /// <summary>
    /// Refresh the Launch button label/color based on observed state. Called
    /// from RefreshAll, OnFormActivated, and after the launch watcher finishes.
    /// </summary>
    private void ApplyLaunchButtonState()
    {
        bool updaterRunning = Process.GetProcessesByName("swxdesktopupdate").Length > 0;
        bool swRunning = Process.GetProcessesByName("sldworks").Length > 0
                         || Process.GetProcessesByName("SLDWORKS").Length > 0;

        if (updaterRunning)
        {
            _btnLaunchSw.Text = LaunchLabelBlocked;
            _btnLaunchSw.BackColor = LaunchColorBlocked;
            _btnLaunchSw.ForeColor = Color.White;
            _btnLaunchSw.FlatAppearance.BorderColor = LaunchColorBlocked;
        }
        else if (swRunning)
        {
            _btnLaunchSw.Text = LaunchLabelFocus;
            _btnLaunchSw.BackColor = LaunchColorFocus;
            _btnLaunchSw.ForeColor = Color.White;
            _btnLaunchSw.FlatAppearance.BorderColor = LaunchColorFocus;
        }
        else
        {
            _btnLaunchSw.Text = LaunchLabelLaunch;
            _btnLaunchSw.BackColor = LaunchColorNormal;
            _btnLaunchSw.ForeColor = Color.White;
            _btnLaunchSw.FlatAppearance.BorderColor = LaunchColorNormal;
        }
    }

    private static class NativeFocus
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    /// <summary>
    /// One-shot diagnostic that walks every install + COM + env-var + DLL
    /// surface that affects whether SOLIDWORKS will load Adze with the new
    /// native sidebar. Prints a checklist into the Logs tab and pops a
    /// summary dialog. Designed so a non-technical user can answer
    /// "did I do everything I needed to do?" without reading host.log.
    /// </summary>
    private void OnVerifySetupClick(object? sender, EventArgs e)
    {
        Log("");
        Log("=== Verify Setup ===");
        RunVerifyChecks(showPromptIfGateOff: true);
    }

    /// <summary>
    /// Runs the verification checklist without the interactive "enable gate?"
    /// prompt — used after Install / Uninstall to give the user immediate
    /// proof the script actually changed the right registry/filesystem state.
    /// </summary>
    private void RunVerifyChecksOnly()
    {
        RunVerifyChecks(showPromptIfGateOff: false);
    }

    private void RunVerifyChecks(bool showPromptIfGateOff)
    {
        var checks = new System.Collections.Generic.List<(string Label, bool Ok, string Detail)>();

        // 1. Adze install dir + DLLs present
        string installDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Adze", "bin");
        bool dirOk = System.IO.Directory.Exists(installDir);
        checks.Add(("Install directory exists", dirOk, dirOk ? installDir : "Missing — run Install"));

        string[] expectedDlls = { "Adze.Host.dll", "Adze.Broker.dll", "Adze.Tools.dll", "Adze.Trace.dll", "Adze.Contracts.dll", "Adze.Index.dll", "Adze.UI.dll", "OpenMcdf.dll" };
        var missing = new System.Collections.Generic.List<string>();
        if (dirOk)
        {
            foreach (string dll in expectedDlls)
            {
                if (!System.IO.File.Exists(System.IO.Path.Combine(installDir, dll))) missing.Add(dll);
            }
        }
        checks.Add(("All required DLLs installed", dirOk && missing.Count == 0,
            missing.Count == 0 ? "8/8 present including Adze.UI.dll" : "Missing: " + string.Join(", ", missing)));

        // 2. COM registrations under HKCU
        bool legacyComOk = ComKeyExists(@"Software\Classes\CLSID\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}");
        bool taskPaneComOk = ComKeyExists(@"Software\Classes\CLSID\{F4068202-600A-4D6F-973B-DA2048A949CF}");
        bool nativeShimComOk = ComKeyExists(@"Software\Classes\CLSID\{C8B41F45-D2A6-4B5E-9F7C-3E0A1D8B2F61}");
        checks.Add(("Legacy AddIn CLSID registered", legacyComOk, legacyComOk ? "OK" : "Missing — re-run Install"));
        checks.Add(("Legacy TaskPaneControl CLSID registered", taskPaneComOk, taskPaneComOk ? "OK" : "Missing — re-run Install"));
        checks.Add(("v1.1 NativeTaskPaneControl shim CLSID registered", nativeShimComOk, nativeShimComOk ? "OK" : "Missing — re-run Install (zip must be v1.1+)"));

        // 3. SOLIDWORKS add-in registration
        bool addinRegOk = ComKeyExists(@"Software\SolidWorks\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}");
        bool addinAutostartOk = ComKeyExists(@"Software\SolidWorks\AddInsStartup\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}");
        checks.Add(("SOLIDWORKS AddIn registered", addinRegOk, addinRegOk ? "OK" : "Missing — re-run Install"));
        checks.Add(("Auto-start enabled", addinAutostartOk, addinAutostartOk ? "OK" : "Missing — re-run Install"));

        // 4. Native sidebar feature gate
        string nativeGateValue = Environment.GetEnvironmentVariable("SOLIDWORKS_AI_NATIVE_SIDEBAR", EnvironmentVariableTarget.User) ?? "(unset)";
        bool nativeGateOn = string.Equals(nativeGateValue, "true", StringComparison.OrdinalIgnoreCase) || nativeGateValue == "1";
        checks.Add(("New native sidebar gate (SOLIDWORKS_AI_NATIVE_SIDEBAR)",
            nativeGateOn,
            nativeGateOn ? "TRUE — new sidebar will load" : "current value: " + nativeGateValue + ". Click 'Yes' on next prompt to enable."));

        // 5. API key store (informational only — not gating)
        bool keyOk = false;
        try { keyOk = Adze.Broker.Configuration.ApiKeyStore.HasStoredKey(); } catch { /* swallow — informational */ }
        checks.Add(("API key stored", keyOk, keyOk ? "OK" : "(not stored — deterministic broker will be used)"));

        // 6. SOLIDWORKS not currently running (recommended for re-install)
        bool swRunning = System.Diagnostics.Process.GetProcessesByName("sldworks").Length > 0
                       || System.Diagnostics.Process.GetProcessesByName("SLDWORKS").Length > 0;
        checks.Add(("SOLIDWORKS not running (good for re-install)", !swRunning, swRunning ? "RUNNING — close before re-install or use Eject" : "OK"));

        // Render checklist into log
        int passed = 0;
        foreach (var c in checks)
        {
            string mark = c.Ok ? "[OK]" : "[ -- ]";
            Log("  " + mark + "  " + c.Label + ": " + c.Detail);
            if (c.Ok) passed++;
        }
        Log("");
        Log("Verify summary: " + passed + "/" + checks.Count + " checks passed.");

        // Interactive prompt for the gate flip — only when the user
        // explicitly clicked Verify Setup. Skipped after Install/Uninstall
        // so the auto-verify pass is silent on success and informational on
        // mismatch.
        if (showPromptIfGateOff && !nativeGateOn)
        {
            var prompt = MessageBox.Show(this,
                "The new v1.1 native sidebar is currently disabled (SOLIDWORKS_AI_NATIVE_SIDEBAR is " + nativeGateValue + ").\n\n" +
                "Enable it now? You will then need to relaunch SOLIDWORKS (and possibly close + reopen the document) for the change to take effect.\n\n" +
                "If you say No, the legacy WebBrowser-based sidebar remains active.",
                "Enable native sidebar?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (prompt == DialogResult.Yes)
            {
                Environment.SetEnvironmentVariable("SOLIDWORKS_AI_NATIVE_SIDEBAR", "true", EnvironmentVariableTarget.User);
                Log("  Set SOLIDWORKS_AI_NATIVE_SIDEBAR=true (User scope). Relaunch SOLIDWORKS to load the new sidebar.");
                MessageBox.Show(this,
                    "SOLIDWORKS_AI_NATIVE_SIDEBAR=true was set in User-scope environment.\n\n" +
                    "Now: relaunch SOLIDWORKS. The new sidebar should appear when SW reloads the add-in.",
                    "Native sidebar enabled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        else if (showPromptIfGateOff)
        {
            string summary = passed == checks.Count
                ? "All checks passed. Relaunch SOLIDWORKS if you just changed something."
                : (checks.Count - passed) + " issue(s) found - see the Logs tab for the checklist.";
            MessageBox.Show(this, summary, "Verify Setup",
                MessageBoxButtons.OK,
                passed == checks.Count ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }

    private static bool ComKeyExists(string subKeyPath)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            return key != null;
        }
        catch
        {
            return false;
        }
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

    /// <summary>
    /// Append a line to the Logs tab's script log. Keeps the historical
    /// MainForm.Log() shape so existing call sites do not need to change.
    /// </summary>
    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action<string>)Log, message);
            return;
        }
        _logsTab.AppendScriptLine(message);
    }
}
