using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using Adze.Host.Infrastructure;
using Adze.UI.V2;

namespace Adze.Host.UI;

/// <summary>
/// COM-registered host for the v1.1 native sidebar (<see cref="NativeTaskPaneControl"/>).
///
/// SOLIDWORKS' Task Pane API requires the registered control's class be COM-visible
/// with a stable ProgID + GUID. <see cref="NativeTaskPaneControl"/> lives in
/// <c>Adze.UI</c> which is intentionally not COM-registered (UI library reused by
/// the harness and any future surface). This shim is the COM boundary: it is
/// instantiated by SW via the registered ProgID, then on first load it pulls the
/// singleton <see cref="HostStateAdapter"/> from <see cref="HostState"/> and
/// mounts the inner native control filling its client area.
///
/// Lifecycle:
///   1. SW calls <c>TaskpaneView.AddControl("Adze.Host.NativeTaskPaneControl", ...)</c>.
///   2. COM creates this UserControl via parameterless ctor (mscoree.dll loader).
///   3. <see cref="OnHandleCreated"/> mounts <see cref="NativeTaskPaneControl"/>
///      bound against <see cref="HostState.GetTaskPaneHost"/>.
///   4. Inner control becomes the only child, Dock=Fill so the shim is invisible.
/// </summary>
[ComVisible(true)]
[Guid(GuidValue)]
[ProgId(ProgIdValue)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public sealed class NativeTaskPaneControlShim : UserControl
{
    public const string ProgIdValue = "Adze.Host.NativeTaskPaneControl";
    public const string GuidValue   = "C8B41F45-D2A6-4B5E-9F7C-3E0A1D8B2F61";

    private NativeTaskPaneControl? _inner;

    public NativeTaskPaneControlShim()
    {
        SuspendLayout();
        try
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            BackColor = Color.FromArgb(247, 248, 250);
        }
        finally
        {
            ResumeLayout(false);
        }
    }

    /// <summary>
    /// Mount the inner native control once the COM-created shim has its handle.
    /// Doing this in the ctor is unsafe — SW hands COM a pre-handle reference.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_inner != null) return;

        try
        {
            HostStateAdapter host = HostState.GetTaskPaneHost();
            _inner = new NativeTaskPaneControl(host)
            {
                Dock = DockStyle.Fill
            };
            Controls.Add(_inner);
            FileLogger.Info("NativeTaskPaneControlShim mounted v1.1 sidebar.");

            // Initial banner refresh + re-pull on every state change. The shim
            // is the bridge between host-side data sources (HostState +
            // BrokerModelSettings) and the v1.1 UI's banner panels.
            RefreshBanners();
            host.StateChanged += (_, _) =>
            {
                try { RefreshBanners(); } catch (Exception ex) { FileLogger.Error("Banner refresh failed.", ex); }
            };
        }
        catch (Exception ex)
        {
            FileLogger.Error("NativeTaskPaneControlShim mount failed.", ex);
            // Render a minimal error label so SW doesn't show an empty pane.
            var fallback = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Adze sidebar failed to load. Check %LOCALAPPDATA%\\Adze\\logs\\host.log.",
                ForeColor = Color.FromArgb(120, 30, 30),
                BackColor = Color.FromArgb(255, 245, 245)
            };
            Controls.Add(fallback);
        }
    }

    private void RefreshBanners()
    {
        if (_inner == null) return;

        // Probe banner — only when probe failed.
        var probe = HostState.GetProbeFailure();
        _inner.SetProbeBanner(probe.Message);

        // Health banner — only when local provider is configured AND health
        // check has run. LocalEndpointHealthCheck.Check is fired async by
        // HostState.RunLocalHealthCheckAsync(); we read whatever cached result
        // exists at refresh time.
        try
        {
            var settings = BrokerModelSettings.LoadFromEnvironment();
            if (settings.IsLocalProvider)
            {
                LocalHealthResult? health = HostState.GetLocalHealthResult();
                _inner.SetHealthBanner(health);
            }
            else
            {
                _inner.SetHealthBanner(null);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("Health banner read failed.", ex);
            _inner.SetHealthBanner(null);
        }

        // Budget banner — pull live status from HostState.
        try
        {
            BudgetStatus status = HostState.GetBudgetStatus();
            _inner.SetBudgetBanner(status);
        }
        catch (Exception ex)
        {
            FileLogger.Error("Budget banner read failed.", ex);
            _inner.SetBudgetBanner(null);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _inner?.Dispose(); } catch { /* shutting down */ }
            _inner = null;
        }
        base.Dispose(disposing);
    }
}
