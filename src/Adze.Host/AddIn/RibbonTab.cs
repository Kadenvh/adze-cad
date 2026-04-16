using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Adze.Host.Infrastructure;

namespace Adze.Host.AddIn;

/// <summary>
/// Registers a custom "Adze" CommandManager ribbon tab so quick actions are
/// discoverable without opening the Task Pane. The tab is global (one per
/// SOLIDWORKS instance) and never scoped to a specific document type, matching
/// SW's built-in always-visible tabs like Evaluate and Features.
///
/// Callbacks route through <see cref="HostState.InvokeQuickAction"/>, which
/// the Task Pane registers during initialization. This keeps the ribbon
/// completely decoupled from the UI control instance.
///
/// Feature-gated by <c>SOLIDWORKS_AI_RIBBON=true</c>. When the gate is off
/// the constructor short-circuits and the add-in runs Task-Pane-only.
/// </summary>
internal sealed class RibbonTab
{
    private const int UserGroupIdOffset = 0;
    private const string DocumentTypePartValue = "Parts";
    private const string DocumentTypeAssemblyValue = "Assemblies";
    private const string DocumentTypeDrawingValue = "Drawings";

    // Callback IDs are assigned per button and round-trip through the ISwAddin
    // callback dispatcher (SetAddinCallbackInfo2). Names must exactly match
    // the public method names declared on AdzeAddIn.
    private static readonly (int Id, string Label, string Tooltip, string CallbackName)[] Buttons =
    {
        (0, "Ask",        "Open the Adze Task Pane and focus the prompt box.", nameof(AdzeAddIn.RibbonAsk)),
        (1, "Diagnose",   "Find rebuild errors, over-constraints, and broken references in the active document.", nameof(AdzeAddIn.RibbonDiagnose)),
        (2, "Mates",      "List all mates in the active assembly with status and components.", nameof(AdzeAddIn.RibbonMates)),
        (3, "Dimensions", "Show the key dimensions, current values, and which features they control.", nameof(AdzeAddIn.RibbonDimensions)),
        (4, "Properties", "Show all custom properties on the active document.", nameof(AdzeAddIn.RibbonProperties)),
        (5, "Explain",    "Explain the currently selected feature or entity in detail.", nameof(AdzeAddIn.RibbonExplain))
    };

    private ICommandManager? _commandManager;
    private ICommandGroup? _commandGroup;
    private ICommandTab? _commandTab;
    private int _userGroupId;
    private bool _registered;

    public bool IsRegistered => _registered;

    /// <summary>
    /// Register the tab. Failures are caught and logged; a failed ribbon
    /// registration never hard-fails the add-in because the Task Pane
    /// remains fully functional without it.
    /// </summary>
    public bool Register(ISldWorks application, int cookie)
    {
        if (application == null) return false;

        try
        {
            _commandManager = application.GetCommandManager(cookie);
            if (_commandManager == null)
            {
                FileLogger.Info("RibbonTab: GetCommandManager returned null; skipping ribbon registration.");
                return false;
            }

            // A stable numeric id avoids conflicts with other add-ins that
            // also use low integers. Pick something unique-ish to Adze.
            _userGroupId = 0xA02E + UserGroupIdOffset;

            int errorCode = 0;
            _commandGroup = _commandManager.CreateCommandGroup2(
                _userGroupId,
                "Adze",
                "Adze AI assistant quick actions",
                "Ask Adze about the active document.",
                -1,
                true,
                ref errorCode);

            if (_commandGroup == null)
            {
                FileLogger.Info("RibbonTab: CreateCommandGroup2 returned null (error=" + errorCode + "); skipping ribbon.");
                return false;
            }

            foreach (var button in Buttons)
            {
                // Positional args because the SW interop parameter names vary
                // across SDK versions (Tooltip vs ToolTip etc.).
                int itemId = _commandGroup.AddCommandItem2(
                    button.Label,
                    -1,
                    button.Tooltip,
                    button.Tooltip,
                    button.Id,
                    button.CallbackName,
                    string.Empty,
                    button.Id,
                    (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);

                if (itemId < 0)
                {
                    FileLogger.Info("RibbonTab: AddCommandItem2 failed for '" + button.Label + "'.");
                }
            }

            _commandGroup.HasToolbar = true;
            _commandGroup.HasMenu = true;
            bool activated = _commandGroup.Activate();
            if (!activated)
            {
                FileLogger.Info("RibbonTab: CommandGroup.Activate returned false.");
            }

            _commandTab = _commandManager.GetCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, "Adze")
                           ?? _commandManager.AddCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, "Adze");
            AttachAllButtonsToTab(_commandTab);

            _commandTab = _commandManager.GetCommandTab((int)swDocumentTypes_e.swDocPART, "Adze")
                           ?? _commandManager.AddCommandTab((int)swDocumentTypes_e.swDocPART, "Adze");
            AttachAllButtonsToTab(_commandTab);

            _commandTab = _commandManager.GetCommandTab((int)swDocumentTypes_e.swDocDRAWING, "Adze")
                           ?? _commandManager.AddCommandTab((int)swDocumentTypes_e.swDocDRAWING, "Adze");
            AttachAllButtonsToTab(_commandTab);

            _registered = true;
            FileLogger.Info("RibbonTab: registered successfully across part/assembly/drawing contexts.");
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Error("RibbonTab: registration threw; continuing without ribbon.", ex);
            return false;
        }
    }

    private void AttachAllButtonsToTab(ICommandTab? tab)
    {
        if (tab == null || _commandGroup == null) return;
        try
        {
            ICommandTabBox? box = tab.AddCommandTabBox();
            if (box == null) return;

            int count = Buttons.Length;
            int[] commandIds = new int[count];
            int[] textTypes = new int[count];

            for (int i = 0; i < count; i++)
            {
                commandIds[i] = _commandGroup.get_CommandID(i);
                textTypes[i] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;
            }

            box.AddCommands(commandIds, textTypes);
        }
        catch (Exception ex)
        {
            FileLogger.Error("RibbonTab: AttachAllButtonsToTab failed for a document type.", ex);
        }
    }

    /// <summary>
    /// Unregister the tab and release COM handles.
    /// </summary>
    public void Unregister()
    {
        if (!_registered && _commandGroup == null && _commandManager == null) return;

        try
        {
            if (_commandManager != null && _userGroupId != 0)
            {
                int errorCode = 0;
                _commandManager.RemoveCommandGroup2(_userGroupId, true);
                _ = errorCode; // unused; RemoveCommandGroup2 does not populate
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("RibbonTab: RemoveCommandGroup2 failed.", ex);
        }
        finally
        {
            if (_commandTab != null)
            {
                Marshal.FinalReleaseComObject(_commandTab);
                _commandTab = null;
            }
            if (_commandGroup != null)
            {
                Marshal.FinalReleaseComObject(_commandGroup);
                _commandGroup = null;
            }
            if (_commandManager != null)
            {
                Marshal.FinalReleaseComObject(_commandManager);
                _commandManager = null;
            }
            _registered = false;
            _userGroupId = 0;
        }
    }
}
