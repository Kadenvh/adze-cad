using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Adze.Broker.Configuration;
using Adze.Host.Infrastructure;

namespace Adze.Host.UI;

/// <summary>
/// Surfaces pending write actions in a native SOLIDWORKS PropertyManager Page
/// so write operations feel like first-class SW commands. The existing HTML
/// Task Pane card flow remains the default; this path is an opt-in preview
/// gated by <c>SOLIDWORKS_AI_PMP_WRITES=true</c>.
///
/// Show is non-blocking: <see cref="IPropertyManagerPage2.Show"/> returns
/// immediately and the <see cref="WriteConfirmationHandler.OnClose"/> callback
/// fires asynchronously when the user clicks OK / Cancel / closes the page.
/// The handler routes back into <see cref="HostState.ApplyPendingWrite"/> or
/// <see cref="HostState.CancelPendingWrite"/>, giving PMP writes identical
/// application semantics to the Task Pane card flow.
///
/// v0.1.2 ships as a proof-of-path for <c>set_dimension_value</c> only; other
/// write tools fall through to the HTML card. v0.2.0 expands coverage.
/// </summary>
public static class PropertyManagerPageBroker
{
    private const int PageIdBase = 0x2000;

    private static readonly object _lock = new();
    private static IPropertyManagerPage2? _currentPage;
    private static WriteConfirmationHandler? _currentHandler;

    public static bool IsEnabled =>
        FeatureGateRegistry.IsEnabled(FeatureGateRegistry.PropertyManagerPageWrites);

    /// <summary>
    /// Attempt to show a PMP for the pending write at the given index.
    /// Returns false (without side effects) if the gate is off, the active
    /// document is unavailable, or the tool is not yet supported on this path.
    /// Callers can then fall back to the Task Pane card UI.
    /// </summary>
    public static bool TryShow(int pendingWriteIndex)
    {
        if (!IsEnabled) return false;

        PendingWriteAction? action = LookupAction(pendingWriteIndex);
        if (action == null) return false;
        if (!IsSupported(action.ToolName)) return false;

        ISldWorks? app = HostState.GetApplication();
        if (app == null) return false;

        ModelDoc2? doc = null;
        try
        {
            doc = app.IActiveDoc2 as ModelDoc2;
            if (doc == null)
            {
                FileLogger.Info("PMP: no active doc; falling back to card flow.");
                return false;
            }

            var handler = new WriteConfirmationHandler(pendingWriteIndex);
            string title = "Confirm: " + action.ToolName;

            IPropertyManagerPage2? page;
            try
            {
                // IModelDoc2.GetPropertyManagerPage(int DialogId, string Title, object Handler)
                page = doc.GetPropertyManagerPage(
                    PageIdBase + pendingWriteIndex,
                    title,
                    handler) as IPropertyManagerPage2;
            }
            catch (Exception ex)
            {
                FileLogger.Error("PMP: GetPropertyManagerPage threw; falling back.", ex);
                return false;
            }

            if (page == null)
            {
                FileLogger.Info("PMP: GetPropertyManagerPage returned null; falling back.");
                return false;
            }

            try
            {
                PopulateControls(page, action);
            }
            catch (Exception ex)
            {
                FileLogger.Error("PMP: PopulateControls failed; closing page and falling back.", ex);
                try { page.Close(false); } catch { }
                try { Marshal.FinalReleaseComObject(page); } catch { }
                return false;
            }

            lock (_lock)
            {
                // Keep both alive so the GC cannot collect the handler before
                // SOLIDWORKS invokes its callbacks.
                _currentPage = page;
                _currentHandler = handler;
            }

            page.Show();
            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Error("PMP: TryShow threw; falling back.", ex);
            ClearActiveHandler(null);
            return false;
        }
    }

    /// <summary>
    /// Release the active handler reference once SOLIDWORKS is done with the
    /// page. Called from <see cref="WriteConfirmationHandler.OnClose"/>.
    /// </summary>
    public static void ClearActiveHandler(WriteConfirmationHandler? handler)
    {
        lock (_lock)
        {
            // If a stale clear fires for a handler that is no longer current,
            // leave the active one alone.
            if (handler != null && !ReferenceEquals(handler, _currentHandler))
            {
                return;
            }

            if (_currentPage != null)
            {
                try { Marshal.FinalReleaseComObject(_currentPage); } catch { }
                _currentPage = null;
            }
            _currentHandler = null;
        }
    }

    private static PendingWriteAction? LookupAction(int index)
    {
        var list = HostState.GetPendingWrites();
        if (index < 0 || index >= list.Count) return null;
        PendingWriteAction action = list[index];
        if (action.Applied || action.Cancelled) return null;
        return action;
    }

    /// <summary>
    /// v0.1.2 PMP coverage is intentionally narrow. Only set_dimension_value
    /// is shipped as the proof-of-path surface. Other tools fall through to
    /// the HTML card flow.
    /// </summary>
    private static bool IsSupported(string toolName)
    {
        return string.Equals(toolName, "set_dimension_value", StringComparison.OrdinalIgnoreCase);
    }

    private static void PopulateControls(IPropertyManagerPage2 page, PendingWriteAction action)
    {
        // Single group box to hold preview labels; SW renders OK/Cancel
        // buttons for us so we do not need to add them as controls.
        IPropertyManagerPageGroup? group = page.AddGroupBox(
            0,
            "Write Preview",
            (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
            (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded) as IPropertyManagerPageGroup;

        if (group == null)
        {
            throw new InvalidOperationException("AddGroupBox returned null.");
        }

        AddLabel(group, 1, "Summary", action.Preview?.Summary ?? action.ToolName);

        string undoLabel = string.IsNullOrEmpty(action.UndoLabel) ? "(no undo label set)" : action.UndoLabel;
        AddLabel(group, 2, "Undo label", undoLabel);

        if (action.IsElevated)
        {
            AddLabel(group, 3, "Class", "Elevated change — confirm carefully.");
        }

        // Dimension-specific before/after snapshot.
        if (action.Arguments.TryGetValue("new_value", out object? newValObj) && newValObj != null)
        {
            AddLabel(group, 4, "New value", newValObj.ToString() ?? string.Empty);
        }

        if (action.Arguments.TryGetValue("dimension_name", out object? dimObj) && dimObj != null)
        {
            AddLabel(group, 5, "Dimension", dimObj.ToString() ?? string.Empty);
        }
    }

    private static void AddLabel(IPropertyManagerPageGroup group, short tag, string caption, string text)
    {
        // Two labels per "row": caption on the left, value on the right. This
        // is the cheapest form of read-only preview in a PMP.
        var captionControl = group.AddControl2(
            tag,
            (short)swPropertyManagerPageControlType_e.swControlType_Label,
            caption + ":",
            (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge,
            (int)swAddControlOptions_e.swControlOptions_Visible |
                (int)swAddControlOptions_e.swControlOptions_Enabled,
            string.Empty) as IPropertyManagerPageLabel;

        if (captionControl != null)
        {
            // Already captioned via AddControl2; nothing more needed.
        }

        var valueControl = group.AddControl2(
            (short)(tag + 100),
            (short)swPropertyManagerPageControlType_e.swControlType_Label,
            text,
            (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent,
            (int)swAddControlOptions_e.swControlOptions_Visible |
                (int)swAddControlOptions_e.swControlOptions_Enabled,
            string.Empty) as IPropertyManagerPageLabel;

        if (valueControl == null)
        {
            FileLogger.Info("PMP: value label creation returned null for tag " + tag + ".");
        }
    }
}
