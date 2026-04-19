# Linear Adoption — Step-by-Step Checklist

**Created:** 2026-04-19 | **Status:** Active — Kaden to execute
**Scope:** Activating Linear as the private workflow / roadmap layer per [Decision #11](../.ava/brain.db). Expected time: 30–45 minutes end-to-end.

---

## Before you start

- [ ] You have a GitHub account admin-access to `Kadenvh/adze-cad`
- [ ] You have ~45 minutes without interruption (the GitHub sync dance is the slow part)
- [ ] You're on a machine with a browser — everything below is done on `linear.app` via the web

## Step 1 — Create the Linear workspace

1. Go to https://linear.app
2. Click **Sign up** → choose "Sign up with GitHub" (easier than email because of the sync later)
3. Workspace name: `VH Tech` (or `Kaden VanHoecke`, your call — this is the outer container)
4. Workspace URL: `vh-tech` or similar — this becomes `vh-tech.linear.app`
5. Invite team members: **skip for now** (you're solo — can invite later)
6. Choose the **Free plan**. It covers unlimited issues for up to 10 members and is the right fit for solo dev + future small team.

## Step 2 — Create the project

1. In the left sidebar, click **+ New project** (or open the Projects section)
2. Name: `adze-cad`
3. Identifier: `ADZ` (Linear uses this as the prefix for issue IDs, e.g., `ADZ-42`)
4. Description paste:
   > Native AI assistant for SOLIDWORKS. In-process COM add-in, agentic loop, governed writes, multi-provider AI. Public repo at github.com/Kadenvh/adze-cad.
5. Status: **Active**
6. Priority: High (this is your main project; you can lower later if you adopt more)

## Step 3 — Create cycles for the next releases

Cycles are Linear's sprint abstraction. You'll want one per planned release.

1. Go to project settings → **Cycles**
2. Enable cycles (if not already)
3. Cycle length: **2 weeks** is the default; for Adze's pace, **4 weeks** is more realistic (solo dev, part-time)
4. Create these cycles (can be approximate — dates are adjustable):
   - Cycle 1: **v1.0.0 release path** — starts on the date you activate Linear, no fixed end (ends when R1–R5 are done)
   - Cycle 2: **v1.1 — MSI installer + context-menu v2 + more write tools** — placeholder, starts after v1.0 ships
   - Cycle 3: **v1.2 — Phase 10 UI expansion (modeless window, drawing annotation)** — placeholder

## Step 4 — Create epics from the current plan

Linear Epics are higher-level than issues. Map each R-phase to an epic.

Go to **Issues** → **+ New issue** → select "Epic" as the type. For each:

1. **Epic: R1 Crash root cause** — assigned to Cycle 1, priority Urgent
2. **Epic: R2 Interop resilience** — assigned to Cycle 1, priority Urgent, blocked by R1
3. **Epic: R3 Update-lifecycle cooperation** — assigned to Cycle 1, priority High, blocked by R2
4. **Epic: R4 Adze.Manager installer/manager UI** — assigned to Cycle 1, priority High
5. **Epic: R5 Release-gate validation** — assigned to Cycle 1, priority High, blocked by R1–R4
6. **Epic: R6 Partner resubmission** — assigned to Cycle 1, priority Medium, blocked by R5

For each epic, in the description: link to the section in `plans/polish-and-v1-path.md`:
> See `plans/polish-and-v1-path.md#r1--crash-root-cause` for the full done-when criteria.

## Step 5 — Break R1 into concrete issues

R1 is the immediately active work, so seed it with real issues:

- [ ] `ADZ-1`: Analyze CXPD dumps from 2026-04-19 (extract managed stack, identify faulting frame)
- [ ] `ADZ-2`: Diff SW 34.1.0.0140 interop surface vs prior build (`ContextMenu.Attach`, ribbon callbacks)
- [ ] `ADZ-3`: Reproduce crash with context-menu gate toggled off (validate hypothesis)
- [ ] `ADZ-4`: Write `plans/crash-investigation-20260419.md` with findings

All assigned to **Epic R1**, all in Cycle 1, priority Urgent.

## Step 6 — Configure labels

Labels in Linear are project-scoped. Settings → Labels → create:

- `bug` (red) — something broken
- `blocker` (dark red) — blocks the current cycle
- `research` (blue) — investigation work, no code change
- `docs` (light blue) — documentation / plans / CHANGELOG
- `interop` (orange) — SW COM API compatibility
- `installer` (yellow) — install/uninstall/update flow
- `ui` (green) — Task Pane / ribbon / context menu / PMP
- `v1.0-blocker` (dark red) — must be done before v1.0 ships

These intentionally mirror the GitHub label set (see `plans/github-labels.md`) — makes bidirectional sync cleaner.

## Step 7 — Connect GitHub bidirectional sync

This is the most important integration. It makes Linear's private roadmap view and GitHub's public issue surface talk to each other.

1. In Linear: workspace settings → **Integrations** → **GitHub** → Connect
2. Authenticate with GitHub, grant access to `Kadenvh/adze-cad`
3. Choose sync direction: **Bidirectional** (Linear ↔ GitHub)
4. In sync settings:
   - **Auto-create Linear issues from GitHub** — OFF (noise; we curate which ones mirror)
   - **Sync Linear issue comments to GitHub** — ON (useful for visibility)
   - **Sync assignee changes** — ON
   - **Sync status changes** — ON (when Linear issue closes, the GitHub one does too)
   - **Link issues by keyword** — enable: `Fixes ADZ-XX`, `Closes ADZ-XX`, `Relates to ADZ-XX` in GitHub PRs/commits auto-link to Linear

Once connected, to mirror a Linear issue to GitHub: click the issue in Linear → "..." menu → "Create GitHub issue". Only do this for community-visible work.

## Step 8 — Publish the public roadmap

1. Project settings → **Public access**
2. Enable **Public roadmap**
3. Choose what's visible: epics and cycle progress (not individual internal issues)
4. Copy the public URL
5. Paste it into `README.md` under a new section "Roadmap":
   > See the public roadmap: [linear.app/vh-tech/roadmap/adze-cad](https://linear.app/...)

## Step 9 — Mobile access

1. Install the Linear mobile app (iOS / Android)
2. Sign in with the same Google/GitHub/email account you used for the workspace
3. Enable push notifications for:
   - @ mentions
   - Issues assigned to you
   - Comments on issues you created
4. Optionally: enable "review mode" for 15-minute daily triage check-ins

## Step 10 — Document it in brain.db

After everything above is active, record the completion:

```bash
node .ava/dal.mjs note add --category handoff "Linear workspace 'VH Tech' / project 'adze-cad' (identifier ADZ) is live. GitHub bidirectional sync configured. Public roadmap URL in README. Cycle 1 = v1.0 release path with epics R1-R6 seeded from plans/polish-and-v1-path.md. R1 broken into ADZ-1..ADZ-4."
```

And add a decision:

```bash
node .ava/dal.mjs decision add \
  --title "Linear adopted on 2026-04-19 (or your actual date)" \
  --context "Decision #11 planned Linear post-v0.1.2 submission. Submission cancelled by R2026x crash blocker; deferral void." \
  --chosen "Workspace VH Tech / project adze-cad (ADZ). Bidirectional GitHub sync. Public roadmap linked from README. Epics match plans/polish-and-v1-path.md phases." \
  --rationale "Unblocks structured roadmap visibility and gives Kaden mobile/web access. GitHub Issues remains public surface; Linear remains private workflow; brain.db remains session continuity. No concerns collapsed."
```

## What you do NOT need to do

- **Do not** import every brain.db note into Linear — they're different layers (see `plans/documentation-structure.md`)
- **Do not** create a Linear issue for every GitHub issue — only mirror where community visibility or cross-tool tracking matters
- **Do not** pay for Linear's paid tiers — the free tier is more than enough for solo + small-team scope
- **Do not** invite Claude to the workspace — the assistant has no Linear access and no reason to have it; brain.db is the agent-facing layer

## When something surfaces on Linear that you want me to work on

Copy-paste the relevant issue ID + description into a Claude session. I'll use `gitnexus_impact` / `gitnexus_context` / brain.db lookups to ground myself, then we work it together. The agent doesn't need direct Linear access — Linear is the human-facing tracking layer.

## Cross-references

- `plans/documentation-structure.md` — the four-layer stack overview
- `plans/github-labels.md` — the GitHub label set that mirrors Linear's
- `plans/release-process.md` — where Linear cycles fit in the release checklist
- `plans/polish-and-v1-path.md` — the source of truth for what goes into each Epic
