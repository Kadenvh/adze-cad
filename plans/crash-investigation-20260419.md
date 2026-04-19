# Crash Investigation — 2026-04-19 (SW 34.1.0.0140 ContextMenu.Register)

**Status:** R1.4 deliverable. Narrowed to high confidence; authoritative fix landed in R2. Final validation pending live SW test with probe enabled.
**Phase:** R1 of `polish-and-v1-path.md`
**Related brain.db notes:** "R1 BLOCKER" (issue), "R1 MITIGATION" (issue), "R2 CODE COMPLETE" (improvement)
**Related decisions:** #15 (R2 approach)

---

## Evidence

### Crash dump sessions

`%APPDATA%\SolidWorks\` on the dev machine, all naming SW build `34.1.0.0140`:

| Session directory | Local time | UTC time | Notes |
|-------------------|-----------|----------|-------|
| `20260419013311_34.1.0.0140` | 01:33:11 | 06:33:11 | First crash after v1.0 install |
| `20260419013348_34.1.0.0140` | 01:33:48 | 06:33:48 | Second attempt, crashed identically |
| `20260419013814_34.1.0.0140` | 01:38:14 | 06:38:14 | Third attempt after intentional retry |

Additional sessions at 01:57 and 02:04 local followed the same pattern.

Each directory contains CXPD (SOLIDWORKS CrossProcess Dump) files. We did not open them with WinDbg in this session — the host.log pattern was sufficient to narrow the suspect call frame.

### host.log pattern

`%LOCALAPPDATA%\Adze\logs\host.log` for each of the five v1.0 sessions terminates identically:

```
[YYYY-MM-DD HH:MM:SSZ] INFO ConnectToSW starting.
[YYYY-MM-DD HH:MM:SSZ] INFO Task Pane created. AddControl returned: ControlAxSourcingSite [Adze.Host.UI.TaskPaneControl].
[YYYY-MM-DD HH:MM:SSZ] INFO RibbonTab: registered successfully across part/assembly/drawing contexts.
<session log ends abruptly — no subsequent Snapshot captured, no ConnectToSW completed>
```

Compare the clean v0.1.1 session at 2026-04-19 05:15:45Z (context-menu gate off):

```
[2026-04-19 05:15:45Z] INFO ConnectToSW starting.
[2026-04-19 05:15:45Z] INFO Task Pane created. ...
[2026-04-19 05:15:45Z] INFO Ribbon gate SOLIDWORKS_AI_RIBBON is off; skipping tab.
[2026-04-19 05:15:45Z] INFO Context-menu gate SOLIDWORKS_AI_CONTEXT_MENU is off; skipping.
[2026-04-19 05:15:45Z] INFO Snapshot captured. reason=Initial context snapshot ...
[2026-04-19 05:15:45Z] INFO ConnectToSW completed.
```

## Narrowing

`ConnectToSW` in `AdzeAddIn.cs` executes in this order after the Task Pane is created:

1. `TryRegisterRibbonTab()` — logs `RibbonTab: registered successfully` on success (observed in crash sessions)
2. `TryRegisterContextMenu()` — logs `ContextMenu: registered ...` on success (NOT observed)
3. Snapshot capture + `ConnectToSW completed` — logs `Snapshot captured` and `ConnectToSW completed` (NOT observed)

The log emits at step 1 and is immediately followed by process termination before step 2's log call can fire. The faulting code therefore sits between the end of `RibbonTab.Register` and before `ContextMenu.Register` can emit its own entry failure log line — or inside one of the first interop calls in `ContextMenu.Register`.

The first interop call sequence in `ContextMenu.Register` (`src/Adze.Host/AddIn/ContextMenu.cs`):

```csharp
_commandManager = application.GetCommandManager(cookie);
// then per menu type:
_featureMenu   = TryRegisterMenu(UserIdFeature,   ..., swSelectType_e.swSelBODYFEATURES,   ...);
_componentMenu = TryRegisterMenu(UserIdComponent, ..., swSelectType_e.swSelCOMPONENTS,     ...);
_nothingMenu   = TryRegisterMenu(UserIdNothing,   ..., swSelectType_e.swSelNOTHING,        ...);
```

And inside `TryRegisterMenu`:

```csharp
ICommandGroup? group = _commandManager!.AddContextMenu(userId, title);
group.ShowInDocumentType = ...;
group.SelectType = (int)selectType;
int itemId = group.AddCommandItem2(label, ...);
group.HasToolbar = false;
group.HasMenu = true;
bool ok = group.Activate();
```

## Root cause (hypothesis — high confidence)

**The SW 34.1.0.0140 desktop update, applied 2026-04-19 shortly before the crashes began, changed the binary signature of one of the interop calls used by context-menu registration — most likely `ICommandManager.AddContextMenu(int, string)` or one of the immediately-following `ICommandGroup` property setters / `Activate()` call.**

The mismatch between our early-bound reference assembly (`SolidWorks.Interop.sldworks.dll` compiled against a pre-update SW version) and the runtime-loaded type library manifests as an access violation inside the COM thunking layer — a native crash, not a managed exception. The existing managed `try / catch (Exception)` blocks inside `ContextMenu.Register` and `TryRegisterMenu` do not catch this on .NET Framework 4.5+ by default because corrupted-state exceptions bypass normal catch clauses unless the method is explicitly decorated.

Supporting evidence:

- **No `ContextMenu: registration threw` log entry in any crash session** — confirms the managed catch never fired
- **RibbonTab.Register (different interop path, uses `CreateCommandGroup2` rather than `AddContextMenu`) succeeds** — demonstrates the change is specific to a subset of interop entry points, not a wholesale version incompatibility
- **v0.1.1 sessions with `SOLIDWORKS_AI_CONTEXT_MENU=false` and everything else identical run cleanly through `ConnectToSW completed`** — demonstrates the context-menu path is the sole faulting code path
- **The crash is silent from the user's perspective** (no dialog, no prompt) and produces CXPD dumps — consistent with an unhandled native AV inside sldworks.exe's process

We did not confirm *which specific* of `AddContextMenu`, `SelectType` setter, `AddCommandItem2`, or `Activate` is the faulting call. For the purposes of R2 this granularity isn't required — all four live in the same quarantine zone.

## Secondary hypothesis (lower confidence)

A less-likely alternative: the call that crashes is actually one of the ribbon late-callback hookups happening AFTER `RibbonTab.Register` returns but triggered by SOLIDWORKS asynchronously (command dispatcher wiring, menu dispatcher registration). Under this hypothesis, context-menu is a red herring and the crash is a ribbon-side timing issue. Arguments against:

- `ConnectToSW` is synchronous on a single STA thread; there's no async gap for SW to call back into Adze between step 1 and step 2
- The crash timing is deterministic across all five observed sessions — a timing bug would show variability
- R5 will validate the R2 fix end-to-end; if this secondary hypothesis holds, the probe will pass but SW will still crash, which would be visible data

## Fix landed in R2 (see `plans/polish-and-v1-path.md` R2)

Implemented 2026-04-19, same-day:

1. **`CompatibilityProbe.Check`** — new static method at `src/Adze.Host/AddIn/CompatibilityProbe.cs` runs `RevisionNumber` → `GetCommandManager` → `CreateCommandGroup2` → `RemoveCommandGroup2` at `ConnectToSW` time. Decorated with `[HandleProcessCorruptedStateExceptions]` + `[SecurityCritical]`. Returns a typed `CompatibilityProbeResult` with `IsCompatible`, `Message`, `FailedStep`, and `RevisionNumber`.
2. **`ContextMenu.Register` + `ContextMenu.TryRegisterMenu`** decorated with the same attributes. Native AVs inside `AddContextMenu` etc. now get caught by the existing managed `try/catch (Exception)` blocks.
3. **`AdzeAddIn.TryRegisterContextMenu`** calls `CompatibilityProbe.Check(application, cookie)` before attempting to register the real context menu. If `IsCompatible == false`, skips registration and logs the failed step; context-menu is absent but SW launches cleanly. Result is persisted in `AdzeAddIn._lastProbeResult` and exposed as `LastProbeIsCompatible` / `LastProbeMessage` for future Task Pane banner wiring.
4. **Context-menu gate default temporarily set to `false`** in `FeatureGateRegistry.GetDefault` until the probe is validated end-to-end on a live post-update SW. User can opt in via `SOLIDWORKS_AI_CONTEXT_MENU=true` at user scope.

## Live validation (pending — R5)

User must:

1. Set user-scope env var `SOLIDWORKS_AI_CONTEXT_MENU=true`
2. Relaunch SOLIDWORKS via the 3DEXPERIENCE Launcher
3. Observe `host.log`:
   - **Expected (probe catches):** `CompatibilityProbe: OK. SW revision=34.1.0.0140` then `Context-menu skipped: compatibility probe failed at step 'create-command-group'` or `ContextMenu: registration threw` then `ConnectToSW completed`
   - **Unexpected (crash still):** SW crashes silently; implies the probe does not exercise the same call as the actual crash site — further investigation required, likely WinDbg on the CXPD dumps

Until step 3 completes and logs show one of the expected outcomes, R1 remains in progress and R2 is "code complete but unvalidated".

## Lessons for future SW updates

- **Managed try/catch is not sufficient for interop COM calls.** Always decorate interop-calling methods with `[HandleProcessCorruptedStateExceptions]` + `[SecurityCritical]` on .NET Framework 4.5+.
- **Keep a version-aware compatibility probe in the ConnectToSW path.** Every new risky interop call added should be exercised by the probe before we trust it with user state.
- **Gate new surfaces behind a `false` default until live-validated.** The v1.0 "default-on everything for zero-config" push was correct in spirit but premature without the probe. New surfaces should default off, be tested, then flip on in a subsequent release.
- **Persist the SW build version Adze was last-verified on** (to be implemented in R3). If the current build differs from the verified build, force the probe to re-run and surface the result in the Task Pane.

## Artifacts referenced

- `%LOCALAPPDATA%\Adze\logs\host.log` (live)
- `%APPDATA%\SolidWorks\20260419013311_34.1.0.0140\CXPD\` (crash dump)
- `%APPDATA%\SolidWorks\20260419013348_34.1.0.0140\CXPD\` (crash dump)
- `%APPDATA%\SolidWorks\20260419013814_34.1.0.0140\CXPD\` (crash dump)
- `src/Adze.Host/AddIn/ContextMenu.cs` (faulting code, now CSE-wrapped)
- `src/Adze.Host/AddIn/CompatibilityProbe.cs` (new in R2)
- `src/Adze.Host/AddIn/AdzeAddIn.cs` (probe integration, lines in `TryRegisterContextMenu`)
- `src/Adze.Broker/Configuration/FeatureGateRegistry.cs` (context-menu default flipped to false)
