using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Adze.Broker.Configuration;

namespace Adze.Host.Infrastructure;

/// <summary>
/// Surfaces run-completion toasts through a tray NotifyIcon so users who alt-tabbed
/// away from the Task Pane still see the result. Only fires when:
/// 1. SOLIDWORKS_AI_TOAST gate is on
/// 2. The SOLIDWORKS window is not currently the foreground window (so the user
///    actually alt-tabbed — no point popping a toast if they're already watching)
///
/// Uses a single shared NotifyIcon for the add-in session; initialized lazily on
/// first ShowCompletion call. Disposed via <see cref="Shutdown"/> at add-in teardown.
/// </summary>
public static class ToastNotifier
{
    private static readonly object _lock = new();
    private static NotifyIcon? _icon;
    private static bool _initFailed;

    private const int BalloonSuccessMs = 3000;
    private const int BalloonFailureMs = 6000;

    /// <summary>
    /// Show a run-completion toast. Silently no-op when the gate is off, when the
    /// SOLIDWORKS window is foregrounded, or when the NotifyIcon could not be created.
    /// </summary>
    public static void ShowCompletion(string title, string body, bool isError)
    {
        if (!FeatureGateRegistry.IsEnabled(FeatureGateRegistry.ToastNotifications))
        {
            return;
        }

        if (IsSolidWorksForeground())
        {
            return;
        }

        try
        {
            NotifyIcon? icon = EnsureIcon();
            if (icon == null) return;

            icon.BalloonTipTitle = title;
            icon.BalloonTipText  = body;
            icon.BalloonTipIcon  = isError ? ToolTipIcon.Warning : ToolTipIcon.Info;
            icon.Visible = true;
            icon.ShowBalloonTip(isError ? BalloonFailureMs : BalloonSuccessMs);
        }
        catch (Exception ex)
        {
            FileLogger.Error("ToastNotifier: ShowCompletion failed.", ex);
        }
    }

    public static void Shutdown()
    {
        lock (_lock)
        {
            if (_icon != null)
            {
                try
                {
                    _icon.Visible = false;
                    _icon.Dispose();
                }
                catch
                {
                    // Teardown is best-effort.
                }
                _icon = null;
            }
        }
    }

    private static NotifyIcon? EnsureIcon()
    {
        lock (_lock)
        {
            if (_initFailed) return null;
            if (_icon != null) return _icon;

            try
            {
                _icon = new NotifyIcon
                {
                    Icon = SystemIcons.Information,
                    Text = "Adze",
                    Visible = false
                };
                return _icon;
            }
            catch (Exception ex)
            {
                FileLogger.Error("ToastNotifier: NotifyIcon creation failed; disabling toasts for session.", ex);
                _initFailed = true;
                return null;
            }
        }
    }

    // ---- Foreground-window check via Win32 ----

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static bool IsSolidWorksForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            System.Diagnostics.Process proc = System.Diagnostics.Process.GetProcessById((int)pid);
            string name = proc.ProcessName ?? string.Empty;
            return name.IndexOf("SLDWORKS", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            // If we cannot determine the foreground process we default to
            // "probably not SW" so the toast fires — better to notify than swallow.
            return false;
        }
    }
}
