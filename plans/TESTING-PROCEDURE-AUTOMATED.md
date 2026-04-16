# Automated Testing Procedure — Adze v0.1.2

**Target:** Validate v0.1.2 release on 2026-04-20 before shipping to SOLIDWORKS Solution Partner Program.
**Executor:** Autonomous agent with `mcp__windows__*` MCP toolset available (no manual intervention assumed).
**Execution time:** 60–90 minutes.
**Success criterion:** Pass/Fail report written to `C:\adze-cad\solidworks-partner\screenshots\TEST-REPORT-2026-04-20.md`.

---

## Section 1: Preconditions

### 1.1 Environment verification

Before starting, verify the following prerequisites. If any fail, STOP and report the failure.

1. SOLIDWORKS is not currently running.
   - Execute: `Get-Process sldworks -ErrorAction SilentlyContinue`
   - Expected: Returns nothing (process not found).
   - If found: Run `Stop-Process -Name sldworks -Force` and wait 5 seconds.

2. Adze is installed and registered.
   - Expected location: `$env:LOCALAPPDATA\Adze\bin\` contains DLL files.
   - Run the installer if not present:
     ```powershell
     powershell.exe -NoProfile -File C:\adze-cad\install\install-adze.ps1
     ```

3. The sample assembly exists at `C:\SOLIDWORKS\spiderbot\SpdrBot v14.SLDASM`.
   - Execute: `Test-Path 'C:\SOLIDWORKS\spiderbot\SpdrBot v14.SLDASM'`
   - If not found: STOP and report "Sample assembly missing."

4. Screenshots directory exists and is writable.
   - Directory: `C:\adze-cad\solidworks-partner\screenshots\`
   - If missing: Create it.
   - Execute: `New-Item -ItemType Directory -Path 'C:\adze-cad\solidworks-partner\screenshots\' -Force`

### 1.2 Feature gate initialization

All new gates for v0.1.2 are enabled individually for their specific tests. Set environment variables in the current PowerShell session BEFORE launching SOLIDWORKS. The add-in reads these at ConnectToSW time.

Default state: All feature gates are OFF unless the test explicitly sets them ON.

```powershell
# For ribbon tests (Section 3.C, 3.D, 3.E, 3.F)
$env:SOLIDWORKS_AI_RIBBON='true'

# For context menu tests (Section 3.G, 3.H, 3.I)
$env:SOLIDWORKS_AI_CONTEXT_MENU='true'

# For toast notification test (Section 3.J)
$env:SOLIDWORKS_AI_TOAST='true'

# For PMP proof-of-path test (Section 3.K) — last, most experimental
# $env:SOLIDWORKS_AI_PMP_WRITES='true'  # Enable only if testing PMP
```

### 1.3 Model configuration (optional)

If testing with a model provider (Anthropic, OpenAI, or local Ollama/LM Studio):

```powershell
$env:SOLIDWORKS_AI_ENABLE_MODEL='true'
$env:SOLIDWORKS_AI_PROVIDER='anthropic'  # or 'openai', 'ollama', 'lmstudio'
$env:SOLIDWORKS_AI_ANTHROPIC_API_KEY='sk-ant-...'  # Set if using Anthropic
```

Without model configuration, Adze uses the deterministic broker — all tests are still valid.

### 1.4 Log directory setup

```powershell
New-Item -ItemType Directory -Path "$env:LOCALAPPDATA\Adze\logs" -Force | Out-Null
```

---

## Section 2: Launch Sequence

### 2.1 SOLIDWORKS launch

```powershell
Start-Process 'C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\sldworks.exe' -ArgumentList '"C:\SOLIDWORKS\spiderbot\SpdrBot v14.SLDASM"'
```

### 2.2 Wait for startup

Wait 45–90 seconds for SOLIDWORKS to fully load and for Adze to initialize.

```powershell
Start-Sleep -Seconds 60
```

### 2.3 3DX login prompt detection and handling

**Critical:** On some machines, SOLIDWORKS launch is blocked by a 3DEXPERIENCE Platform login or update window. This window appears BEFORE SOLIDWORKS loads the add-in.

Execute a screenshot to check for the blocker: `mcp__windows__Snapshot`.

**Decision tree:**

- **If a window titled "3DEXPERIENCE ID", "3DEXPERIENCE Platform", or "3DEXPERIENCE Update" appears:**
  - Report: "3DX login blocker detected. Cannot proceed."
  - Do NOT click Yes, No, OK, or any button.
  - STOP. The test cannot continue. Document this as a blocker in the Pass/Fail report.

- **If no login/update window is visible:**
  - Proceed to 2.4.

### 2.4 Main window confirmation

Take a second screenshot to confirm SOLIDWORKS is loaded with SpdrBot visible:

Expected state:
- SOLIDWORKS main window with "SpdrBot v14" or similar in the title bar.
- FeatureManager tree visible on the left (showing assembly structure).
- 3D graphics area visible in the center.
- No login or update prompts visible.

If the main window is not visible after 90 seconds, STOP and report "SOLIDWORKS failed to load within timeout."

---

## Section 3: Functional Tests

Run tests in order. Each test has a **name**, **setup**, **action**, **screenshot**, and **pass/fail criterion**. Coordinate strategy: prefer label-based `mcp__windows__Click` via `mcp__windows__Snapshot` when possible; fall back to pixel coordinates when necessary.

### Test A: Adze Task Pane Appears

**Setup:** SOLIDWORKS has launched with SpdrBot v14 loaded and no gate flags are set (or only non-UI gates are set).

**Action:** Wait 2 seconds. Take a screenshot.

**Expected:** The Adze Task Pane sidebar is visible on the right side of SOLIDWORKS with:
- Navy header "Adze — SOLIDWORKS AI"
- Prompt input box labeled "Ask about the active document..."
- "Run assistant" button
- Collapsible sections below (clarification, plan, tools log, status)

**Screenshot:** Save as `partner-00-task-pane-empty.png`

**Pass criterion:** Task Pane is visible with correct header and input box.

**Fail criterion:** Task Pane does not appear, or header is missing/malformed.

---

### Test B: Baseline In-Pane Toolbar (Existing UX)

**Setup:** Task Pane is visible (from Test A). The four baseline quick-action buttons should be visible: Diagnose, Mates, Dimensions, Properties.

**Action:** Locate and click the "Diagnose" button in the in-pane toolbar (gray button with label "Diagnose" below the prompt box).

**Tool invocation:**
```
mcp__windows__Snapshot (identify button location via label)
mcp__windows__Click (click the Diagnose button)
Start-Sleep -Seconds 2
mcp__windows__Snapshot (capture tool-chip animations firing)
```

**Expected:**
- After clicking, the run state label shows "Calling..." or similar.
- Colored tool chips appear in sequence (blue for read tools).
- After ~10–30 seconds, an answer appears in the Task Pane content area.
- Answer text is grounded in the SpdrBot assembly (references mates, features, rebuild status, etc.).

**Screenshot:** Save mid-run as `partner-01b-tool-chips.png` showing at least one tool chip firing.

**Pass criterion:** Run executes, tool chips animate, and a grounded answer appears.

**Fail criterion:** Run does not start, no tool chips appear, or answer is generic/empty.

---

### Test C: Ribbon Tab Presence

**Setup:** SOLIDWORKS main window is in focus. Feature gate `SOLIDWORKS_AI_RIBBON=true` was set BEFORE launch.

**Action:** Take a screenshot of the SOLIDWORKS ribbon.

**Expected:** The "Adze" tab is visible in the ribbon (typically between existing tabs like Standard, Sketch, and Features).

**Screenshot:** Save as `partner-02-ribbon-tab.png` showing the full ribbon with Adze tab visible.

**Pass criterion:** "Adze" tab is visible in the ribbon.

**Fail criterion:** No "Adze" tab appears, or it is grayed out.

---

### Test D: Ribbon Tab — Six Buttons

**Setup:** The "Adze" tab is visible (Test C).

**Action:** Click the "Adze" tab to activate it.

**Tool invocation:**
```
mcp__windows__Snapshot (identify Adze tab)
mcp__windows__Click (click Adze tab)
Start-Sleep -Seconds 1
mcp__windows__Screenshot (capture ribbon buttons)
```

**Expected:** Six buttons appear in the ribbon: Ask, Diagnose, Mates, Dimensions, Properties, Explain. Each button is labeled and has a tooltip that appears on hover.

**Screenshot:** Save as `partner-02b-ribbon-buttons.png` showing all six buttons.

**Pass criterion:** All six buttons are visible and labeled correctly.

**Fail criterion:** Fewer than six buttons appear, or buttons are not labeled.

---

### Test E: Ribbon Ask Button

**Setup:** Adze ribbon tab is active (Test D).

**Action:** Click the "Ask" button.

**Expected:** The Task Pane gains focus, and the prompt input box is focused (text cursor visible in the input box). No run fires; the user is expected to type and then click Run.

**Pass criterion:** Prompt input box is focused and ready for user input.

**Fail criterion:** No focus change, or a run fires automatically (should not happen).

---

### Test F: Ribbon Diagnose Button

**Setup:** Adze ribbon tab is active, SpdrBot is still the active document.

**Action:** Click the "Diagnose" button on the ribbon.

**Expected:** A run fires immediately with a pre-built diagnostic prompt. The run state label shows "Calling get_rebuild_diagnostics..." or similar. Tool chips fire. An answer appears.

**Screenshot:** Save as `partner-03-ribbon-diagnose.png`.

**Pass criterion:** Clicking Diagnose fires a run with the diagnostic intent.

**Fail criterion:** No run starts, or a different prompt is used.

---

### Test G: Ribbon Mates Button

**Setup:** Adze ribbon tab is active. SpdrBot has a known assembly with mates.

**Action:** Click the "Mates" button on the ribbon. Wait for run to complete (~15 seconds).

**Expected:**
- A run fires with a pre-built mates prompt.
- Tool chip for `get_mates` fires (blue chip labeled "get_mates").
- Answer lists all mates in SpdrBot, **including subassembly mates** (the 873477e fix ensures that mates inside subassemblies are included, not just top-level mates).
- Answer includes mate count, component names, and mate status.

**Screenshot:** Save as `partner-03b-ribbon-mates.png` showing the mates answer.

**Pass criterion:** Run completes, `get_mates` chip fires, and subassembly mates are returned (not empty).

**Fail criterion:** No mates returned, or subassembly mates are missing.

---

### Test H: Ribbon Dimensions Button

**Setup:** Adze ribbon tab is active.

**Action:** Click the "Dimensions" button on the ribbon.

**Expected:** A run fires with a pre-built dimensions prompt. `get_dimensions` tool chip fires. Answer lists key dimensions with current values.

**Pass criterion:** Run completes and dimensions are listed.

**Fail criterion:** No dimensions returned, or answer is empty.

---

### Test I: Context Menu — Feature

**Setup:** Feature gate `SOLIDWORKS_AI_CONTEXT_MENU=true` was set BEFORE launch. SOLIDWORKS main window is in focus with SpdrBot loaded.

**Action:** In the FeatureManager tree on the left, right-click any visible feature (e.g., "Boss", "Cut", "Fillet", etc.).

**Expected:** A context menu appears with the item "Ask Adze about this feature".

**Screenshot:** Save as `partner-04-context-menu-feature.png` showing the context menu with the Adze item visible.

**Pass criterion:** Context menu item is visible on right-click.

**Fail criterion:** Context menu does not appear, or Adze item is missing.

---

### Test J: Context Menu — Feature Click and Run

**Setup:** Context menu is open with "Ask Adze about this feature" item visible (from Test I).

**Action:** Click the "Ask Adze about this feature" menu item.

**Expected:** A run fires immediately. The prompt automatically includes the selected feature's name and context. Tool chips fire. An answer appears explaining the feature.

**Pass criterion:** Run fires and answer is grounded in the selected feature.

**Fail criterion:** No run starts, or answer does not reference the feature.

---

### Test K: Context Menu — Component

**Setup:** Feature gate `SOLIDWORKS_AI_CONTEXT_MENU=true`. SOLIDWORKS is open with SpdrBot (an assembly) active.

**Action:** In the FeatureManager tree, expand the assembly to show components. Right-click on any component.

**Expected:** A context menu item "Ask Adze about this component" appears.

**Screenshot:** Save as `partner-04b-context-menu-component.png`.

**Pass criterion:** Component context menu item is visible.

**Fail criterion:** Item is missing or context menu does not appear.

---

### Test L: Context Menu — Empty Canvas

**Setup:** Feature gate `SOLIDWORKS_AI_CONTEXT_MENU=true`.

**Action:** Right-click in an empty area of the 3D graphics view (not on any feature or component).

**Expected:** A context menu item "Diagnose this model" appears.

**Screenshot:** Save as `partner-04c-context-menu-diagnose.png`.

**Pass criterion:** Diagnose context menu item is visible on right-click in empty area.

**Fail criterion:** Item is missing.

---

### Test M: Toast Notification

**Setup:** Feature gate `SOLIDWORKS_AI_TOAST=true` was set BEFORE launch.

**Action:**
1. Click the "Run assistant" button or a ribbon button to start a run.
2. Immediately switch away from SOLIDWORKS (Alt+Tab to another window).
3. Wait for the run to complete (15–30 seconds).
4. Check the system tray (bottom-right of screen) for a balloon popup notification.

**Expected:** A tray balloon notification appears with title "Adze" or similar, and message about run completion (e.g., "Assistant run completed").

**Screenshot:** Save as `partner-05-toast-notification.png` (if visible) or note "Not visible in screenshot but logged as expected."

**Pass criterion:** Notification appears while SOLIDWORKS is not foreground.

**Fail criterion:** No notification appears, or notification appears when SOLIDWORKS is foreground (should be suppressed).

---

### Test N: PMP Write Confirmation (Proof-of-Path)

**Setup:** Feature gate `SOLIDWORKS_AI_PMP_WRITES=true` was set BEFORE launch.

**Note:** The PMP write feature is marked as "proof-of-path" in v0.1.2. Only `set_dimension_value` has PMP support. Other write tools still use HTML cards. If this test blocks the release, it is acceptable to note it as "deferred to v0.2.0" per CHANGELOG.md line 17.

**Action:**
1. Send a prompt to Adze asking it to change a dimension. Example: "Change the length of the main extrusion to 10 mm."
2. The agent should call `set_dimension_value` as a write tool.
3. Instead of an HTML confirmation card in the Task Pane, a native SOLIDWORKS PropertyManager Page (PMP) should appear.

**Expected:** A native SOLIDWORKS PropertyManager Page modal appears instead of an HTML card. The modal has:
- A title bar (e.g., "Confirm: set_dimension_value").
- Labels showing Summary, Undo label, New value, Dimension.
- OK and Cancel buttons (not Apply/Cancel like the HTML card).

**Screenshot:** Save as `partner-06-pmp-write-card.png`.

**Pass criterion:** PMP modal appears with OK/Cancel buttons.

**Fail criterion:** HTML card appears instead of PMP, or no confirmation UI appears.

**Note on failure:** If PMP does not work, this is acceptable for v0.1.2 as a proof-of-path feature. Document it as "Blocked: PMP support not initialized" and continue. All other v0.1.2 features (ribbon, context menu, toast) must pass.

---

## Section 4: Regression Tests

### Test O: Disable All New Gates and Verify Baseline UX

**Setup:** SOLIDWORKS is running. Close it and clear all feature gates.

**Action:**
1. Close SOLIDWORKS.
2. Clear environment variables:
   ```powershell
   Remove-Item env:SOLIDWORKS_AI_RIBBON -ErrorAction SilentlyContinue
   Remove-Item env:SOLIDWORKS_AI_CONTEXT_MENU -ErrorAction SilentlyContinue
   Remove-Item env:SOLIDWORKS_AI_TOAST -ErrorAction SilentlyContinue
   Remove-Item env:SOLIDWORKS_AI_PMP_WRITES -ErrorAction SilentlyContinue
   ```
3. Launch SOLIDWORKS again with SpdrBot.
4. Wait 60 seconds for full load.
5. Take a screenshot.

**Expected:**
- Task Pane appears (baseline always-on feature).
- NO ribbon tab (gate is off).
- NO context menu items (gate is off).
- In-pane toolbar (Diagnose, Mates, Dimensions, Properties) is still present and functional.
- Everything looks exactly as it did before v0.1.2 (no regressions).

**Screenshot:** Save as `partner-07-regression-baseline.png`.

**Pass criterion:** Baseline UX unchanged. Existing buttons work. No new surfaces are visible.

**Fail criterion:** Baseline functionality is broken or missing.

---

### Test P: Baseline Diagnose Run

**Setup:** All gates disabled. Task Pane is visible. In-pane toolbar is visible.

**Action:** Click the baseline "Diagnose" button in the in-pane toolbar.

**Expected:** Run fires, tool chips appear, answer is grounded.

**Pass criterion:** Existing diagnostic functionality works without any new gates enabled.

**Fail criterion:** Run fails or produces an error.

---

## Section 5: Screenshot Capture for Partner Submission

After all tests pass (or after the primary test suite, before PMP test if optional), the executing agent has already captured the following screenshots. Verify they exist and are readable.

| Screenshot | Purpose | Captured in |
|---|---|---|
| `partner-00-task-pane-empty.png` | Task Pane overview with new UI | Test A |
| `partner-01b-tool-chips.png` | Tool chips firing during a run | Test B |
| `partner-02-ribbon-tab.png` | Ribbon with Adze tab visible | Test C |
| `partner-02b-ribbon-buttons.png` | Ribbon with all 6 buttons expanded | Test D |
| `partner-03-ribbon-diagnose.png` | Ribbon Diagnose run in progress or completed | Test F |
| `partner-03b-ribbon-mates.png` | Ribbon Mates result with subassembly mates listed | Test G |
| `partner-04-context-menu-feature.png` | Context menu on a feature showing "Ask Adze" item | Test I |
| `partner-04b-context-menu-component.png` | Context menu on a component showing "Ask Adze" item | Test K |
| `partner-04c-context-menu-diagnose.png` | Context menu on empty canvas showing "Diagnose" item | Test L |
| `partner-05-toast-notification.png` | Tray toast notification (if visible) | Test M |
| `partner-06-pmp-write-card.png` | PMP modal for dimension write confirmation | Test N |
| `partner-07-regression-baseline.png` | Baseline UI with all gates disabled | Test O |

**Save location:** `C:\adze-cad\solidworks-partner\screenshots\`

**Overwrite policy:** Each run's screenshots overwrite the previous ones. Only the final, passing run's screenshots are kept.

---

## Section 6: Pass/Fail Report Format

At the end of the test run, create a markdown report file: `C:\adze-cad\solidworks-partner\screenshots\TEST-REPORT-2026-04-20.md`

**Structure:**

```markdown
# Adze v0.1.2 Automated Testing Report

**Date:** [execution date]
**Executor:** [agent name or "automated"]
**Result:** [PASS / FAIL / BLOCKED]
**Summary:** [one-sentence summary]

## Test Results

| Test ID | Name | Status | Notes | Screenshot |
|---------|------|--------|-------|------------|
| A | Adze Task Pane Appears | PASS / FAIL | [observation] | partner-00-task-pane-empty.png |
| B | Baseline In-Pane Toolbar | PASS / FAIL | [observation] | partner-01b-tool-chips.png |
| C | Ribbon Tab Presence | PASS / FAIL | [observation] | partner-02-ribbon-tab.png |
| D | Ribbon Tab Six Buttons | PASS / FAIL | [observation] | partner-02b-ribbon-buttons.png |
| E | Ribbon Ask Button | PASS / FAIL | [observation] | N/A |
| F | Ribbon Diagnose Button | PASS / FAIL | [observation] | partner-03-ribbon-diagnose.png |
| G | Ribbon Mates Button | PASS / FAIL | [observation] | partner-03b-ribbon-mates.png |
| H | Ribbon Dimensions Button | PASS / FAIL | [observation] | N/A |
| I | Context Menu Feature | PASS / FAIL | [observation] | partner-04-context-menu-feature.png |
| J | Context Menu Feature Click | PASS / FAIL | [observation] | N/A |
| K | Context Menu Component | PASS / FAIL | [observation] | partner-04b-context-menu-component.png |
| L | Context Menu Empty Canvas | PASS / FAIL | [observation] | partner-04c-context-menu-diagnose.png |
| M | Toast Notification | PASS / FAIL / SKIP | [observation] | partner-05-toast-notification.png |
| N | PMP Write Confirmation | PASS / FAIL / BLOCKED | [observation] | partner-06-pmp-write-card.png |
| O | Regression: Gates Disabled | PASS / FAIL | [observation] | partner-07-regression-baseline.png |
| P | Regression: Baseline Diagnose | PASS / FAIL | [observation] | N/A |

## Blockers Encountered

[If any: list 3DX login prompt, launcher issues, crashes, or feature initialization failures here]

## Recommendations

- [If all tests pass: "Ready for partner submission."]
- [If Test N (PMP) fails: "PMP write support not initialized. Acceptable for v0.1.2 (marked proof-of-path). HTML fallback active."]
- [If multiple tests fail: "Investigate and retry."]

## Log Files Captured

- Host log: `%LOCALAPPDATA%\Adze\logs\host.log` (tail last 50 lines, if relevant)
- Launcher preflight report: `%LOCALAPPDATA%\Adze\logs\launcher-preflight.json` (if 3DX blocker occurred)
```

---

## Section 7: Escalation

### Launcher Blocker (3DX Login/Update)

If a 3DEXPERIENCE login or update window appears during launch (Section 2.3):

1. Take a screenshot showing the blocker.
2. Do NOT click any button on the blocker window.
3. In the Pass/Fail report, mark all tests as "BLOCKED: 3DX login prompt."
4. Record the blocker in the report's "Blockers Encountered" section.
5. Recovery requires either waiting for the platform update to complete (manual) or running the test on a different machine.

### Feature Gate Initialization Failure

If a feature gate fails to initialize (e.g., ribbon does not appear even though `SOLIDWORKS_AI_RIBBON=true` is set):

1. Check the host log: `%LOCALAPPDATA%\Adze\logs\host.log`
2. Search for the feature name (e.g., "RibbonTab", "ContextMenu", "Toast").
3. If the log shows an initialization error, note which gate failed.
4. Continue testing other features. A single-gate failure does not block the release if other gates pass.
5. Mark the failed test as "BLOCKED: Feature initialization failed. See host.log." and note the log line.

### Crash or COM Exception

If SOLIDWORKS crashes or a COM exception occurs during testing:

1. Record which test caused the crash (by name and ID).
2. Copy the host log tail (last 50 lines) into the report's "Log Files Captured" section.
3. Mark the test as "FAIL: SOLIDWORKS crash."
4. Mark all subsequent tests as "NOT RUN" due to crash.
5. Report: "Critical failure detected. Add-in crashed at [test name]. See logs for details."

### PMP Write Test Failure (Acceptable)

If Test N (PMP Write Confirmation) fails but all other tests pass:

1. Mark Test N as "BLOCKED: PMP proof-of-path not operational."
2. Confirm that the HTML write confirmation card still appears as fallback (check manually).
3. Mark it as acceptable for v0.1.2 per CHANGELOG.md.
4. Report: "PMP write feature not initialized. HTML write cards active as fallback. Acceptable for v0.1.2."

---

## Section 8: Quick Reference — Coordinates and Label-Based Clicks

To minimize brittle pixel-coordinate dependencies, use label-based `mcp__windows__Click` whenever possible. The `mcp__windows__Snapshot` tool returns interactive element references.

**Generic label-based click pattern:**

```
# Step 1: Get the page structure with element references
mcp__windows__Snapshot (use_annotation=true)

# Step 2: Identify the element by its label or visual text
# The snapshot returns ref IDs like ref_1, ref_2, etc.

# Step 3: Click by reference/label
mcp__windows__Click (label=42)
```

**Fallback: Pixel-based click when label is unavailable:**

```
mcp__windows__Click (loc=[x, y])
```

**Key locations (approximate pixel coordinates, adjust per actual screenshot):**

| Element | Approx. Coordinates | Better approach |
|---------|---|---|
| Task Pane Prompt Box | [350, 150] | Use label "Ask about the active document..." |
| Run Button | [280, 180] | Use label "Run assistant" |
| Ribbon Adze Tab | [1050, 50] | Use label "Adze" |
| FeatureManager Tree | [100, 300] | Use label of feature name |
| 3D Graphics Area | [700, 400] | Right-click at this coordinate for empty-canvas menu |

---

## Appendix: Known Issues and Watch Points

1. **SOLIDWORKS Maker license:** If the add-in fails to load, it may be a Maker-tier licensing issue. Document if encountered.

2. **Multi-monitor setups:** If SOLIDWORKS is on a secondary monitor, adjust screenshot coordinates accordingly. The `mcp__windows__Snapshot` tool is monitor-aware.

3. **Slow machine or heavy load:** If tool execution takes longer than expected (>30 seconds per run), wait longer before expecting the answer.

4. **Local model latency:** If testing with Ollama or LM Studio, add 20–60 seconds to expected run times.

5. **Toast notifications:** If the system has notification popups disabled (Windows Settings > Notifications & Actions), the toast may not appear visually even if it fired. Check the log.

---

**End of Automated Testing Procedure — Adze v0.1.2**
