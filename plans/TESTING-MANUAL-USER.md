# v0.1.2 Testing Manual — Kaden's Copy

**Target release:** v0.1.2 on 2026-04-20
**Audience:** you, running the validation pass on your own machine
**Sister document:** `plans/TESTING-PROCEDURE-AUTOMATED.md` (agent script, different scope)

---

## 1. Why this exists

This is your hands-on checklist for the v0.1.2 validation pass. It assumes you know the product and have full access to SOLIDWORKS Makers. The agent-facing companion (`TESTING-PROCEDURE-AUTOMATED.md`) is deterministic and exhaustive; this one is faster and trusts your judgment.

Run this before submission day. If anything surprises you, the automated procedure will catch edge cases you did not stress.

---

## 2. Pre-flight

Do this once, before you launch SOLIDWORKS:

- [ ] Branch is `main`, clean working tree, `git log -1` shows the v0.1.2 commit head
- [ ] `pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks` — 0 errors, 0 warnings
- [ ] `pwsh -NoProfile -File scripts\setup\run-tests.ps1 -SkipRestore` — all 670 pass (6 inconclusive smoke tests are expected without a `.env` key)
- [ ] `powershell.exe -NoProfile -File install\install-adze.ps1` — installs from the fresh Debug build
- [ ] Check DLL timestamp: `ls $env:LOCALAPPDATA\Adze\bin\Adze.Host.dll` should match your latest build time

### Environment for this test session

Set these in the PowerShell session you will launch SW from. The add-in reads env vars at `ConnectToSW` so they must be present before SW starts.

```powershell
$env:SOLIDWORKS_AI_ENABLE_MODEL      = 'true'
$env:SOLIDWORKS_AI_AGENT_LOOP        = 'true'
$env:SOLIDWORKS_AI_FIRST_WAVE_WRITES = 'true'
$env:SOLIDWORKS_AI_RIBBON            = 'true'
$env:SOLIDWORKS_AI_CONTEXT_MENU      = 'true'
$env:SOLIDWORKS_AI_TOAST             = 'true'
$env:SOLIDWORKS_AI_STREAM_FINAL_TEXT = 'true'
# leave PMP_WRITES off for the first pass — enable it in Section 8 only
```

If you have a provider key in `.env`, run `. scripts\setup\load-env.ps1` first so the API key is available.

---

## 3. Launch

```powershell
Start-Process 'C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\sldworks.exe' `
  -ArgumentList '"C:\SOLIDWORKS\spiderbot\SpdrBot v14.SLDASM"'
```

Expect to handle a 3DEXPERIENCE Platform login prompt. Log in as you normally would. Wait for SpdrBot v14 to finish loading.

**Quick sanity:**
- [ ] Adze task pane sidebar visible with navy header
- [ ] "Adze" ribbon tab visible next to Features / Sketch / Evaluate
- [ ] No error popups from Adze (`%LOCALAPPDATA%\Adze\logs\host.log` tail is clean)

If any of these fail, note in `host.log` which surface did not register and continue. The Task Pane is the baseline; losing ribbon or context menu does not kill the session.

---

## 4. Baseline Task Pane behavior (regression check)

These worked in v0.1.1 and should still work.

- [ ] **Diagnose toolbar button** — click in-pane Diagnose, tool chips fire, grounded answer appears
- [ ] **Mates toolbar button** — tool chip shows `get_mates` firing, answer now includes subassembly mates (the 873477e fix). Before the fix this returned "No mates found" on SpdrBot.
- [ ] **Dimensions toolbar button** — shows a dimensions list
- [ ] **Properties toolbar button** — shows custom properties
- [ ] **Free-form prompt** — type "What is this assembly doing?" and click Run. Answer should reference SpdrBot components by name.
- [ ] **Cancel button** — during an agent run, click Cancel; the run aborts, no crash

If any of these fail, stop — this is a regression and must be fixed before shipping.

---

## 5. Ribbon tab (new, Phase 2)

- [ ] Click the "Adze" ribbon tab
- [ ] Confirm 6 buttons visible: **Ask**, **Diagnose**, **Mates**, **Dimensions**, **Properties**, **Explain**
- [ ] Hover each — tooltips match `RibbonTab.cs` Buttons table
- [ ] **Ask** — focus jumps to the Task Pane prompt input box. No run fires. Test pane is scrolled to input.
- [ ] **Diagnose** — agent run fires, same prompt as the in-pane button
- [ ] **Mates** — mate listing run, verify subassembly mates present in the answer
- [ ] **Dimensions** — dimension listing run
- [ ] **Properties** — custom-property listing run
- [ ] **Explain** — with nothing selected, get a document-level explanation. With a feature selected, the answer describes that feature.

**Known weakness:** Ribbon icons use SOLIDWORKS defaults (we did not bundle custom PNG icon lists for v0.1.2). The text labels carry the UX. Do not fix — document and defer to v0.2.0.

---

## 6. Feature-tree context menu (new, Phase 3)

Three selection types drive three menus.

### 6.1 Right-click a feature in the FeatureManager tree

- [ ] Expand SpdrBot → pick any solid feature (e.g. `Extrude1` on a sub-part)
- [ ] Right-click it
- [ ] Confirm "Ask Adze about this feature" item present
- [ ] Click it — Task Pane focuses, agent run fires with the `explain` prompt
- [ ] Grounded answer references the specific feature name

### 6.2 Right-click an assembly component

- [ ] In the FeatureManager tree, right-click a subassembly or part component
- [ ] Confirm "Ask Adze about this component" item present
- [ ] Click it — answer summarizes the component

### 6.3 Right-click in empty graphics area

- [ ] Right-click in empty space on the canvas (nothing selected)
- [ ] Confirm "Diagnose this model" item present
- [ ] Click it — diagnostic run fires

If a menu item is missing, check whether the gate was set before SW launched. Context menu registration happens in `AttachActiveDocumentEvents` which fires on document open; re-launching the doc should re-register.

---

## 7. Toast notifications (new, Phase 4)

Toasts fire only when SW is not the foreground window.

- [ ] Click Diagnose in the Task Pane
- [ ] Immediately alt-tab to another application (browser is fine)
- [ ] Wait for the run to complete (~5-15s depending on provider)
- [ ] Tray balloon should appear: "Adze run complete" with the first ~180 chars of the answer
- [ ] Alt-tab back to SW
- [ ] Trigger another run without alt-tabbing — **no toast** should fire (SW is foreground)
- [ ] Simulate failure (unplug network, trigger run) — toast reads "Adze run failed" with warning icon

Gotcha: if you never installed a `NotifyIcon` before, the first toast might land behind the Windows "new app in tray" notification. Click into the tray overflow to confirm.

---

## 8. PropertyManager Page writes (new, Phase 4 proof-of-path)

**Enable only after Sections 4–7 pass.** This is the most experimental surface.

```powershell
$env:SOLIDWORKS_AI_PMP_WRITES = 'true'
# Then relaunch SW
```

### 8.1 Trigger a `set_dimension_value` write

- [ ] Prompt: "Change the main length dimension to 150mm" (or pick a real dimension name from SpdrBot)
- [ ] Agent runs, calls `set_dimension_value`, captures a pending write
- [ ] **Expected:** a native SOLIDWORKS PropertyManager Page opens on the left showing "Confirm: set_dimension_value" with labels: Summary, Undo label, New value, Dimension
- [ ] Click the green checkmark (OK)
- [ ] The dimension changes in the model, write history entry appears in Task Pane

### 8.2 Cancel flow

- [ ] Trigger another `set_dimension_value` write
- [ ] Click the red X (Cancel) on the PMP
- [ ] No change to the model, write history shows a cancelled entry

### 8.3 Fallback

- [ ] Disable the gate: `$env:SOLIDWORKS_AI_PMP_WRITES = 'false'; # relaunch`
- [ ] Trigger a `set_dimension_value` write
- [ ] **Expected:** HTML write confirmation card in the Task Pane (the v0.1.1 flow)
- [ ] Other write tools (`set_custom_property`, `suppress_feature`, etc.) still use the HTML card even with PMP on — they are not yet wired through the broker. This is intentional for v0.1.2.

### 8.4 If PMP fails

If the PMP does not open, or SW crashes, or the write applies without ever showing a PMP:
1. Note the exact tool + arguments that triggered the failure
2. Disable the gate before shipping
3. File in the CHANGELOG as "PMP: experimental, deferred to v0.2.0 if unstable"
4. The rest of v0.1.2 still ships — PMP is designed to be gated off without collateral damage

---

## 9. Screenshot capture (for partner submission)

Capture these at the natural points in the flow above. Save to `solidworks-partner\screenshots\` overwriting the existing `partner-*.png`.

| # | File | When |
|---|------|------|
| 01 | `partner-01-task-pane-overview.png` | Section 4 baseline, empty state with toolbar |
| 02 | `partner-02-ribbon-tab.png` | Section 5, Adze ribbon tab clicked, all 6 buttons visible |
| 03 | `partner-03-tool-chips.png` | Section 4.1 mid-run, tool chip animation visible |
| 04 | `partner-04-grounded-answer.png` | Section 4.1 post-run, full answer rendered |
| 05 | `partner-05-context-menu.png` | Section 6.1, right-click menu with "Ask Adze about this feature" visible |
| 06 | `partner-06-pmp.png` | Section 8.1, PMP modal open with labels visible (skip if PMP is gated off) |

Also capture a 30–60s demo GIF of the Diagnose flow: open the assembly, click ribbon Diagnose, watch tool chips fire, read the answer. GIF goes in the release body.

---

## 10. Wrap-up

- [ ] Close SOLIDWORKS cleanly (File → Exit)
- [ ] Check `%LOCALAPPDATA%\Adze\logs\host.log` for any `ERROR` entries from this session
- [ ] Back up any failing screenshots to a `partner-bad-*.png` naming in case re-capture is needed
- [ ] Record overall pass/fail in `plans/RELEASE-4-20.md` under the Sunday / Monday section

If ≥8 of the 11 main sections pass (Sections 4 + 5 + 6 + 7), v0.1.2 ships. Section 8 (PMP) is non-blocking.

If fewer than 8 pass, cut v0.1.2 without the failing surface's feature gate on by default (they already are off by default — which means the release still works for users who do not opt in). Document honestly in CHANGELOG.

---

## Appendix: What this manual does NOT cover

- Every possible SW assembly type
- Cross-provider behavior (Ollama vs OpenAI vs Anthropic) — the automated procedure covers a provider matrix
- High-mate-count stress (the automated procedure has a paginated test)
- Long-session memory behavior
- Localization
- Multi-display setups

Those are the automated procedure's job.
