using System;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.swconst;
using Adze.Host.Infrastructure;

namespace Adze.Host.UI;

/// <summary>
/// [ComVisible] callback receiver for a PropertyManager Page–based write
/// confirmation. SOLIDWORKS uses COM IDispatch to invoke the methods here by
/// name when the user interacts with the page. The class does not implement
/// a typed interface; the public method signatures below are the contract.
///
/// Routes OK/Cancel back into <see cref="HostState.ApplyPendingWrite"/> or
/// <see cref="HostState.CancelPendingWrite"/>, giving PMP writes exactly the
/// same application semantics as the Task Pane card flow.
/// </summary>
[ComVisible(true)]
public sealed class WriteConfirmationHandler
{
    private readonly int _pendingWriteIndex;
    // 0 = not disposed, 1 = dispose in-flight. Use Interlocked.CompareExchange
    // so a double OnClose (SW dispatch race, rapid double-click) routes to the
    // pipeline exactly once instead of racing two ApplyPendingWrite calls.
    private int _disposed;

    public WriteConfirmationHandler(int pendingWriteIndex)
    {
        _pendingWriteIndex = pendingWriteIndex;
    }

    /// <summary>
    /// Called by SOLIDWORKS when the user clicks OK, Cancel, or the X / Escape.
    /// Routes to the existing pending-write pipeline so the write follows the
    /// same apply/verify/trace flow regardless of surface.
    /// </summary>
    public void OnClose(int reason)
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        try
        {
            switch ((swPropertyManagerPageCloseReasons_e)reason)
            {
                case swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay:
                case swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Apply:
                    HostState.ApplyPendingWrite(_pendingWriteIndex);
                    break;
                case swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Cancel:
                case swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_UserEscape:
                default:
                    HostState.CancelPendingWrite(_pendingWriteIndex);
                    break;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Error("WriteConfirmationHandler.OnClose: routing failed for index " + _pendingWriteIndex + ".", ex);
        }
        finally
        {
            PropertyManagerPageBroker.ClearActiveHandler(this);
        }
    }

    // ---- Required no-op handlers that SOLIDWORKS discovers via IDispatch. ----
    // SW can invoke any of these during the page lifecycle. Providing empty
    // bodies avoids COM exceptions on method-lookup failures.

    public void OnActiveXControlCreated(int id, bool status) { }
    public void AfterClose() { }
    public void OnButtonPress(int id) { }
    public void OnCheckboxCheck(int id, bool check) { }
    public void OnComboboxEditChanged(int id, string text) { }
    public void OnComboboxSelectionChanged(int id, int item) { }
    public void OnGroupCheck(int id, bool check) { }
    public void OnGroupExpand(int id, bool expand) { }
    public void OnHelp() { }
    public void OnKeystroke(int wParam, int message, int lParam, int id) { }
    public bool OnNumberboxChanged(int id, double value) => true;
    public void OnNumberBoxTrackingCompleted(int id, double value) { }
    public bool OnOptionCheck(int id) => true;
    public bool OnPopupMenuItem(int id) => true;
    public bool OnPopupMenuItemUpdate(int id, ref int retval) => true;
    public bool OnPreview() => true;
    public bool OnRedo() => true;
    public void OnSelectionboxCalloutCreated(int id) { }
    public void OnSelectionboxCalloutDestroyed(int id) { }
    public bool OnSelectionboxFocusChanged(int id) => true;
    public void OnSelectionboxListChanged(int id, int count) { }
    public void OnSliderPositionChanged(int id, double val) { }
    public void OnSliderTrackingCompleted(int id, double val) { }
    public bool OnSubmitSelection(int id, object selection, int selType, ref string itemText) => true;
    public void OnTabClicked(int id) { }
    public bool OnTextboxChanged(int id, string text) => true;
    public bool OnUndo() => true;
    public void OnWhatsNew() { }
    public int OnWindowFromHandleControlGotFocus(int id) => 0;
    public void AfterActivation() { }
}
