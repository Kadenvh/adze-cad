# GitHub Labels — Recommended Set

**Created:** 2026-04-19 | **Status:** Active
**Scope:** The label set to apply on `https://github.com/Kadenvh/adze-cad/issues` and pull requests. Labels are the primary triage signal when an issue first lands.

---

## How to apply

Labels are configured at `https://github.com/Kadenvh/adze-cad/labels`. Existing GitHub defaults (`bug`, `documentation`, `enhancement`, `good first issue`, `help wanted`, `invalid`, `question`, `wontfix`, `duplicate`) are preserved — the additions below extend them.

For each label in the table: click "New label", paste the name, paste the color (hex without `#`), paste the description, save.

## Core labels (already present as GitHub defaults)

Keep these. Defaults are fine.

| Name | Color | Purpose |
|------|-------|---------|
| `bug` | `d73a4a` | Something is broken |
| `enhancement` | `a2eeef` | New feature or improvement |
| `documentation` | `0075ca` | README, SETUP, inline docs, plans/ updates |
| `good first issue` | `7057ff` | Good for new contributors |
| `help wanted` | `008672` | Extra attention is needed |
| `wontfix` | `ffffff` | This will not be worked on |
| `duplicate` | `cfd3d7` | This issue already exists |
| `invalid` | `e4e669` | Not a real issue |
| `question` | `d876e3` | Further information is requested |

## Adze-specific labels (add these)

### Category labels — what kind of issue is this

| Name | Color | Description |
|------|-------|-------------|
| `crash` | `b60205` | SOLIDWORKS or Adze crashed / hung |
| `installer` | `fbca04` | Issue with install-adze.ps1, .bat, uninstall, or Adze.Manager |
| `update-lifecycle` | `fbca04` | Issue that surfaces during or after SW / 3DEXPERIENCE update |
| `interop` | `d93f0b` | SOLIDWORKS COM API compatibility, version-specific behavior |
| `ai-provider` | `5319e7` | OpenAI / Anthropic / Ollama / LM Studio / OpenRouter integration |
| `ui` | `c2e0c6` | Task Pane, ribbon, context menu, PropertyManager Page |
| `tool` | `1d76db` | Grounding or write-tool behavior (get_mates, set_dimension_value, etc.) |
| `docs` | `0075ca` | Synonym for `documentation` when narrower scope is useful |
| `privacy-security` | `b60205` | Privacy policy, DPAPI, data handling, telemetry |

### Priority / severity labels

| Name | Color | Description |
|------|-------|-------------|
| `v1.0-blocker` | `b60205` | Must be fixed before v1.0 ships |
| `v1.x-followup` | `fbca04` | Known issue, scheduled for a v1.x patch |
| `future` | `cccccc` | Good idea, not currently scheduled |
| `needs-triage` | `d4c5f9` | Newly opened, awaiting maintainer review |
| `needs-info` | `d876e3` | Waiting on reporter for reproduction details |
| `in-progress` | `0e8a16` | Actively being worked on |

### SOLIDWORKS version labels

Apply when an issue is specific to a SW build. One per issue max.

| Name | Color | Description |
|------|-------|-------------|
| `sw-makers` | `e99695` | SOLIDWORKS for Makers tier |
| `sw-2025` | `fef2c0` | SOLIDWORKS 2025 SPx |
| `sw-2026` | `bfe5bf` | SOLIDWORKS 2026 SPx |
| `sw-3dx-r2026x` | `bfdadc` | 3DEXPERIENCE SOLIDWORKS R2026x |

### Community labels

| Name | Color | Description |
|------|-------|-------------|
| `partner-feedback` | `5319e7` | Feedback from the Dassault Solution Partner review |
| `maker-community` | `7057ff` | Signal from r/SolidWorks or Maker-tier user community |

## Saved filters to create (GitHub UI)

After applying the labels, create these bookmarked filters at the top of the Issues page:

- `is:open label:v1.0-blocker` → all things blocking the next release
- `is:open label:crash` → all active crash reports
- `is:open label:needs-triage` → inbox for new reports
- `is:open label:help wanted` → surface work open to contributions
- `is:open -label:needs-info` → everything actively actionable (not waiting on reporter)

## Labels NOT to create

These are anti-patterns from past projects — do not create:

- Labels that duplicate `bug` / `enhancement` (e.g., `defect`, `new-feature`) — redundant
- Labels tied to internal person names / initials — belongs in assignees
- Labels for "I want to watch this" — use the GitHub subscribe feature instead
- Labels with sprint numbers / dates — use Linear cycles for that, not GitHub labels

## Cross-references

- `plans/documentation-structure.md` — why GitHub Issues is its own layer
- `plans/linear-adoption-checklist.md` — Linear label set, configured separately
- `.github/ISSUE_TEMPLATE/` — issue templates auto-apply their own labels via frontmatter
