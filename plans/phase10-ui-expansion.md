# Phase 10 — UI Expansion Beyond the Task Pane

**Status:** Deferred to post-partner-submission (after 2026-04-20)
**Scope:** Expand Adze's presence in SOLIDWORKS beyond the Task Pane sidebar
**Last updated:** 2026-04-16

---

## Premise

Adze currently lives entirely in a Task Pane. That is a legitimate first surface but does not reflect the SOLIDWORKS API's full capability. SOLIDWORKS exposes at least eight distinct UI surfaces an add-in can occupy. Each has different discoverability, interaction, and integration properties. The product roadmap should expand across these deliberately, not by accident.

This plan enumerates the options, ranks them by product value, and stages the work.

---

## Surface inventory

### 1. Task Pane — current surface

- WinForms control hosted inside a `ITaskpaneView`
- Persistent sidebar, user-toggleable
- **Current state:** full chat UI, quick-action toolbar, tool chips, write cards, history, recipes, settings
- **Limit:** narrow width, requires user to open the pane, not in the line of fire during normal CAD work

### 2. CommandManager ribbon tab — PRIORITY A

- Custom tab in the SW ribbon alongside Features / Sketch / Surfaces
- Built via `ICommandManager.CreateCommandTab2`, `AddCommandGroup2`, `AddCommandItem2`
- **Why it matters:** permanent eyeline presence. User sees "Adze" tab whenever they open SW. Signals first-class integration to reviewers.
- **Button set:** Ask · Diagnose · Mates · Dimensions · Properties · Explain
- **Effort:** 3–4 hours
- **Target:** v0.1.2 (pre-4/20)

### 3. Feature-tree context menu — PRIORITY A

- Right-click a feature in the FeatureManager tree → "Ask Adze about this feature"
- Right-click in graphics area → "Diagnose this model"
- Built via `ICommandManager.AddCallback` or `FeatureManagerTree` event hooks
- **Why it matters:** discoverability + contextual prompting. Reduces friction from "open task pane, type question, mention feature name" to "right-click, ask."
- **Effort:** 2–3 hours
- **Target:** v0.1.2 (pre-4/20)

### 4. PropertyManager Page (PMP) — PRIORITY B

- SW's native pre-canvas modal/modeless panel for commands
- Built via `IPropertyManagerPage2`
- **Use case:** migrate the write confirmation cards from the Task Pane into PMPs so write operations feel like native SW commands (insert, suppress, rename dialogs).
- **Why it matters:** makes Adze's writes feel first-class. PMP is the canonical surface for any operation that affects the model.
- **Effort:** 1–2 days (each write tool needs its own PMP)
- **Target:** v0.2.0 (Phase 10 headline)

### 5. External modeless window — PRIORITY B

- Separate WinForms/WPF window outside the SW host process, docked to the side or floating
- **Use case:** long-form output that does not fit the Task Pane width — chat history, trace viewer, recipe manager, write history audit
- **Why it matters:** Task Pane is narrow. Some views (tables, traces, recipes) need more horizontal space.
- **Effort:** 1 day
- **Target:** v0.2.0 or v0.3.0

### 6. Feature tree decorations — PRIORITY C

- Icons/badges overlaid on features in the FeatureManager tree (warning glyphs on rebuild errors, lock icons on AI-suppressed, tag icons on recipe-touched)
- Built via `Feature.SetFaceColor` + custom glyph injection (limited API support)
- **Why it matters:** passive awareness — users see AI state at a glance without running a query.
- **Effort:** 2–3 days (API support is partial; may require workarounds)
- **Target:** v0.3.0+

### 7. Toast notifications — PRIORITY C

- Balloon popups from the Windows tray or the SW title bar on run completion, write apply, rebuild resolution
- Built via Windows Toast API or custom NotifyIcon
- **Why it matters:** lets users run async queries and get notified instead of polling the Task Pane
- **Effort:** half day
- **Target:** v0.1.2 stretch, or v0.2.0

### 8. Drawing annotation layer — PRIORITY D

- AI-generated dimension suggestions, BOM issue callouts, annotation placement in SW drawings
- Built via `IView.InsertNote`, `IDrawingDoc.InsertDimension`
- **Why it matters:** drawing workflows are painful; AI assistance has strong product-market fit here
- **Effort:** 2–3 weeks
- **Target:** v0.3.0 or later

### 9. In-canvas 3D overlays — PRIORITY E

- AI-generated reference geometry, measurement overlays, problem-area highlights drawn into the graphics area
- Built via `IModelDocExtension.CreateRenderable` or custom OpenGL hook
- **Why it matters:** visual AI feedback in the user's focus area
- **Effort:** 1 month+ (fragile, API surface is narrow)
- **Target:** vFuture

---

## Priority ranking (product value × effort)

| Rank | Surface | Effort | Version |
|------|---------|--------|---------|
| 1 | CommandManager ribbon tab | S | v0.1.2 |
| 2 | Feature-tree context menu | S | v0.1.2 |
| 3 | PropertyManager Page for writes | M | v0.2.0 |
| 4 | External modeless window | M | v0.2.0 |
| 5 | Toast notifications | XS | v0.1.2 stretch |
| 6 | Feature tree decorations | M | v0.3.0 |
| 7 | Drawing annotation layer | L | v0.3.0 |
| 8 | In-canvas 3D overlays | XL | vFuture |

---

## Design constraints

- **Feature-gate everything new.** Each new surface gets its own env var (`SOLIDWORKS_AI_RIBBON`, `SOLIDWORKS_AI_CONTEXT_MENU`, `SOLIDWORKS_AI_PMP_WRITES`). Default off for v0.1.2, progressively defaulted on as they stabilize.
- **COM threading stays strict.** All new UI event handlers must marshal to the UI thread via `IUiThreadInvoker` (already exists). No direct COM calls from non-UI threads.
- **Graceful fallback.** If ribbon registration fails, add-in still loads with Task Pane only. Never hard-fail the add-in because of a surface misregistration.
- **No surface duplicates the Task Pane.** Ribbon buttons, context menu items, and PMPs all route through the same prompt composer. Do not fork the intent parsing logic per surface.

---

## Non-goals for Phase 10

- No new tools. Phase 10 is UI expansion only.
- No new AI providers. Five is enough.
- No changes to the 8-step write lifecycle. Surface migrates; governance stays.
- No rewrite of the agent loop. `AgentLoopRunner` is the backend for every surface.

---

## Success criteria for v0.2.0 (Phase 10 headline release)

- Ribbon tab and context menu shipped, on by default
- At least one write tool (probably `set_dimension_value`) migrated to PropertyManager Page
- External modeless window available as a toggle
- Zero regressions in Task Pane UX
- All new surfaces feature-gated and documented in SETUP.md

---

## Appendix A: Implementation Briefs

*Compiled 2026-04-16 from an Explore-subagent pass over the Adze codebase and SW API patterns. API specifics marked with hedges ("if available; fallback to X") reflect honest uncertainty — verify against SW 2025+ docs at implementation time.*

### Surface 1: CommandManager Ribbon Tab (v0.1.2)

**Overview.** Register a custom ribbon tab ("Adze") alongside native SOLIDWORKS tabs. Buttons trigger the same `QuickAction` prompt composer already built into the Task Pane.

**Exact SW API calls**
- `ISldWorks.GetCommandManager() → ICommandManager`
- `ICommandManager.CreateCommandTab2(int tabIndex, string tabName) → ICommandTab`
- `ICommandTab.AddCommandGroup2(int groupPosition, string groupName, string toolTip, int iconIndex, bool hasDropdown) → ICommandGroup`
- `ICommandGroup.AddCommandItem2(string itemName, int itemPosition, string hint, string tooltip, int imageIndex, string callbackMethod, int menuItemType) → bool`
- `ICommandGroup.UpdateGroupImage(int normalIconList, int hotIconList, string imagePath) → bool`
- Button callback routed through `ISwAddin._callback` (already set via `SetAddinCallbackInfo2`).

**Lifecycle integration**
- `ConnectToSW`: after `CreateTaskPane()` add `RegisterCommandManagerTab(ref ribbonTabHandle)`. Store `_commandTab` and `_commandGroups` as instance fields.
- `DisconnectFromSW`: before `DestroyTaskPane()`, call `UnregisterCommandManagerTab()` and `Marshal.FinalReleaseComObject(_commandTab)`.

**Threading model.** Ribbon clicks arrive on the SW UI thread (STA). Callback invokes `TaskPaneControl.QuickAction()` which already marshals via `IUiThreadInvoker`. No extra threading needed.

**Known gotchas**
1. **COM ref-counting.** Each button icon image list must be released with `Marshal.FinalReleaseComObject()`.
2. **Tab ID stability.** Re-calling `CreateCommandTab2` on the same tab index fails. Cache and reuse.
3. **Icon resources.** Icons as embedded PNGs in assembly or on disk. Share `_ribbonIconPath` across groups.
4. **Multi-document.** Ribbon is global (one per SW instance), not per-document. No teardown on document close.
5. **Registration conflicts.** If a prior add-in version crashed, registry entries may persist. Wrap creation in try/catch and fail gracefully.

**Effort.** 2–3 hours.

**Feature gate.** `SOLIDWORKS_AI_RIBBON=true` (default: false for v0.1.2, on for v0.2.0+).

**Failure modes.** If `ICommandManager` null or tab creation fails, log silently and continue with Task Pane only. Never hard-fail `ConnectToSW`.

---

### Surface 2: Feature-Tree Context Menu (v0.1.2)

**Overview.** Inject "Ask Adze about this feature" and "Diagnose this model" into the right-click menu on features.

**Exact SW API calls**
- `ICommandManager.AddCallback(int callbackInt, string callbackName) → int` (register callback ID)
- Document-level right-click events via `IModelDocExtension` (exact listener interface varies by SW version; fall back to polling selection changes if direct subscription not exposed).
- `IModelDoc2.FeatureManager.GetFeatureByName()` for feature-name resolution fallback.

**Lifecycle integration**
- `AttachActiveDocumentEvents()`: after selection-change listeners, register context menu callbacks with `RegisterContextMenuCallbacks(_application, model)`.
- `DetachActiveDocumentEvents()`: `UnregisterContextMenuCallbacks()`.
- Store callback IDs and handler refs as instance fields.

**Threading model.** Right-click events on UI thread. Extract feature name and construct prompt in-thread. Dispatch to `TaskPaneControl.QuickAction()` through existing `IUiThreadInvoker` pipeline.

**Known gotchas**
1. **Feature name resolution.** Tree right-clicks don't always expose the clicked feature directly. Fall back to last-selected feature name.
2. **Graphics-area right-clicks.** Require geometry hit-testing. Scope to feature-tree-only for v0.1.2.
3. **Event handler cleanup.** Context menu listeners leak if not explicitly unsubscribed. Guard with try/finally.
4. **Menu item persistence.** Rebuild per document to avoid stale references.
5. **Callback IDs.** Each item needs a unique ID. Cache these and unregister all on detach.

**Effort.** 2–2.5 hours.

**Feature gate.** `SOLIDWORKS_AI_CONTEXT_MENU=true`.

**Failure modes.** If registration fails, skip silently — Task Pane remains functional. If feature name unresolvable, fall through to generic "Diagnose this model."

---

### Surface 3: PropertyManager Page for Write Confirmations (v0.2.0)

**Overview.** Migrate write confirmation cards from Task Pane HTML into native SW PropertyManager Pages. When the agent proposes a write, show a native PMP modal instead of an HTML card.

**Exact SW API calls**
- `ISwDocument.CreatePropertyManagerPage(ref PropertyManagerPageHandler handler) → IPropertyManagerPage2`
- `IPropertyManagerPage2.SetProgrammaticTitle(string title)`
- `IPropertyManagerPage2.AddControl(swPropertyManagerPageControlType_e, swPropertyManagerPageOptions_e, string) → IPropertyManagerPageControl`
- `IPropertyManagerPageControl.SetPosition(int top, int left, int width, int height)`
- Handler callbacks: `IPropertyManagerPageHandler.OnButtonPressed(int id)`, `OnClosed(swPropertyManagerPageCloseReasons_e)`.

**Lifecycle integration.** `HostState.ApplyPendingWrite()`: before `ApplyWriteToolDirect()`, check PMP gate and call `ShowWriteConfirmationPMP(action)`. Add `PropertyManagerPageBroker` class to `Adze.Host.UI`. Store PMP instance in `HostState._currentWritePMP` and clean up on close/apply.

**Threading model.** PMP is modal — blocks the calling thread. Called from the WebBrowser bridge, which is UI-thread-bound, so no extra marshaling. If invoked from agent loop (background thread), marshal to UI thread first via `IUiThreadInvoker.Invoke()`.

**Known gotchas**
1. **Modal blocking.** PMP blocks the current thread. Marshal to UI thread if from background.
2. **Resource cleanup.** PMPs hold COM refs. Call `ClosePage()` in finally block.
3. **Event handler lifetime.** Handler must stay alive for the page's duration. Store as instance field, not local.
4. **Value marshaling.** Setting control values dynamically can fail. Cast to expected types (string/bool/int/double).
5. **Configuration-scoped writes.** Display config name in PMP title or read-only text box.

**Effort.** 1–1.5 days per write tool. Start with `set_dimension_value` (single numeric input).

**Feature gate.** `SOLIDWORKS_AI_PMP_WRITES=true`.

**Failure modes.** If PMP creation fails, fall back to Task Pane card UI. If user cancels, mark write `Cancelled` and re-render. If `ClosePage()` throws, log and continue.

---

### Surface 4: External Modeless Window for Long-Form Output (v0.2.0)

**Overview.** Separate WinForms window docked to the SW window or floating. Wide-format content: chat history table, trace viewer, recipe manager, write history audit log.

**Exact SW API calls**
- `ISldWorks.GetHWnd() → IntPtr` (SW window handle for parenting).
- Win32 P/Invoke: `SetParent(childHwnd, parentHwnd)`, `SetWindowLong(hwnd, GWL_STYLE, styles)`.
- No SW COM API directly needed.

**Lifecycle integration.** `ConnectToSW`: create window hidden (`_externalWindow = new AdzeHistoryWindow(); _externalWindow.Hide();`). Task Pane adds toggle button (`window.external.ShowHistoryWindow()`). `DisconnectFromSW`: `Close()` + `Dispose()`.

**Threading model.** Window on SW UI thread (STA) so it can be parented. Data updates from `HostState` via thread-safe getters (already locked). Use `Control.BeginInvoke()` for background-originated updates.

**Known gotchas**
1. **Window parenting.** Optional. `WS_CHILD` style can break standalone behavior — use carefully.
2. **Data refresh.** No auto-update on new chat entries. Poll `HostState.GetChatHistory()` on a timer, or publish change events.
3. **Multi-instance.** Singleton per add-in instance. Store in `HostState` or `AdzeAddIn`.
4. **Docking position.** Persist window position/size in `%APPDATA%\Adze\`.
5. **Tab management.** Separate tab selection state if multi-tab (Chat/Trace/Recipes).

**Effort.** 1 day.

**Feature gate.** `SOLIDWORKS_AI_HISTORY_WINDOW=true`.

**Failure modes.** If parenting fails, show floating. If data refresh fails, show reload button. If window crashes, log and suppress — add-in continues.

---

### Surface 5: Feature-Tree Decorations (v0.3.0)

**Overview.** Overlay icons/badges/colors on features in FeatureManager tree to indicate AI state. Direct tree-node glyph injection is not exposed — use `SetFaceColor` tinting as the practical surrogate.

**Exact SW API calls**
- `IFeature.SetFaceColor(long color, long transparency)` — colors faces (not tree node). Closest available surface.
- Fallback: `IModelDocExtension.InsertNote()` for text/glyph annotation at face location.
- Rebuild-event hook: `FeatureManager.RebuildEvent` or `ModelDoc2.RegenNotify` to restore decorations after rebuild.

**Lifecycle integration.** `HostState.RunAgenticAssistant()`: post-completion, if gate enabled, call `ApplyFeatureTreeDecorations(writeTracker.CapturedWrites)`. New static `FeatureTreeDecorator` manages state. Map of feature names → decoration in `HostState` for cleanup on document close.

**Threading model.** UI thread (COM calls). Invoke via `IUiThreadInvoker` if from agent loop thread.

**Known gotchas**
1. **API limitation.** No direct tree-node rendering API. `SetFaceColor` colors geometry faces, not the tree icon itself.
2. **Face selection ambiguity.** Features may have many faces. Color first face or iterate and rebuild.
3. **Persistence.** Colors lost on rebuild unless reapplied. Hook `EditRebuild3` event.
4. **Color conflicts.** User-colored faces get overridden. Provide "clear decorations" button.
5. **Performance.** Decorating 100+ features can stall UI. Batch or async.

**Effort.** 2–3 days.

**Feature gate.** `SOLIDWORKS_AI_FEATURE_DECORATIONS=true`.

**Failure modes.** Per-feature failures logged and skipped. Cap at 20 most-recent decorations. On document close, clear all.

---

### Summary table

| Surface | Key API | Lifecycle hook | Threading | Effort | Gate | Top risk |
|---------|---------|-----------------|-----------|--------|------|----------|
| Ribbon Tab | `CreateCommandTab2`, `AddCommandItem2` | `ConnectToSW` / `DisconnectFromSW` | UI | 2–3 h | `_RIBBON` | COM ref-count, icon paths |
| Context Menu | `AddCallback`, doc events | `AttachActiveDocumentEvents` / `Detach` | UI | 2–2.5 h | `_CONTEXT_MENU` | Feature resolution, event leaks |
| PMP Writes | `CreatePropertyManagerPage`, `AddControl` | `ApplyPendingWrite()` gate check | UI (marshal) | 1–1.5 d/tool | `_PMP_WRITES` | Modal blocking, handler lifetime |
| Modeless Window | Win32 `SetParent`, WinForms | `ConnectToSW` / `DisconnectFromSW` | UI (polling OK) | 1 d | `_HISTORY_WINDOW` | Parenting, data sync |
| Decorations | `SetFaceColor`, rebuild events | Post-`RunAgenticAssistant` | UI (invoker) | 2–3 d | `_FEATURE_DECORATIONS` | API limits, rebuild invalidation, perf |

Total phase-10 effort estimate: ~6–9 person-days of focused work for all five surfaces, spread across v0.1.2 → v0.3.0.
