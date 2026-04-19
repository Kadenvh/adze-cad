# Draft — GitHub Issue for R1 Crash Tracking

**Purpose:** paste the body below into a new GitHub issue at `https://github.com/Kadenvh/adze-cad/issues/new?template=crash_report.md` once you have GitHub labels + issue templates applied. This is the public tracking anchor for the v1.0 blocker.

**Title suggestion:** `Crash: enabling Adze after 3DEXPERIENCE R2026x update (SW build 34.1.0.0140) terminates sldworks.exe after ContextMenu attach`

**Labels to apply:** `bug`, `crash`, `v1.0-blocker`, `update-lifecycle`, `sw-3dx-r2026x`, `interop`

---

## Issue body (paste below this line)

**What crashed**
- [x] SOLIDWORKS itself (hard close / no error dialog)

Crash is silent from the user's perspective — `sldworks.exe` disappears shortly after the Adze Task Pane finishes initial render. SW crash dumps are generated but the user sees no in-app dialog.

**When did it crash?**
- [x] On SOLIDWORKS launch with Adze installed
- [x] When enabling Adze via Tools > Add-Ins (after the add-in was auto-disabled from a prior launch crash)

Both scenarios reach the same faulting code path. First-launch crashes cause SOLIDWORKS' own add-in recovery to disable Adze, after which the user must manually re-enable to reproduce.

**To reproduce**
1. Install Adze v1.0 (pre-release, built from `main` at commit TBD) on a machine running 3DEXPERIENCE SOLIDWORKS R2026x build 34.1.0.0140
2. Launch SOLIDWORKS (either directly or via 3DEXPERIENCE Launcher)
3. Open any assembly (observed on SpdrBot v14.SLDASM)
4. SOLIDWORKS crashes silently without a user-facing error dialog

Workaround: `powershell -NoProfile -File install\install-adze.ps1 -Uninstall` removes the add-in; SW launches normally without Adze.

**Environment**
- **SOLIDWORKS build number:** 34.1.0.0140 (post-R2026x update applied 2026-04-19)
- **SOLIDWORKS edition:** 3DEXPERIENCE SOLIDWORKS R2026x (Makers tier)
- **License type:** Makers ($48/yr consumer tier)
- **Adze version:** v1.0.0-pre (source `AssemblyVersion` 1.0.0.0, unshipped)
- **AI provider:** none — crash occurs with deterministic broker, no API key configured
- **Windows version:** Windows 11
- **Recent SW / 3DEXPERIENCE update?** Yes — R2026x update applied 2026-04-19 immediately before crashes began

**Crash artifacts**

CXPD sessions in `%APPDATA%\SolidWorks\`:
- `20260419013311_34.1.0.0140\` (06:33:11 UTC)
- `20260419013348_34.1.0.0140\` (06:33:48 UTC)
- `20260419013814_34.1.0.0140\` (06:38:14 UTC)

Adze `host.log` pattern (three consecutive sessions, all ending identically):

```
[2026-04-19 06:33:23Z] INFO ConnectToSW starting.
[2026-04-19 06:33:23Z] INFO Task Pane created. AddControl returned: ControlAxSourcingSite [Adze.Host.UI.TaskPaneControl].
[2026-04-19 06:33:23Z] INFO RibbonTab: registered successfully across part/assembly/drawing contexts.
<session ends here — no subsequent Snapshot captured, no ConnectToSW completed>
```

Compare to v0.1.1 session at 05:15:45Z on the same machine (before v1.0 install, context-menu gate default-off):

```
[2026-04-19 05:15:45Z] INFO ConnectToSW starting.
[2026-04-19 05:15:45Z] INFO Task Pane created. ...
[2026-04-19 05:15:45Z] INFO Ribbon gate SOLIDWORKS_AI_RIBBON is off; skipping tab.
[2026-04-19 05:15:45Z] INFO Context-menu gate SOLIDWORKS_AI_CONTEXT_MENU is off; skipping.
[2026-04-19 05:15:45Z] INFO Snapshot captured. reason=Initial context snapshot tool_count=10 ...
[2026-04-19 05:15:45Z] INFO ConnectToSW completed.
```

**Hypothesis (preliminary — validate in R1)**

After `RibbonTab.Register()` returns, the next initialization step is `ContextMenu.Attach()` (now default-on in v1.0 — it was default-off in v0.1.1 via `SOLIDWORKS_AI_CONTEXT_MENU` gate). The `host.log` silence after `RibbonTab: registered successfully` suggests the faulting frame is inside or immediately after the context-menu attach code path. SW build 34.1.0.0140 likely changed the interop surface for one of:

- `ISwAddin`-level notification registration for context-menu events
- `ModelDoc2`-scoped `IAttachContextMenuItem`-style calls
- The `ISldWorks.SetAddinCallbackInfo2` / context-menu callback registration

**Feature gates active at crash**

No env vars were set. v1.0's new `%LOCALAPPDATA%\Adze\config.json` defaults were active:

```json
{
  "SOLIDWORKS_AI_RIBBON": true,
  "SOLIDWORKS_AI_CONTEXT_MENU": true,
  "SOLIDWORKS_AI_ENABLE_MODEL": true,
  "SOLIDWORKS_AI_AGENT_LOOP": true,
  "SOLIDWORKS_AI_FIRST_WAVE_WRITES": true,
  "SOLIDWORKS_AI_STREAM_FINAL_TEXT": true,
  "SOLIDWORKS_AI_RETRIEVAL": true,
  "SOLIDWORKS_AI_TOAST": false,
  "SOLIDWORKS_AI_PMP_WRITES": false,
  "SOLIDWORKS_AI_LOCAL_MODELS": false
}
```

(Note: config.json file may not literally exist on disk — these are the baked-in defaults per `FeatureGateRegistry.GetDefault` when no env var is set and no config has been written.)

**Additional context**

- The 3DEXPERIENCE desktop updater separately fails to run while SW is held open by a crashed add-in (`"Failed to parse user intentions file: ...\InstallData\incomplete\UserIntentions.tmp"`), even though this file isn't the root cause. This compounds the UX impact — a user hitting this crash cannot easily recover without knowing to uninstall Adze first.
- The crash does not reproduce with `SOLIDWORKS_AI_CONTEXT_MENU=false` set as an env var override (inferred from the clean 05:15:45Z session above).
- v0.1.1 public release (last shipped) does not reproduce because its defaults are gate-off; the same interop call would only fire with an explicit env var set.

**Tracking**

- Adze plan phase: **R1 — Crash root cause** in `plans/polish-and-v1-path.md`
- Linear epic: `ADZ-R1` (private)
- This issue blocks v1.0.0 release. See `plans/polish-and-v1-path.md` R5 release-gate criteria for what must pass before this issue can close.

---

## After filing

- [ ] Apply labels: `bug`, `crash`, `v1.0-blocker`, `update-lifecycle`, `sw-3dx-r2026x`, `interop`
- [ ] Link from Linear ADZ-1 epic as the public mirror
- [ ] Pin to the Issues tab if it stays open long enough to warrant pinning
- [ ] Add a comment with the eventual `plans/crash-investigation-20260419.md` link once R1.4 is written
