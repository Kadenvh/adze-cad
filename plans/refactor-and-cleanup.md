# Refactor & Cleanup — Project Curation Pass

**Created:** 2026-05-14 (session 10) | **Status:** Scoped, not started | **Target:** Next dedicated session, single sitting where feasible

This plan exists because three sessions of "looks identical" debugging exposed how many ways the project is currently confusable. Session 10's install/ consolidation (`install/CLAUDE.md`) is the first move; this plan captures the rest.

Founder framing 2026-05-14:
> "we have like 3 or 4 points of failure, we need to define a source of truth & schema (in this readme/claude) and STICK to it... do you want to refine our project contents effectively, ie curate && verify all documentation and folder contents, like dejunk everything?"

The answer is yes. This is the dejunk pass.

---

## Mission

Reduce points of failure across the install / deploy / dev-loop surface to one canonical entry point per concern. Every loose script, ambiguous doc, and duplicate source of truth gets either consolidated, moved to its semantic home, or deleted with a written reason.

**Success criteria:**
- One file answers each question. No "which README do I read."
- Every entry point that ships to a user is a `.bat` (or a `.exe`). `.ps1` files are dev/implementation, never the documented user surface.
- Every script that ships has its non-shipping siblings in a clearly different home.
- Every `plans/` file is either active and current, or archived with an extraction receipt.
- An external beta tester can install, run, and report a failure through a documented opt-in pathway that we receive. No reliance on screenshots and back-and-forth.

---

## Scope

### Track A — Install/ surface (mostly DONE in session 10)

- ✅ Single source of truth: `install/CLAUDE.md` (auto-loaded by agent).
- ✅ Old `install/README.md` + `install/RELEASE-AND-TESTING.md` deleted.
- ✅ Install logging — both scripts tee output via `Start-Transcript` to `%LOCALAPPDATA%\Adze\logs\install-<ts>.log` / `uninstall-<ts>.log`. No Manager required.
- ✅ **Moved `install/package-release.ps1` → `scripts/setup/package-release.ps1`** (session 10, Decision #29). Path computations updated (2-level repoRoot + `$installDir` for sibling refs), all 5 doc references migrated, validator reference updated, zip rebuilt clean from new location.
- ☐ **Hide loose `.ps1` from the documented surface.** `install-adze.bat` and `uninstall-adze.bat` are the only user-facing entry points. Both invoke the sibling `.ps1`. Consider:
  - Option 1: rename `.ps1` to `_install-impl.ps1` / `_uninstall-impl.ps1` (underscore prefix as the "internal" convention).
  - Option 2: move to `install/internal/` and update `.bat` paths.
  - Option 3: leave names alone; rely on docs + `.bat`-first defaults. **Recommended** — renaming forces a touch of every doc, deploy script, and historical reference for low ergonomic gain.
- ☐ **Consider whether old zips (`adze-v0.1.0.0.zip`, `adze-v1.0.0.0.zip`) should live in `install/` long-term.** They're shippable artifacts of past releases. Either keep them as a local archive, or move to `install/archive/` to clean the directory listing. They're already gitignored.

### Track B — Scripts / dev tooling

- ☐ `scripts/setup/` has 28 scripts across build, validate, deploy, host registration, benchmarks, fixtures, and GitHub. The README organizes them into 6 categories but some scripts overlap or duplicate behavior. Audit:
  - `build-host.ps1` vs `build-all.ps1` — both exist; do we need both?
  - `register-host-addin.ps1` vs `install/install-adze.ps1` — same operation different code paths. Pick one canonical implementation; the other delegates or is removed.
  - `reload-host.ps1` orchestrates several scripts — is it still current?
- ☐ Consolidate by responsibility:
  - **Build:** build-all (release default), build-host (host only — keep if useful)
  - **Test:** run-tests, run-provider-smoke
  - **Validate:** validate-package, validate-json-schemas, validate-host-spike
  - **Deploy:** deploy-to-test, package-release (moved here from `install/`)
  - **Diagnose:** diagnose-install, collect-support-bundle
  - **Dev registration:** reload-host (and maybe collapse register-host-addin + unregister-host-addin into a single `-Mode {Install|Uninstall}` switch)
  - **Benchmarks + fixtures:** as-is
- ☐ Make `scripts/setup/README.md` the navigation source of truth for dev scripts (it already mostly is).

### Track C — Plans/ ✅ (session 11)

- ✅ Audit complete (session 10 parallel-explore pass). Classification documented in `plans/project-index.md` §5.
- ✅ 9 historical plans archived to `plans/archive/2026-05-15-session-10-curation/` with `ARCHIVE_RECEIPT.md`: phase10-ui-expansion, polish-and-v1-path, crash-investigation-20260419, discovery-clarification-ui, documentation-structure, github-labels, TESTING-MANUAL-USER, TESTING-PROCEDURE-AUTOMATED, release-process.
- ✅ `plans/tasks/TASK-INDEX.md` rewritten as current-state snapshot (session 11). Was 415 lines stale at 2026-03-23; now compact ledger with open items called out at the top, Phase 11 + v1.1 UI rebuild + cross-cutting sections current.
- ✅ 3 research briefs classified (session 11): `design-mcp-server.md` KEEP + freshness note (tool count drift 19 → 27); `research-openclaw-feasibility.md` + `research-solidworks-ai-ecosystem.md` ARCHIVED with extraction receipts.

### Track D — Docs / brain.db / memory (partial ✅ session 11)

- ✅ Root-level doc sweep (session 11). Tool counts brought current across `README.md` (architecture diagram + project table + Test Coverage section), `SETUP.md` (intro + Feature Gates table now includes `SOLIDWORKS_AI_MESH_IMPORT`), `CONTRIBUTING.md` (project structure table). `CHANGELOG.md` Phase 11 entry already current. `LICENSE`/`SECURITY.md`/`PRIVACY.md` had no count drift.
- ☐ brain.db identity entries: verify `project.version` matches what's actually in csproj. Old entries that no longer reflect reality get updated or removed. **Open.**
- ☐ Memory directory (`.claude/memory/`) — audit for stale feedback memories. **Open.**
- ☐ brain.db notes — sweep open notes; many of the "improvement" notes have been addressed and aren't closed. **Open.**

### Track E — Observability for external testers (design landed session 11)

> *Founder asked:* "for external testing/users, should there be a place in settings where I/they can connect to the 'log/debug' server so we recieve them?"

**Yes. Design landed. Implementation deferred to a dedicated session.**

📄 **Design brief:** [`plans/research-opt-in-telemetry.md`](research-opt-in-telemetry.md) (session 11). Covers: allowed-events whitelist + hard-exclusion list + threat model for why messages/stack traces aren't sent; opt-in mechanic via `Adze.Manager` Settings tab with confirmation dialog; backend recommendation (Cloudflare Workers + R2) with cost/complexity trade-off table; revocation path; 6 open questions blocking implementation. Updates to `PRIVACY.md` and `SECURITY.md` are required at ship time.

- ☐ Define what we want telemetry to capture:
  - Install attempt outcomes (success / specific failure mode)
  - First-launch host.log signals (probe outcome, ribbon registration outcome)
  - Crash reports (uncaught exceptions, COM failures)
  - **NOT** captured: document contents, model prompts, API keys, user identity (anonymized device ID at most)
- ☐ Design opt-in flow:
  - Settings tab in Adze.Manager: "Share diagnostic logs with VH Tech" toggle, default OFF
  - Toggle ON requires a confirmation dialog explaining what's sent + linked to PRIVACY.md
  - User can revoke at any time
- ☐ Backend:
  - Simple HTTPS POST endpoint receiving JSONL diagnostic events
  - Storage: anything that's cheap and queryable. Cloudflare Workers + R2 / a tiny VPS / Supabase — pick smallest viable.
  - Auth: per-tester opaque token issued at opt-in time, so we can correlate sessions but not identify the user.
- ☐ Implementation:
  - `Adze.Trace` already has the log infrastructure. Add a `TelemetrySinkHttp` alongside the file sink.
  - Background uploader thread, batched POSTs every N minutes or N events.
  - Backoff + drop on repeated failures (no infinite retry).
- ☐ Documentation:
  - `PRIVACY.md` already exists — update with the new telemetry flow.
  - `install/CLAUDE.md` documents how to enable telemetry as a tester.
- ☐ **Privacy is non-negotiable.** Opt-in only. Default OFF. Explicit "what we collect" list. Easy revocation. Document storage retention + deletion path.

### Track F — Build artifact hygiene (cross-cutting)

- ☐ Decision #28 mitigates the stale-DLL bug at packaging. Consider whether the same check should also gate the test runner — i.e. `run-tests.ps1` should refuse to run if `bin/Debug` is older than source, so we don't get false-green tests on stale binaries.
- ☐ `validate-package.ps1` currently checks Release DLLs. Add a `-Debug` mode that checks Debug DLLs for the test path.

---

## Explicit non-goals

- **Not rewriting the install logic in .bat.** The .ps1 implementation has registry calls, SHA verification, COM registration, transcript logging — all of which are awful or impossible in batch. The .bat is the user entry point; the .ps1 is the implementation.
- **Not consolidating into one giant `manage-adze.ps1`.** Single-purpose scripts compose better and document themselves better than mega-scripts.
- **Not removing `Adze.UiHarness`.** The dev shell is valuable for iterating UI without SW round-trips, even if it's currently dev-only.
- **Not redesigning the agent loop or write framework.** This pass is hygiene, not architecture.

---

## Sequencing for the actual session

1. **Track A finish** (15 min) — move package-release.ps1, update paths in `install/CLAUDE.md` and `scripts/setup/README.md`.
2. **Track F** (30 min) — wire freshness check into `run-tests.ps1` so we never green-light on stale Debug.
3. **Track C** (45 min) — plans/ audit + archive moves.
4. **Track B** (1-2 h) — scripts audit + consolidation. Highest risk of breaking workflow; do after C is settled.
5. **Track D** (30 min) — root docs sweep.
6. **Track E** (multi-session) — telemetry endpoint design. Land design first as `plans/research-opt-in-telemetry.md` with an explicit privacy model. Implementation is a follow-on.

Track E is the biggest. Don't try to do it in the dejunk session — scope it, plan it, build the endpoint and the opt-in flow in dedicated sessions.

---

## Success exit criteria for the dejunk session

- `install/` contains: `CLAUDE.md`, `install-adze.bat`, `install-adze.ps1`, `uninstall-adze.bat`, `uninstall-adze.ps1`, `dist/` (gitignored), `adze-v*.zip` (gitignored), and nothing else.
- `scripts/setup/` README accurately describes every script in it; no orphans.
- `plans/` only contains active strategy files + `archive/`.
- `run-tests.ps1` refuses to run on stale Debug.
- `plans/research-opt-in-telemetry.md` exists with a credible privacy model.
- Open brain.db notes count is down ≥30% from sweeping resolved items.

---

## Cross-references

- `install/CLAUDE.md` — Track A landing zone (mostly done in session 10)
- `scripts/setup/README.md` — Track B landing zone
- brain.db Decision #28 — Build artifact freshness gate (origin of Track F)
- brain.db trace #18 — the stale-DLL incident that motivated this whole plan
- session 10's `commits b57baa8` and onward — install/ consolidation
