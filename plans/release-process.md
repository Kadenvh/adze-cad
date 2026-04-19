# Release Process

**Created:** 2026-04-19 | **Status:** Active
**Scope:** The repeatable procedure for cutting an Adze release — from "I want to tag vN.N.N" through "tag pushed, release published, session closed, docs consistent."

---

## When to cut a release

A release is warranted when:

- A new user-visible feature has landed and been validated end-to-end
- A security or crash fix needs to reach users
- A cycle closes in Linear with shippable work

A release is NOT warranted for:

- In-progress feature work (ship when stable, not when incrementally built)
- Doc-only changes (push to `main` and let them flow)
- Cosmetic / internal refactors with no user-visible effect

## Version numbering

Semantic versioning, `MAJOR.MINOR.PATCH`:

- `MAJOR` — user-facing breaking changes, major architectural shifts (Phase 10 UI expansion, sidecar migration)
- `MINOR` — new features, new AI providers, new tool classes — no break on existing users
- `PATCH` — bug fixes, small polish, release follow-ups

`AssemblyVersion` in source matches the target of the in-progress release; it may be ahead of the last shipped tag. The last shipped tag is the authoritative "released" version (tracked in `identity.project.version`).

## Pre-release checklist (gates — any failure blocks the release)

Ordered. Do not skip.

### Phase A — Clean state
- [ ] `git status` — working tree clean, on `main`, up to date with `origin/main`
- [ ] All relevant Linear issues in the current cycle are closed
- [ ] Active plans in `plans/` reflect current state (no stale TODOs in the active plan)
- [ ] brain.db open notes reviewed — no unresolved blockers

### Phase B — Source version bump
- [ ] `AssemblyVersion` and `AssemblyFileVersion` set to the target version in all 6 `AssemblyInfo.cs` files
- [ ] `<AssemblyVersion>`, `<FileVersion>`, `<Version>`, `<InformationalVersion>` set in all 6 `.csproj` files
- [ ] `identity.project.version` set via DAL: `node .ava/dal.mjs identity set "project.version" --value "X.Y.Z"`
- [ ] `CLAUDE.md` header updated
- [ ] `README.md` status line + status section updated
- [ ] `docs/index.html` version badges + download button updated
- [ ] `CHANGELOG.md` `[Unreleased]` renamed to `[X.Y.Z] — YYYY-MM-DD`

### Phase C — Build + test gates
- [ ] Delete `bin/` and `obj/` to force clean rebuild
- [ ] `pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks` — 0 errors, 0 warnings
- [ ] `pwsh -NoProfile -File scripts\setup\run-tests.ps1` — all unit tests pass (6 inconclusive live-provider smoke are OK without a key)
- [ ] `pwsh -NoProfile -File scripts\setup\run-broker-evals.ps1` — 12/12 pass
- [ ] `pwsh -NoProfile -File scripts\setup\run-grounding-benchmarks.ps1` — green against curated corpus

### Phase D — Integration gates (post-R2–R4)
These are the R5 release-gate items from `plans/polish-and-v1-path.md`. They are MANDATORY from v1.0 onward.
- [ ] Fresh install via `Adze.Manager.exe` on a clean machine or VM — SW launches, Task Pane visible, Settings panel visible
- [ ] Apply a real 3DEXPERIENCE update with Adze installed — updater completes without errors
- [ ] Post-update relaunch SW — Adze either loads cleanly OR refuses with a friendly banner (no crash)
- [ ] Pre-update "Eject" flow works — Manager unhooks, updater runs, Manager re-enables after
- [ ] Uninstall via `Adze.Manager.exe` — clean COM unreg, clean DLL removal, SW launches again without Adze
- [ ] Live smoke test on SpdrBot v14 covering TESTING-MANUAL-USER.md sections 3–8

### Phase E — Artifacts
- [ ] `pwsh -NoProfile -File install\package-release.ps1` — produces `install\adze-vX.Y.Z.0.zip` at the target version
- [ ] Unzip and inspect — DLLs present, `install-adze.ps1` + `install-adze.bat` + uninstallers + `Adze.Manager.exe` all bundled
- [ ] Install from the produced zip on a test environment (separate from the dev machine) — verify end-user flow

### Phase F — Tag + notes
- [ ] Write release notes in `solidworks-partner/RELEASE-NOTES-vX.Y.Z.md` (private draft, since `solidworks-partner/` is gitignored)
- [ ] `git tag -a vX.Y.Z -m "Adze vX.Y.Z"` locally (do NOT push yet)
- [ ] Verify tag points to the right commit: `git log -1 vX.Y.Z`

## Release checklist (one-way actions)

### Phase G — Publication
- [ ] `git push origin main`
- [ ] `git push origin vX.Y.Z`
- [ ] Create GitHub Release from the tag:
  - Title: `Adze vX.Y.Z`
  - Body: paste from `solidworks-partner/RELEASE-NOTES-vX.Y.Z.md`
  - Attach `install\adze-vX.Y.Z.0.zip`
- [ ] Wait for CI checks to go green on the tag (CodeQL, builds)

### Phase H — Post-publish
- [ ] Close the current Linear cycle; open the next cycle's epics
- [ ] Update `CHANGELOG.md` — add a new `[Unreleased]` header above `[X.Y.Z]` for the next cycle
- [ ] `node .ava/dal.mjs identity set "project.version" --value "X.Y.Z"` (confirms last-shipped)
- [ ] `node .ava/dal.mjs note add --category handoff "vX.Y.Z published on YYYY-MM-DD. Next cycle starts: ..."`
- [ ] Post in any relevant community surfaces (partner channels, Reddit r/SolidWorks, LinkedIn, etc.) if this is a marketing-worthy release

### Phase I — Session close
- [ ] `/session-closeout` skill runs (writes session note, generates handoff YAML, emits health beacon, commits if needed)

## Rollback / hotfix

If a released version reveals a critical bug:

1. Create a hotfix branch from the release tag: `git checkout -b hotfix/vX.Y.Z+1 vX.Y.Z`
2. Fix the issue in isolation (no new features)
3. Run Phases B–F on the hotfix branch
4. Merge hotfix to `main`, tag as `vX.Y.Z+1`, publish
5. Do NOT delete or overwrite the original tag — the bug is part of release history

## Notes for Adze specifically

- **Partner submission alignment** — when a release is tied to a partner application resubmission, the Phase G (publication) action must come BEFORE submitting the partner form so the URL reviewers click lands on the new release, not the old one.
- **3DX update compatibility** — from R5 forward, no release ships without confirmed survival of an R2026x-style desktop update. The Phase D integration gates enforce this.
- **Fresh-clone test** — Phase E's "install on a test environment" requirement is specifically a fresh-clone or fresh-VM scenario. Installing on the dev machine is necessary but not sufficient — the dev machine has too much state to prove the end-user experience.

## Cross-references

- `plans/polish-and-v1-path.md` — current release path (v1.0.0), phases R1–R6
- `plans/documentation-structure.md` — where release information lives (this doc + CHANGELOG + GitHub Releases)
- `plans/TESTING-MANUAL-USER.md` + `TESTING-PROCEDURE-AUTOMATED.md` — the smoke tests invoked in Phase D
- `CHANGELOG.md` — the official per-version changelog
