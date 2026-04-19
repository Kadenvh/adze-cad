# v0.1.2 Release Notes (Draft)

**Intended target:** GitHub Release body for tag `v0.1.2` on 2026-04-20.
**Status:** Draft — final wording may be tightened before publish.

This file stays in `plans/` until submission day so it does not accidentally become the public landing if someone browses the repo mid-prep.

---

## Proposed title

`Adze v0.1.2 — Ribbon tab, context menus, and the get_mates fix`

## Pre-release flag

Yes (v0.1.x is public beta per CHANGELOG convention).

---

## Release body

```markdown
Public beta v0.1.2. Three new UI surfaces, one real bug fix, and honest accounting.

## New

- **CommandManager ribbon tab.** Persistent "Adze" tab in the SOLIDWORKS ribbon with Ask, Diagnose, Mates, Dimensions, Properties, and Explain buttons. Same prompt composer as the in-pane toolbar. Enable with `SOLIDWORKS_AI_RIBBON=true`.
- **Feature-tree context menu.** Right-click a feature, a component, or the empty canvas for "Ask Adze" / "Diagnose this model" items that route the selection directly into the prompt. Enable with `SOLIDWORKS_AI_CONTEXT_MENU=true`.
- **Tray toast notifications.** Balloon popups on run completion when SOLIDWORKS is not foreground. Suppressed otherwise. Enable with `SOLIDWORKS_AI_TOAST=true`.
- **PropertyManager Page write confirmation (preview).** For `set_dimension_value` only, an opt-in native PMP modal replaces the HTML write card. Proof-of-path for v0.2.0. Enable with `SOLIDWORKS_AI_PMP_WRITES=true`. Other write tools continue to use the HTML card.
- **`PRIVACY.md`** and **`docs/index.html`** — formal privacy policy and a landing page at https://kadenvh.github.io/adze-cad/.

## Fixed

- **`get_mates` now walks subassembly components.** Previously returned empty on assemblies whose mates live inside subassembly components. `SessionContextBuilder.BuildMates` recurses through `AssemblyDoc.GetComponents`, dedupes by path, and respects the 150-mate budget.

## Changed

- **Tool count reconciled to honest 18** (10 read + 1 retrieval + 7 write). The prior "19" double-counted `search_project_files`. Reflected across README, CHANGELOG, docs, and partner-facing materials.
- **GitHub repo polished:** About description updated, `mcp` added to topics (8 total), homepage now points at the new Pages landing page.
- **DAL MCP wrapper** composes the continuity brief from subcommands instead of falling through to CLI help.

## Feature gates (all default off)

| Variable | Enables |
|---|---|
| `SOLIDWORKS_AI_RIBBON` | CommandManager ribbon tab |
| `SOLIDWORKS_AI_CONTEXT_MENU` | Right-click context menus |
| `SOLIDWORKS_AI_TOAST` | Tray balloon notifications |
| `SOLIDWORKS_AI_PMP_WRITES` | Native PropertyManager Page write confirmations (preview, `set_dimension_value` only) |

Pre-existing gates unchanged. See `SETUP.md` for the full list.

## Install

Download `adze-v0.1.2.zip`, extract, double-click `Install Adze.bat`, launch SOLIDWORKS. The Adze Task Pane appears automatically. See `SETUP.md` for provider configuration.

Confirmed working on SOLIDWORKS 2025+ including the $48/yr 3DEXPERIENCE SOLIDWORKS for Makers consumer tier.

## Tests

670 NUnit tests, 664 pass, 6 inconclusive (live-provider smoke tests skip gracefully without an API key). Build is 0 errors, 0 warnings. CI and CodeQL green.

## Compatibility

No breaking changes from v0.1.1. All existing Task Pane behavior is preserved; new surfaces are opt-in via feature gates. Disabling all new gates gives the exact v0.1.1 UX.

## Credits

Adze is built by [VH Tech LLC](https://github.com/Kadenvh) as a free tool for the SOLIDWORKS engineering community.

Full changelog: https://github.com/Kadenvh/adze-cad/blob/main/CHANGELOG.md
```

---

## Assets to attach

- `adze-v0.1.2.zip` — from `install\package-release.ps1`
- Optionally a demo GIF if captured Monday morning

## Publication checklist (Monday)

1. [ ] Tag `v0.1.2` locally with `git tag -a v0.1.2 -m "Adze v0.1.2"`
2. [ ] Push the tag: `git push origin v0.1.2`
3. [ ] Build release config: `pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks`
4. [ ] Package: `pwsh -NoProfile -File install\package-release.ps1`
5. [ ] Verify `install\dist\adze-v0.1.2.zip` exists
6. [ ] Capture a demo GIF via windows-mcp (Diagnose flow on SpdrBot)
7. [ ] Create the release via GitHub UI OR via API: `gh release create v0.1.2 ...`
8. [ ] Paste the body from above (tightened if needed)
9. [ ] Mark as pre-release
10. [ ] Attach the zip and GIF
11. [ ] Publish
12. [ ] Verify the release page renders correctly — Pages homepage link works, topics visible, assets downloadable

---

## Why this body is tuned this way

- **Lead with what changed, not narrative.** Partner reviewers scan; they do not read.
- **Every feature has its enable flag.** Shows the opt-in discipline and prevents "why doesn't the ribbon show up" confusion.
- **Honest scope for PMP.** Calling it a proof-of-path avoids the feature feeling half-built when it deliberately is.
- **Explicit compatibility statement.** Relevant to reviewers assessing maturity.
- **Maker compatibility mentioned inline.** Differentiator. Costs one sentence.
- **No emojis, no hype words.** Matches the product voice established in `portfolio.description`.
