using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using Adze.Host.Infrastructure;
using Adze.Host.UI;

namespace Adze.Host.AddIn;

[ComVisible(true)]
[Guid(AddInGuid)]
[ProgId(ProgIdValue)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class AdzeAddIn : ISwAddin
{
    public const string AddInGuid = "A2E09EE4-BB43-4A0C-945F-14711F792EFA";
    public const string ProgIdValue = "Adze.Host.AddIn";
    private const string Title = "Adze for SOLIDWORKS";
    private const string Description = "Native AI assistant add-in for SOLIDWORKS.";

    private ISldWorks? _application;
    private TaskpaneView? _taskPaneView;
    private int _cookie;
    private DSldWorksEvents_Event? _applicationEvents;
    private DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler? _activeModelDocChangeHandler;
    private DSldWorksEvents_ActiveDocChangeNotifyEventHandler? _activeDocChangeHandler;
    private DSldWorksEvents_FileOpenPostNotifyEventHandler? _fileOpenPostHandler;
    private DPartDocEvents_Event? _partDocEvents;
    private DPartDocEvents_NewSelectionNotifyEventHandler? _partNewSelectionHandler;
    private DPartDocEvents_ClearSelectionsNotifyEventHandler? _partClearSelectionsHandler;
    private DAssemblyDocEvents_Event? _assemblyDocEvents;
    private DAssemblyDocEvents_NewSelectionNotifyEventHandler? _assemblyNewSelectionHandler;
    private DAssemblyDocEvents_ClearSelectionsNotifyEventHandler? _assemblyClearSelectionsHandler;
    private DDrawingDocEvents_Event? _drawingDocEvents;
    private DDrawingDocEvents_NewSelectionNotifyEventHandler? _drawingNewSelectionHandler;
    private DDrawingDocEvents_ClearSelectionsNotifyEventHandler? _drawingClearSelectionsHandler;

    public bool ConnectToSW(object thisSw, int cookie)
    {
        try
        {
            _application = (ISldWorks)thisSw;
            _cookie = cookie;

            FileLogger.Info("ConnectToSW starting.");
            HostState.SetApplication(_application);
            _application.SetAddinCallbackInfo2(0, new DispatchWrapper(this), _cookie);
            AttachApplicationEvents();
            AttachActiveDocumentEvents();
            CreateTaskPane();
            HostState.LogSnapshot("Initial context snapshot");
            FileLogger.Info("ConnectToSW completed.");
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Error("ConnectToSW failed.", ex);
            return false;
        }
    }

    public bool DisconnectFromSW()
    {
        try
        {
            FileLogger.Info("DisconnectFromSW starting.");
            DetachApplicationEvents();
            DetachActiveDocumentEvents();
            DestroyTaskPane();
            HostState.SetApplication(null);
            _application = null;
            FileLogger.Info("DisconnectFromSW completed.");
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Error("DisconnectFromSW failed.", ex);
            return false;
        }
    }

    [ComRegisterFunction]
    public static void RegisterFunction(Type type)
    {
        using RegistryKey? userAddInsKey = Registry.CurrentUser.CreateSubKey($@"Software\SolidWorks\AddIns\{{{type.GUID}}}");
        userAddInsKey?.SetValue(null, 1, RegistryValueKind.DWord);
        userAddInsKey?.SetValue("Title", Title, RegistryValueKind.String);
        userAddInsKey?.SetValue("Description", Description, RegistryValueKind.String);

        using RegistryKey? startupKey = Registry.CurrentUser.CreateSubKey($@"Software\SolidWorks\AddInsStartup\{{{type.GUID}}}");
        startupKey?.SetValue(null, 1, RegistryValueKind.DWord);
    }

    [ComUnregisterFunction]
    public static void UnregisterFunction(Type type)
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\SolidWorks\AddIns\{{{type.GUID}}}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\SolidWorks\AddInsStartup\{{{type.GUID}}}", false);
    }

    private void CreateTaskPane()
    {
        if (_application == null || _taskPaneView != null)
        {
            return;
        }

        string iconPath = TaskPaneIcon.Ensure();
        _taskPaneView = _application.CreateTaskpaneView2(iconPath, Title);
        _taskPaneView.AddControl(TaskPaneControl.ProgIdValue, string.Empty);
        FileLogger.Info("Task Pane created.");
    }

    private void DestroyTaskPane()
    {
        if (_taskPaneView == null)
        {
            return;
        }

        try
        {
            _taskPaneView.DeleteView();
            FileLogger.Info("Task Pane deleted.");
        }
        finally
        {
            _taskPaneView = null;
        }
    }

    private void AttachApplicationEvents()
    {
        if (_application is not DSldWorksEvents_Event applicationEvents)
        {
            return;
        }

        _applicationEvents = applicationEvents;
        _activeModelDocChangeHandler = OnActiveModelDocChange;
        _activeDocChangeHandler = OnActiveDocChange;
        _fileOpenPostHandler = OnFileOpenPostNotify;

        _applicationEvents.ActiveModelDocChangeNotify += _activeModelDocChangeHandler;
        _applicationEvents.ActiveDocChangeNotify += _activeDocChangeHandler;
        _applicationEvents.FileOpenPostNotify += _fileOpenPostHandler;
    }

    private void DetachApplicationEvents()
    {
        if (_applicationEvents == null)
        {
            return;
        }

        if (_activeModelDocChangeHandler != null)
        {
            _applicationEvents.ActiveModelDocChangeNotify -= _activeModelDocChangeHandler;
        }

        if (_activeDocChangeHandler != null)
        {
            _applicationEvents.ActiveDocChangeNotify -= _activeDocChangeHandler;
        }

        if (_fileOpenPostHandler != null)
        {
            _applicationEvents.FileOpenPostNotify -= _fileOpenPostHandler;
        }

        _applicationEvents = null;
        _activeModelDocChangeHandler = null;
        _activeDocChangeHandler = null;
        _fileOpenPostHandler = null;
    }

    private int OnActiveModelDocChange()
    {
        AttachActiveDocumentEvents();
        HostState.LogSnapshot("ActiveModelDocChangeNotify");
        return 0;
    }

    private int OnActiveDocChange()
    {
        AttachActiveDocumentEvents();
        HostState.LogSnapshot("ActiveDocChangeNotify");
        return 0;
    }

    private int OnFileOpenPostNotify(string fileName)
    {
        AttachActiveDocumentEvents();
        HostState.LogSnapshot("FileOpenPostNotify: " + fileName);
        return 0;
    }

    private void AttachActiveDocumentEvents()
    {
        DetachActiveDocumentEvents();

        if (_application?.IActiveDoc2 is not ModelDoc2 model)
        {
            return;
        }

        switch (model.GetType())
        {
            case 1:
            {
                PartDoc? partDoc = model as PartDoc;
                if (partDoc is not DPartDocEvents_Event partEvents)
                {
                    return;
                }

                _partDocEvents = partEvents;
                _partNewSelectionHandler = OnPartNewSelection;
                _partClearSelectionsHandler = OnPartClearSelections;
                _partDocEvents.NewSelectionNotify += _partNewSelectionHandler;
                _partDocEvents.ClearSelectionsNotify += _partClearSelectionsHandler;
                return;
            }
            case 2:
            {
                AssemblyDoc? assemblyDoc = model as AssemblyDoc;
                if (assemblyDoc is not DAssemblyDocEvents_Event assemblyEvents)
                {
                    return;
                }

                _assemblyDocEvents = assemblyEvents;
                _assemblyNewSelectionHandler = OnAssemblyNewSelection;
                _assemblyClearSelectionsHandler = OnAssemblyClearSelections;
                _assemblyDocEvents.NewSelectionNotify += _assemblyNewSelectionHandler;
                _assemblyDocEvents.ClearSelectionsNotify += _assemblyClearSelectionsHandler;
                return;
            }
            case 3:
            {
                DrawingDoc? drawingDoc = model as DrawingDoc;
                if (drawingDoc is not DDrawingDocEvents_Event drawingEvents)
                {
                    return;
                }

                _drawingDocEvents = drawingEvents;
                _drawingNewSelectionHandler = OnDrawingNewSelection;
                _drawingClearSelectionsHandler = OnDrawingClearSelections;
                _drawingDocEvents.NewSelectionNotify += _drawingNewSelectionHandler;
                _drawingDocEvents.ClearSelectionsNotify += _drawingClearSelectionsHandler;
                return;
            }
        }
    }

    private void DetachActiveDocumentEvents()
    {
        if (_partDocEvents != null)
        {
            if (_partNewSelectionHandler != null)
            {
                _partDocEvents.NewSelectionNotify -= _partNewSelectionHandler;
            }

            if (_partClearSelectionsHandler != null)
            {
                _partDocEvents.ClearSelectionsNotify -= _partClearSelectionsHandler;
            }
        }

        if (_assemblyDocEvents != null)
        {
            if (_assemblyNewSelectionHandler != null)
            {
                _assemblyDocEvents.NewSelectionNotify -= _assemblyNewSelectionHandler;
            }

            if (_assemblyClearSelectionsHandler != null)
            {
                _assemblyDocEvents.ClearSelectionsNotify -= _assemblyClearSelectionsHandler;
            }
        }

        if (_drawingDocEvents != null)
        {
            if (_drawingNewSelectionHandler != null)
            {
                _drawingDocEvents.NewSelectionNotify -= _drawingNewSelectionHandler;
            }

            if (_drawingClearSelectionsHandler != null)
            {
                _drawingDocEvents.ClearSelectionsNotify -= _drawingClearSelectionsHandler;
            }
        }

        _partDocEvents = null;
        _partNewSelectionHandler = null;
        _partClearSelectionsHandler = null;
        _assemblyDocEvents = null;
        _assemblyNewSelectionHandler = null;
        _assemblyClearSelectionsHandler = null;
        _drawingDocEvents = null;
        _drawingNewSelectionHandler = null;
        _drawingClearSelectionsHandler = null;
    }

    private int OnPartNewSelection()
    {
        HostState.LogSnapshot("PartDoc NewSelectionNotify");
        return 0;
    }

    private int OnPartClearSelections()
    {
        HostState.LogSnapshot("PartDoc ClearSelectionsNotify");
        return 0;
    }

    private int OnAssemblyNewSelection()
    {
        HostState.LogSnapshot("AssemblyDoc NewSelectionNotify");
        return 0;
    }

    private int OnAssemblyClearSelections()
    {
        HostState.LogSnapshot("AssemblyDoc ClearSelectionsNotify");
        return 0;
    }

    private int OnDrawingNewSelection()
    {
        HostState.LogSnapshot("DrawingDoc NewSelectionNotify");
        return 0;
    }

    private int OnDrawingClearSelections()
    {
        HostState.LogSnapshot("DrawingDoc ClearSelectionsNotify");
        return 0;
    }
}
