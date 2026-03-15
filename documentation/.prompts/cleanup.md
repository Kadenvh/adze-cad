# Cleanup — DAL Reconciliation & Knowledge Hygiene

You are performing a comprehensive reconciliation of the project's persistent state (brain.db) against its documentation and codebase. Whether brain.db is empty (first run / hydration) or full (ongoing maintenance), the job is the same: **brain.db must accurately reflect project reality.**

---

## 1. PREREQUISITES

Verify the DAL is available:

```bash
node .ava/dal.mjs status
```

If brain.db doesn't exist or status fails, stop and report. Cleanup requires an active DAL.

Check current state:

```bash
node .ava/dal.mjs fact audit
node .ava/dal.mjs fact list
node .ava/dal.mjs decision list
node .ava/dal.mjs session list
```

**Detect mode:** If facts = 0 AND sessions = 0, this is a **first-run hydration** — brain.db is deployed but unpopulated. Proceed to Section 2 (doc-aware reconciliation) and be comprehensive. If facts > 0, this is **ongoing maintenance** — focus on drift and gaps.

---

## 2. DOC-AWARE RECONCILIATION

This is the core of cleanup. Read the project's documentation and reconcile with brain.db.

### 2a. Read project documentation

Read these files in order (skip any that don't exist):

1. **CLAUDE.md** (project root) — tech stack, critical rules, commands, file structure
2. **documentation/PROJECT_ROADMAP.md** — architecture decisions, version history, vision
3. **documentation/IMPLEMENTATION_PLAN.md** — current tasks, handoff notes, blockers
4. Any additional docs: `SETUP.md`, `BUILD_SPEC.md`, `README.md`, etc.
5. **documentation/archive/** — historical context worth preserving

### 2b. Extract facts from documentation

For each document, identify knowledge that should be a brain.db fact:

| Doc Source | What to Extract | Typical Permanence |
|------------|----------------|-------------------|
| CLAUDE.md | Project identity, tech stack, critical commands, conventions | `persistent` |
| CLAUDE.md | Version number, current status | `standard` |
| PROJECT_ROADMAP.md | Vision, mission, founding principles | `immutable` |
| PROJECT_ROADMAP.md | Tech stack choices, deployment architecture | `persistent` |
| IMPLEMENTATION_PLAN.md | Current blockers, active work | `ephemeral` |
| IMPLEMENTATION_PLAN.md | Handoff context for next session | `ephemeral` |
| SETUP.md / BUILD_SPEC.md | Build commands, dependencies, environment | `persistent` |

Architecture decisions (AD-* entries in ROADMAP, or implicit choices) should be recorded as decisions, not facts:

```bash
node .ava/dal.mjs decision add "Decision title" --context "Why it came up" --chosen "What was chosen" --rationale "Why"
```

### 2c. Compare and reconcile

For each extracted item:
- **If brain.db already has it:** verify the value matches. If it contradicts, flag it.
- **If brain.db is missing it:** insert with appropriate permanence.
- **If brain.db has something docs don't mention:** verify it's still true. If outdated, flag for removal.

### 2d. Codebase validation (lightweight)

Scan the project structure to validate key facts:
- File structure matches what CLAUDE.md describes
- Tech stack facts match actual dependencies (package.json, Cargo.toml, *.csproj, etc.)
- Key directories mentioned in docs actually exist

Don't deep-read the codebase — just validate that documented facts match reality.

**Present all proposed inserts, updates, and flags as a table. Wait for user confirmation before applying.**

---

## 3. FACT HEALTH

### 3a. Run the audit

```bash
node .ava/dal.mjs fact audit
```

Report findings:
- **Unclassified facts** (permanence = 'standard' that should be reclassified) — present each with a recommended permanence tier.
- **Stale facts** (>90 days without confirmation, not immutable/persistent) — recommend: confirm, reclassify, or delete.
- **Expired ephemeral facts** (3+ sessions since last confirm) — recommend pruning.

### 3b. Classify unclassified facts

| Content Pattern | Permanence |
|----------------|-----------|
| Mission, vision, identity, founding principles | `immutable` |
| Architecture patterns, tech stack, service ports, conventions | `persistent` |
| Current implementation patterns, recent learnings | `standard` (keep as-is) |
| Debug findings, temp workarounds, session-specific context | `ephemeral` |

Present recommendations as a table. Ask user to confirm before applying.

Use: `node .ava/dal.mjs fact set "<key>" --permanence <tier> --value "<existing value>"`

### 3c. Prune expired ephemeral facts

```bash
node .ava/dal.mjs fact prune --dry-run
```

Show what would be deleted. If the user approves:

```bash
node .ava/dal.mjs fact prune --execute
```

### 3d. Verify

```bash
node .ava/dal.mjs fact audit
```

Target: 0 unclassified, 0 stale, 0 expired ephemeral.

---

## 4. DECISION HEALTH

Review all active decisions:

```bash
node .ava/dal.mjs decision list
```

For each active decision, evaluate:
- **Still relevant?** If the project has moved past it, recommend marking as superseded.
- **Correctly scoped?** If it covers too much or too little, flag it.
- **Missing decisions?** If documentation records architectural choices without a matching brain.db decision, suggest adding one.

Present findings as a table: Decision | Status | Recommendation.

---

## 5. SESSION HEALTH

Check for interrupted or crashed sessions:

```bash
node .ava/dal.mjs session list
```

For any session with `exit_reason` of `interrupted` or `crashed`:
- Check if it's recent (last 7 days) — may need recovery investigation.
- Check if it's old (>7 days) — just note it as historical.
- Report the pattern: are sessions closing cleanly? If not, what might be causing interruptions?

---

## 6. NOTE HYGIENE

```bash
node .ava/dal.mjs note list
node .ava/dal.mjs note counts
```

For open notes:
- Flag completed notes that should be cleared.
- Flag notes older than 30 days — still relevant?
- Report by category: how many improvements, issues, bugs, ideas are open?

---

## 7. OVERALL HEALTH REPORT

Compile a final report:

```
DAL Health Report
=================
Mode:              {first-run hydration | ongoing maintenance}
Schema version:    v{N}
DB size:           {N} KB
Integrity:         {ok|FAILED}

Facts:             {total} total
  - immutable:     {N}
  - persistent:    {N}
  - standard:      {N}
  - ephemeral:     {N}
  - stale:         {N} (>90 days)

Decisions:         {total} ({active} active, {superseded} superseded)
Sessions:          {total} ({normal} normal, {interrupted} interrupted)
Notes:             {open} open across {tabs} contexts
Tasks:             {active} active, {blocked} blocked

Reconciliation:
  - Facts inserted from docs: {N}
  - Facts updated: {N}
  - Decisions added from docs: {N}
  - Contradictions flagged: {N}
  - Gaps detected: {N}

Actions taken:
  - Facts classified: {N}
  - Facts pruned: {N}
  - Decisions updated: {N}
  - Notes cleared: {N}
```

---

## 8. RULES

- **Always dry-run first.** Never delete facts or prune without showing the user what will be affected.
- **Present recommendations, don't auto-apply.** The user confirms every classification change and every deletion.
- **Be honest about uncertainty.** If you're not sure whether a fact is persistent or standard, say so and let the user decide.
- **Report everything.** Even if the DB is healthy, say so — "clean bill of health" is valuable information.
- **Docs are truth, brain.db is the cache.** When docs and brain.db contradict, docs win. Update brain.db to match.
- **Don't invent facts.** Only insert knowledge that's explicitly stated in documentation or verifiable in the codebase. Never infer or speculate.
