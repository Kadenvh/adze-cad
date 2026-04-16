# Release 4/20/2026 — Partner Resubmission Window

**Target date:** Monday, 2026-04-20
**Version to ship:** v0.1.2 (public beta)
**Parallel action:** SOLIDWORKS Solution Partner Program reapplication
**Author/owner:** Kaden VanHoecke (VH Tech LLC)
**Last updated:** 2026-04-16

---

## Doctrine

This window is about shipping a release candidate whose feature set makes the partner application self-evident. The reviewer should not have to imagine why Adze belongs in the ecosystem — the v0.1.2 download should demonstrate it.

Two operational rules frame the next four days:

1. **Dev days are for dev, not polish.** Do not curate GitHub Release pages, capture screenshots, or record demo GIFs until submission day. Evidence captured during dev goes stale before it ships.
2. **Hold the 4/20 date even if ready earlier.** Release quality is about the last 24 hours of focus, not calendar eagerness. A sharper delivery window produces a sharper submission.

Code freeze: Sunday 2026-04-19 21:00 local. After that, no new features. Docs, release notes, and test runs only.

---

## Why resubmit now (the case to make)

The first application was denied for insufficient web presence. The reapplication should make the reviewer's decision trivial. Specifically:

- **Product exists and runs today.** v0.1.2 downloadable installer, loads on Makers ($48/yr consumer tier), agentic loop live, governed writes live.
- **Competitive window.** Dassault's LEO goes GA July 2026. Adze is live today with architectural coverage LEO does not match (local/offline operation, governed write safety lifecycle, multi-provider AI, deterministic fallback).
- **Public traction surfaces.** Repo public, 666 tests green, CI + CodeQL + Dependabot + branch protection active, MIT-licensed, landing page at https://kadenvh.github.io/adze-cad/.
- **Honest positioning.** Tool count reconciled to 18 (no inflation). "Public beta" framing, not "production-ready." Developer background stated as fact (VH Tech LLC, CSWA, Maker subscriber) without overclaim.

The partner reviewer should read the application and feel the gap Adze fills, not be persuaded into it.

---

## Scope for v0.1.2

### Must ship

| Item | Why it matters |
|------|----------------|
| `fix(mates)` subassembly recursion (873477e, already in main) | Closes the "empty mates" bug on real assemblies. Core demo integrity. |
| Tool count reconciled to 18 (ae50251, already in main) | Factual integrity for application |
| GitHub Pages landing page (7f0987e, 7f77a1f, already in main) | Satisfies Section 3.5 Company Website URL |
| **CommandManager ribbon tab** | Adze visible in SW ribbon permanently. Discoverability. Signals "first-class integration" to reviewer. |
| **Feature-tree context menu injection** | Right-click → "Ask Adze." Enormous UX win. Demonstrates deep SW integration. |
| **CHANGELOG.md v0.1.2 entry** | Release-note hygiene |
| **`PRIVACY.md`** | Section 5.3 of application references a user agreement/privacy policy |

### Stretch (ship if time permits)

| Item | Value |
|------|-------|
| Toast notification on run completion | Passive feedback UX polish |
| 5th quick-action button ("Explain selection") | Rounds out the toolbar |
| Updated screenshots in `solidworks-partner/screenshots/` reflecting new ribbon UI | Will be captured Monday regardless |

### Explicitly out of scope

- PropertyManager Page migration of writes (Phase 10)
- External modeless window
- In-canvas 3D overlays
- Feature tree decorations (icons/badges)
- Drawing annotation layer
- Simulation grounding tools (license-blocked)
- Fusion 360 port (post-partner)

---

## Four-day schedule

### Thursday 4/16 (today)

- [x] Hold Adze v0.1.2 at current main baseline
- [ ] brain.db note cleanup (close 7 historical completion markers)
- [ ] Record new brain.db decisions (this plan, UI expansion ranking, Linear adoption, LangChain rejected)
- [ ] Attempt MCP reconnection (GitNexus, DAL)
- [ ] Write `plans/RELEASE-4-20.md` (this file)
- [ ] Write `plans/phase10-ui-expansion.md`
- [ ] Draft `PRIVACY.md`
- [ ] Draft `CHANGELOG.md` v0.1.2 entry (ship at end)
- [ ] Audit `SECURITY.md` for partner-app adequacy

### Friday 4/17

- [ ] Implement CommandManager ribbon tab
- [ ] Wire ribbon buttons to existing QuickAction COM bridge
- [ ] Unit tests for ribbon registration
- [ ] Run `dependency-audit` skill
- [ ] Strengthen `solidworks-partner/application-draft.md` Section 2.1 language (LEO GA July 2026 competitive note)

### Saturday 4/18

- [ ] Implement feature-tree context menu injection
- [ ] Wire "Ask Adze about {feature}" to prompt composer
- [ ] Optional: 5th quick-action button, toast notifications
- [ ] Smoke test: build-all + run-tests + install + launch SW + load add-in
- [ ] End-to-end rehearsal of v0.1.2 install from packaged zip

### Sunday 4/19 — CODE FREEZE 21:00

- [ ] Final build + tests + broker evals
- [ ] Final documentation pass: CHANGELOG, SETUP.md, README.md
- [ ] Tag v0.1.2 locally (do not push tag)
- [ ] Draft GitHub Release notes body (don't publish)
- [ ] Regenerate PDF from `application-draft.md` (needs Kaden to fill TIN)
- [ ] Full smoke test dry run

### Monday 4/20 — SUBMISSION DAY

- [ ] Push v0.1.2 tag → CI runs
- [ ] Build Release config packaged zip
- [ ] Launch SW + SpdrBot v14 → capture:
  - 3–5 screenshots (empty state, quick actions, tool chips, grounded answer, write confirmation)
  - 1 demo GIF (30–60s, Diagnose flow)
- [ ] Upload fresh screenshots to `solidworks-partner/screenshots/`
- [ ] Publish GitHub Release v0.1.2 with installer zip + notes
- [ ] Fill TIN/EIN in `application-draft.md` and regenerate final PDF
- [ ] Complete online partner application form
- [ ] Send email with PDF attachment to SOLIDWORKS.PartnerProgram@3ds.com
- [ ] Record submission trace in brain.db
- [ ] Close the 4/20 session with dense handoff

---

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| 3DEXPERIENCE Platform blocks SW launch Monday morning | Smoke-test launch Saturday. If platform is down, have backup screenshots from Sunday. |
| CommandManager tab causes registration conflicts on some SW installs | Feature-gate behind env var `SOLIDWORKS_AI_RIBBON=true`, default off. |
| Context menu injection crashes feature tree | Fail-safe: wrap in try/catch, log errors, never propagate to SW host. |
| TIN/EIN not provided | Application blocked regardless of code readiness. Nothing to do but surface and wait. |
| CI fails after tagging | Tag locally first; only push tag after local build passes. |
| Partner reviewer evaluates during the re-review denial window | Not mitigatable. Ship best product possible and trust the process. |

---

## Deferred to Phase 10 (post-partner-submission)

The UI expansion plan (`plans/phase10-ui-expansion.md`) captures all the surfaces we discussed that do not belong in v0.1.2:

- PropertyManager Page migration of writes
- External modeless window for long-form output
- Feature tree decorations (icons/badges)
- Drawing annotation layer
- Toast notifications (stretch only)
- In-canvas 3D overlays (far future)

Once the partner application is submitted, the focus shifts to Phase 10 and whichever of these surfaces the user community requests first.
