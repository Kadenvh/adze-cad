using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Adze.Host.Infrastructure;

namespace Adze.Host.AddIn;

/// <summary>
/// Injects "Ask Adze" items into SOLIDWORKS right-click context menus for
/// feature-tree features, assembly components, and the empty graphics area.
///
/// Each context menu is a separate <see cref="ICommandGroup"/> bound to a
/// specific <c>swSelectType_e</c> via <c>SelectType</c>. Items route through
/// the SOLIDWORKS callback dispatcher to public methods on <see cref="AdzeAddIn"/>
/// which delegate to <see cref="HostState.InvokeQuickAction"/> — the same entry
/// point the ribbon and in-pane toolbar use.
///
/// Feature-gated by <c>SOLIDWORKS_AI_CONTEXT_MENU=true</c>. Each of the three
/// menus registers independently; a partial failure still leaves working
/// items where possible.
/// </summary>
internal sealed class ContextMenu
{
    // User IDs are picked to avoid collision with the ribbon (0xA02E) while
    // staying in Adze's own numeric band for easy identification.
    private const int UserIdFeature   = 0xA030;
    private const int UserIdComponent = 0xA031;
    private const int UserIdNothing   = 0xA032;

    private ICommandManager? _commandManager;
    private ICommandGroup? _featureMenu;
    private ICommandGroup? _componentMenu;
    private ICommandGroup? _nothingMenu;
    private bool _registered;

    public bool IsRegistered => _registered;

    [HandleProcessCorruptedStateExceptions]
    [SecurityCritical]
    public bool Register(ISldWorks application, int cookie)
    {
        if (application == null) return false;

        try
        {
            _commandManager = application.GetCommandManager(cookie);
            if (_commandManager == null)
            {
                FileLogger.Info("ContextMenu: GetCommandManager returned null; skipping.");
                return false;
            }

            _featureMenu   = TryRegisterMenu(UserIdFeature,   "Adze (Feature)",   swSelectType_e.swSelBODYFEATURES, "Ask Adze about this feature",   "Describe the selected feature in context of the current document.", nameof(AdzeAddIn.ContextMenuExplainFeature));
            _componentMenu = TryRegisterMenu(UserIdComponent, "Adze (Component)", swSelectType_e.swSelCOMPONENTS,   "Ask Adze about this component", "Summarize the selected component and its role in the assembly.",     nameof(AdzeAddIn.ContextMenuExplainComponent));
            _nothingMenu   = TryRegisterMenu(UserIdNothing,   "Adze (Diagnose)",  swSelectType_e.swSelNOTHING,      "Diagnose this model",           "Run Adze's diagnostic sweep across features, mates, and rebuild errors.", nameof(AdzeAddIn.ContextMenuDiagnoseDocument));

            _registered = _featureMenu != null || _componentMenu != null || _nothingMenu != null;
            if (_registered)
            {
                FileLogger.Info("ContextMenu: registered (features=" + (_featureMenu != null) + ", components=" + (_componentMenu != null) + ", background=" + (_nothingMenu != null) + ").");
            }
            return _registered;
        }
        catch (Exception ex)
        {
            FileLogger.Error("ContextMenu: registration threw; continuing without context menus.", ex);
            return false;
        }
    }

    [HandleProcessCorruptedStateExceptions]
    [SecurityCritical]
    private ICommandGroup? TryRegisterMenu(int userId, string title, swSelectType_e selectType, string label, string tooltip, string callbackName)
    {
        try
        {
            ICommandGroup? group = _commandManager!.AddContextMenu(userId, title);
            if (group == null)
            {
                FileLogger.Info("ContextMenu: AddContextMenu returned null for '" + title + "'.");
                return null;
            }

            // Show this group's item across all three document types so
            // selections inside parts, assemblies, and drawings all surface
            // the entry.
            group.ShowInDocumentType =
                (int)swDocumentTypes_e.swDocPART |
                (int)swDocumentTypes_e.swDocASSEMBLY |
                (int)swDocumentTypes_e.swDocDRAWING;

            group.SelectType = (int)selectType;

            int itemId = group.AddCommandItem2(
                label,
                -1,
                tooltip,
                tooltip,
                -1,
                callbackName,
                string.Empty,
                userId,
                (int)swCommandItemType_e.swMenuItem);

            if (itemId < 0)
            {
                FileLogger.Info("ContextMenu: AddCommandItem2 failed for '" + title + "'.");
                return null;
            }

            group.HasToolbar = false;
            group.HasMenu = true;
            bool ok = group.Activate();
            if (!ok)
            {
                FileLogger.Info("ContextMenu: Activate returned false for '" + title + "'.");
            }
            return group;
        }
        catch (Exception ex)
        {
            FileLogger.Error("ContextMenu: failed to register '" + title + "'.", ex);
            return null;
        }
    }

    public void Unregister()
    {
        if (!_registered && _commandManager == null) return;

        TryReleaseGroup(_featureMenu,   UserIdFeature);
        TryReleaseGroup(_componentMenu, UserIdComponent);
        TryReleaseGroup(_nothingMenu,   UserIdNothing);

        _featureMenu = null;
        _componentMenu = null;
        _nothingMenu = null;

        if (_commandManager != null)
        {
            try
            {
                Marshal.FinalReleaseComObject(_commandManager);
            }
            catch
            {
                // Already released; swallow.
            }
            _commandManager = null;
        }

        _registered = false;
    }

    private void TryReleaseGroup(ICommandGroup? group, int userId)
    {
        if (group == null) return;
        try
        {
            _commandManager?.RemoveCommandGroup2(userId, true);
        }
        catch (Exception ex)
        {
            FileLogger.Error("ContextMenu: RemoveCommandGroup2 failed for id " + userId + ".", ex);
        }
        finally
        {
            try { Marshal.FinalReleaseComObject(group); } catch { /* already released */ }
        }
    }
}
