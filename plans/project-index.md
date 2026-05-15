# Project Index — Where Things Live

**Created:** 2026-05-15 (session 10 deep-refinement pass) | **Purpose:** Canonical "where does X live" map. The single navigation source of truth.

This document was built from four parallel folder audits and is the answer to "I'm new (or returning after a week) — where do I look?" It's the index every other doc should defer to. When two docs disagree, this one wins until updated.

**For routine work, you do not read this file end-to-end.** Use the table of contents, jump to the section that owns your concern, follow the cross-refs.

---

## Table of contents

| Question | Section |
|---|---|
| "What does X project do?" | [§1 — src/ — Source projects](#1--src--source-projects) |
| "Where is the test for X?" | [§2 — tests/Adze.Tests/ — Test layout](#2--testsadzetests--test-layout) |
| "Where is the contract / schema for X?" | [§3 — schemas/ — Contracts](#3--schemas--contracts) |
| "How do I build / package / install / deploy?" | [§4 — install/ + scripts/setup/](#4--install--scriptssetup--build-and-deploy) |
| "What's the current strategic plan?" | [§5 — plans/ — Strategy and design](#5--plans--strategy-and-design) |
| "Where's the changelog / readme / setup?" | [§6 — Root-level docs](#6--root-level-docs) |
| "Where do benchmarks live?" | [§7 — benchmarks/](#7--benchmarks) |
| "Where's brain.db / sessions / handoffs?" | [§8 — Continuity layer (.ava/, sessions/)](#8--continuity-layer-ava-sessions) |
| "What needs attention right now?" | [§9 — Outstanding issues this audit surfaced](#9--outstanding-issues-this-audit-surfaced) |

---

## §1 — `src/` — Source projects

**10 production projects + 1 test project. All ship as part of the `Adze.sln` solution. AssemblyVersion 1.1.0.0 except where noted.**

| Project | Type | Files | Refs | Purpose | README |
|---|---|---:|---|---|:---:|
| `Adze.Contracts` | Library | 20 | (none) | Shared models, enums, tool names, abstractions, write contracts | ✓ |
| `Adze.Mesh` | Library | 16 | (none) | Mesh I/O + analysis + primitive plane fitting (Phase 11). Pure managed, no SW interop. | ✓ |
| `Adze.Trace` | Library | 13 | Contracts | Trace/progression/recipe/snapshot JSON persistence | ✓ |
| `Adze.Index` | Library | 5 | Contracts | Closed-file OLE Structured Storage indexer | ✓ |
| `Adze.Tools` | Library | 35 | Contracts, Index, Mesh | Read tools (Grounding/), compute tools (Compute/), mesh tools (Mesh/), write tools (Write/) | ✓ |
| `Adze.Broker` | Library | 42 | Contracts, Tools, Index | Prompt formatting, OpenAI/Anthropic clients, hybrid broker turn layer, agent loop | ✓ |
| `Adze.UI` | Library | 8 | Contracts, Broker, Trace | Native WinForms sidebar v2 (NativeTaskPaneControl + supporting controls). No SW interop. | ✓ |
| `Adze.Host` | Library (COM) | 22 | Contracts, Tools, Trace, Broker, UI | SW add-in lifecycle, Task Pane, ribbon, context capture, COM shims | ✓ |
| `Adze.Manager` | WinExe | 7 | Broker | 4-tab control panel (Logs / Settings / Agent Profile / Status) | ✓ |
| `Adze.UiHarness` | WinExe | 5 | Contracts, Trace, UI | Out-of-SW dev shell — mounts `NativeTaskPaneControl` against `StubHostState` for hot iteration | ✓ |

**Dependency direction:** Contracts at the bottom, Host at the top. Mesh, Index are leaf libraries (no Adze dependencies). Broker references Tools and Index. Host orchestrates everything. UI is a pure presentation layer with no SW interop.

**Conventions:**
- Tool implementations go under `Adze.Tools/{Grounding|Compute|Mesh|Write}/`. See `src/README.md` for the 10-step wiring checklist.
- Contract changes (Models/Tooling) ripple to `schemas/` — update both together.
- SW COM access lives in `Adze.Host` (in-proc) and write tool `Apply` paths (via `dynamic`). Nowhere else.
- New shippable project = run `pwsh scripts\setup\validate-package.ps1 -Fix` to register its DLL with the installer.

**What NOT to add:**
- `Adze.UI` and `Adze.Mesh` must NEVER reference `SolidWorks.Interop.*`. They run anywhere .NET 4.8 does.
- No new long-lived projects without first adding them to BOTH `$requiredDlls` arrays (Decision #28 + validator Check 1).
- No projects under `src/` that aren't `Adze.*`-prefixed.

---

## §2 — `tests/Adze.Tests/` — Test layout

**Single test project (NUnit 3). 59 .cs files. 823 tests local (817 passed + 6 inconclusive smoke).**

| Subdir | Files | Covers |
|---|---:|---|
| `Broker/` | 31 | Agent loop, prompt composer, model clients, write coordinator, conversation state, dispatchers, telemetry, error classifier, streaming, rate limit |
| `Tools/` | 11 | Read, compute, mesh, write tool implementations |
| `UI/` | 5 | `MarkdownToRichText`, `TaskPaneHost` contract, `QuickActionsBar`, `UiPalette`, `RefinementPanel` |
| `Mesh/` | 5 | `StlReader`, `MeshStatsCalculator`, `MeshAnalyzer`, `PrimitiveFitter`, `MeshTestFixtures` (procedural fixtures, no committed binaries) |
| `Trace/` | 4 | `MemoryStore`, `ModelJsonMapper` (incl. round-trip), `LearningSystem` |
| `Host/` | 3 | `HostStateAdapterMappingTests`, `HostProbeStateTests`, `PreUpdateEjectServiceTests` (gated by `IncludeHostTests`) |
| `Index/` | 1 | OLE closed-file indexer |
| `Helpers/` | 1 | `SessionContextFactory` — fixture builder used by ~20 tests |

**Conventions:**
- New tool → new test file under `Tools/`. Mirror the dispatcher pattern.
- Apply paths that require live SW COM are NOT unit-tested; they're exercised by live host validation.
- `Adze.Host` tests are conditional on `/p:IncludeHostTests=true` (default in local builds, off in CI to avoid COM dependencies).
- Smoke tests with the `LiveProvider` category skip gracefully when no API key is present; they show as `Inconclusive`, NOT failed.

**Cross-refs:** Test wiring is documented in `src/README.md` "Wiring a new tool — checklist." Run with `pwsh scripts\setup\run-tests.ps1`.

---

## §3 — `schemas/` — Contracts

**All 27 tool schemas validated. No orphans. No missing schemas.** Mirror of `src/Adze.Contracts/Tooling/ToolNames.cs` constants.

| Subdir | Count | Has README | Contents |
|---|---:|:---:|---|
| `schemas/context/` | 1 | ✗ | `session-context.schema.json` — the canonical SessionContext shape consumed by all read tools |
| `schemas/tools/` | 28 | ✓ | 27 `*.request.schema.json` + `tool-result-envelope.schema.json` |
| `schemas/traces/` | 3 | ✓ | `trace-event.schema.json`, `progression-state.schema.json`, `recipe-candidate.schema.json` |

**Conventions:**
- Tool schema filename = `<tool_name>.request.schema.json` exactly. The schema validator and `validate-package.ps1` enforce this.
- Action class color matches the SW capability tier:
  - `green` → read-only (snapshot + compute + retrieval + mesh-analyze)
  - `yellow` → first-wave write (small, reversible)
  - `red` → Class 3 elevated (opens new doc, asks SW to author geometry, etc.)
- Update schema and `ToolContracts.cs` parameters class together. They're a contract pair.

**Validation:**
```powershell
pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1
```

**Cross-refs:** Tool surface enumeration is in root `CLAUDE.md` Live Wave 1 / Compute / Retrieval / Mesh / Write sections. The shape of a `ToolResult` envelope is in `schemas/tools/tool-result-envelope.schema.json`.

---

## §4 — `install/` + `scripts/setup/` — Build and deploy

### `install/` — End-user surface
- `install-adze.bat` / `uninstall-adze.bat` — **canonical end-user entry points** (double-click)
- `install-adze.ps1` / `uninstall-adze.ps1` — implementation invoked by the `.bat` wrappers
- The packaging pipeline (`package-release.ps1`) lives in `scripts/setup/` (moved session 10, Decision #29). It writes the zip into `install/`.
- `adze-v*.zip` — shippable artifacts (gitignored)
- `dist/` — staging dir, recreated each package run (gitignored)
- `CLAUDE.md` — **authoritative release procedure**

**Procedure validated session 10:** install on dev box → SHA256 `2705E9D5...` matches end-to-end (build / zip / installed). See `install/CLAUDE.md` for the full lifecycle.

### `scripts/setup/` — Dev surface
**28 .ps1 scripts. Categorized in `scripts/setup/README.md`.** Key files:

| File | What for |
|---|---|
| `validate-package.ps1` | Pre-package contract validator (Check 1-4). **The freshness check that closed the session 8-10 stale-DLL gap.** Wired into `package-release.ps1` Step 0. |
| `deploy-to-test.ps1` | One-command cross-device deploy via SSH/SCP |
| `diagnose-install.ps1` | One-shot diagnostic dump for "install looks identical" reports |
| `build-all.ps1` | Build full solution (defaults to Debug — use `MSBuild ... /p:Configuration=Release` for the packager) |
| `run-tests.ps1` | NUnit 3 suite runner |
| `reload-host.ps1` | Dev-loop orchestrator: stop SW → build → register → launch |
| `validate-host-spike.ps1` | End-to-end live SW validation |

**Conventions:** Run with `pwsh -NoProfile -File scripts\setup\<name>.ps1`. Each script's synopsis comment block is authoritative for invocation.

**Cross-refs:** `install/CLAUDE.md` for release procedure; `scripts/setup/README.md` for full per-script index.

---

## §5 — `plans/` — Strategy and design

**19 files. Active plans drive current work; historical plans are reference; research briefs may be codified into code already.**

### Active (act on these tomorrow)
| File | Purpose |
|---|---|
| `phase11-embedded-ai.md` | Wave 1 outcome (mesh + sketch + drawing tools landed) + Wave 2/3/4/5 roadmap. **Primary work track.** |
| `ui-rebuild-v1.1.md` | v1.1 native WinForms sidebar rebuild (Chunks 1-3 + Round 2 landed; founder live-verification pending) |
| `refactor-and-cleanup.md` | Project-wide dejunk pass scoping. **Next dedicated session's target.** |
| `adr-001-native-winforms-ui-authority.md` | ADR for WebBrowser→native pivot |
| `END-GOAL-FINAL.md` | 700-line authoritative agentic vision (phases, capability classes, success criteria) |
| `IMPLEMENTATION-BLUEPRINT.md` | C# interface contracts across all phases |

### Research briefs (may be codified)
| File | Purpose |
|---|---|
| `design-mcp-server.md` | 638-line MCP sidecar architecture (Phase 10+) |
| `research-openclaw-feasibility.md` | Mesh library feasibility (decision made — see Adze.Mesh) |
| `research-solidworks-ai-ecosystem.md` | Competitive landscape |

### Historical (ARCHIVED session 10)

All 9 historical plans listed in the previous version of this index have been moved to `plans/archive/2026-05-15-session-10-curation/` with a full `ARCHIVE_RECEIPT.md` documenting what each covered and where its durable value now lives. Browse the receipt if you need to know whether a past framing was considered or what replaced it.

### `plans/archive/`

- `2026-04-28-codified/` — 21 research briefs codified into code (Sessions 1-5)
- `2026-05-15-session-10-curation/` — 9 plans superseded or codified (Sessions 8-10)
- Both directories have `ARCHIVE_RECEIPT.md` files documenting what was extracted before archival.
- Don't restore from archive without a written reason. Write fresh plans from current understanding instead.

> **Local-only.** Per the `.gitignore` `plans/` rule, the archive directories are local to each contributor's machine. Contributors who clone fresh see only the active plans + this index + the refactor plan + plans/README.md. That's intentional — historical context is per-machine memory.

**Conventions:**
- New active plan ranked above existing plans = explicit decision; record in brain.db.
- "Plan touched in last 30 days" is the heuristic for ACTIVE. Touch a plan when you act on it.
- Research briefs that get codified into code → move to `archive/<date>-codified/` with an `ARCHIVE_RECEIPT.md` noting what was extracted.

---

## §6 — Root-level docs

| File | Role | Gitignored | Lines |
|---|---|:---:|---:|
| `README.md` | Public-facing project description (GitHub front door) | ✗ | 248 |
| `CHANGELOG.md` | Versioned change history (Keep a Changelog format) | ✗ | 216 |
| `SETUP.md` | Build + run + register from source — for contributors | ✗ | 249 |
| `CONTRIBUTING.md` | Contribution guide | ✗ | 147 |
| `CODE_OF_CONDUCT.md` | Contributor Covenant 2.1 reference | ✗ | 29 |
| `SECURITY.md` | Vulnerability reporting | ✗ | 44 |
| `PRIVACY.md` | Privacy policy (zero telemetry today; future opt-in) | ✗ | 112 |
| `LICENSE` | MIT | ✗ | — |
| `CLAUDE.md` | Agent operating manual (this project's "what to do") | ✓ | 452 |
| `AGENTS.md` | GitNexus code-intelligence integration notes | ✓ | 101 |
| `SYSTEM-OVERVIEW.md` | Agent toolbox manual (this project's "what exists") | ✓ | 349 |

**Conventions:** Gitignored files (`CLAUDE.md`, `AGENTS.md`, `SYSTEM-OVERVIEW.md`) are machine-local agent config — they may differ between contributors. Public docs (`README.md`, `CONTRIBUTING.md`, etc.) are the source of truth for external readers.

**Per-directory `CLAUDE.md` files (NOT root)** SHOULD be tracked when they document shared procedures. Force-add when needed. See `install/CLAUDE.md` — that one's tracked because the release procedure is a shared concern.

---

## §7 — `benchmarks/`

| Subdir | Role | Currency |
|---|---|---|
| `grounding/` | Curated grounding corpus + task definitions | Stable, occasional additions |
| `reports/` | 23 JSON reports from broker/grounding/host-validation runs | **Last run 2026-04-19** — about a month stale. Probably fine; report only when reading benchmarks for the first time. |

**Conventions:** Reports follow `*-latest.json` symlink convention for the most-recent of each category. Old reports stay for trend analysis.

**Cross-refs:** Run with `pwsh scripts\setup\run-grounding-benchmarks.ps1` or `run-broker-evals.ps1`.

---

## §8 — Continuity layer (`.ava/`, `sessions/`)

| Path | Role | Tracked |
|---|---|:---:|
| `.ava/brain.db` | SQLite continuity ledger: identity, decisions, notes, sessions, traces | ✗ |
| `.ava/dal.mjs` | CLI for brain.db (`node .ava/dal.mjs <verb>`) | ✗ |
| `.ava/handoffs/` | YAML handoffs written at session close | ✗ |
| `.ava/migrations/` | Schema evolution | ✗ |
| `sessions/` | Curated structured session notes (`session-{N}.md`) | ✗ |
| `.claude/` | Agent toolchain — skills, hooks, agents, memory, settings | ✗ |

**Everything in this layer is gitignored.** It's machine-local agent continuity, not project source. Multiple sessions and machines have independent brain.db instances; cross-machine sync would be a future capability.

**Read-only access patterns:**
```bash
node .ava/dal.mjs status        # health + counts
node .ava/dal.mjs decision list # all active decisions
node .ava/dal.mjs note list     # open work items
node .ava/dal.mjs handoff latest # most recent handoff YAML
```

**Cross-refs:** `SYSTEM-OVERVIEW.md` is the operating manual for the agent surface (skills, hooks, knowledge layers, file layout).

---

## §9 — Outstanding issues this audit surfaced

These came out of the audits and are NOT blockers for current work — they're the punch list for `plans/refactor-and-cleanup.md` to drain.

### Documentation (CLOSED in session 10)
- ✅ **14 plans/ files not linked from root `CLAUDE.md`.** Closed via this index's §5; root CLAUDE.md Docs Map now starts with `project-index.md`.
- ✅ **`plans/README.md` outdated.** Replaced with a thin pointer to this index.
- ✅ **4 src/ projects had no README.md** (`Adze.UiHarness`, `Adze.UI`, `Adze.Index`, `Adze.Manager`). All written session 10.
- ✅ **`schemas/context/` had no README.** Written session 10 — documents the 12 SessionContext slices.

### Code (CLOSED in session 10)
- ✅ **`Adze.UiHarness` AssemblyVersion = 0.1.0.0** drift fixed — now 1.1.0.0 matching the rest.

### Code (deferred to next session — refactor Track B)
- **`register-host-addin.ps1` duplicates registration logic from `install-adze.ps1`.** Two implementations diverged in subtle ways. Consolidate during refactor session.
- **7 orphan scripts in `scripts/setup/`** (zero references anywhere): `check-solidworks-host-readiness.ps1`, `wait-for-solidworks-host-readiness.ps1`, `find-custom-property-fixtures.ps1`, `find-dimension-fixtures.ps1`, `find-reference-graph-fixtures.ps1`, `inspect-annotation-types.ps1`, `enable-github-pages.ps1`, `update-repo-meta.ps1`. Audit per refactor plan Track B — some are one-time setup tools (keep + document), some are likely deletable.

### Process
- **Benchmarks haven't been run in ~30 days.** Not a regression if no breaking changes happened — but verify before the next release tag.
- ✅ **`.gitignore` blanket rules.** Root `CLAUDE.md` tracked session 10 (Decision #30); plans/ files force-added as needed. `sessions/` remains gitignored (local narrative).

---

## How to use this index going forward

- **Looking up where something lives:** start here, jump to the right section.
- **Adding new content:** find the section that owns the concern, follow its conventions, update the inventory in this file.
- **When this file disagrees with code:** the code is right; fix this file. Pin a brain.db note if you don't have time to fix it now.

**Maintenance cadence:** Refresh after every major folder restructure. Light-touch updates when a new project lands or a plan moves to archive. The file is force-tracked (per the `.gitignore` `plans/` rule) so contributors see it on clone.
