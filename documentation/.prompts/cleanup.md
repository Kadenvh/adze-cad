# Cleanup — DAL Reconciliation & Knowledge Hygiene

You are performing a comprehensive reconciliation of the project's persistent state (brain.db) against its documentation and codebase. Whether brain.db is empty (first run / hydration) or full (ongoing maintenance), the job is the same: **brain.db must accurately reflect project reality.**

brain.db is the primary context source for every future session. The session-context hook injects brain.db contents at startup — if brain.db is incomplete, the next agent starts blind. **Every cleanup must leave brain.db in a state where the next agent can be productive without reading any docs.**

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

**Detect mode:** If facts = 0 AND sessions = 0, this is a **first-run hydration** — brain.db is deployed but unpopulated. Be comprehensive — extract everything. If facts > 0, this is **ongoing maintenance** — focus on drift, gaps, and completeness.

---

## 2. REQUIRED FACT EXTRACTION

Every project's brain.db MUST have these facts. Extract them from the sources listed. If a fact exists but is wrong, update it. If missing, insert it.

### From CLAUDE.md (read the entire file):

| Fact Key | What to Extract | Permanence |
|----------|----------------|------------|
| `project.name` | Project name from header | `persistent` |
| `project.version` | Version from header | `standard` |
| `project.status` | Status line from header | `standard` |
| `project.identity` | First paragraph — what the project IS | `persistent` |
| `tech.stack` | Primary language/framework/runtime | `persistent` |
| `tech.build` | Build/run commands | `persistent` |
| `tech.test` | Test commands and framework | `persistent` |
| `project.structure` | Key directories and their purposes | `persistent` |
| `rules.critical` | DO NOT rules (summarized) | `persistent` |

### From PROJECT_ROADMAP.md:

| Fact Key | What to Extract | Permanence |
|----------|----------------|------------|
| `project.vision` | Vision/goals — why this project exists | `immutable` |
| `arch.*` | Each architectural decision (AD-*) → record as **decision**, not fact | — |
| `tech.architecture` | High-level architecture description | `persistent` |

### From IMPLEMENTATION_PLAN.md:

| Fact Key | What to Extract | Permanence |
|----------|----------------|------------|
| `current.phase` | Current development phase | `standard` |
| `current.blockers` | Active blockers (if any) | `ephemeral` |
| `current.handoff` | Handoff notes for next session | `ephemeral` |

### From other docs (SETUP.md, BUILD_SPEC.md, README.md, etc.):

| Fact Key | What to Extract | Permanence |
|----------|----------------|------------|
| `env.*` | Environment setup, dependencies, prerequisites | `persistent` |
| `deploy.*` | Deployment targets, procedures | `persistent` |

### Architecture Decisions → Decision Records

Every AD-* entry in PROJECT_ROADMAP.md and every significant architectural choice should be a decision record:

```bash
node .ava/dal.mjs decision add "Decision title" --context "Why it came up" --chosen "What was chosen" --rationale "Why"
```

**Do not skip this.** Decisions are the most valuable brain.db content — they prevent the next agent from relitigating settled questions.

---

## 3. COMPARE AND RECONCILE

After extraction, compare systematically:

### 3a. brain.db → docs (is brain.db accurate?)
For every fact in brain.db, verify it matches current documentation:
- Version facts match CLAUDE.md header
- Tech stack facts match actual dependencies
- Architecture facts match ROADMAP
- Ephemeral facts are still relevant

### 3b. docs → brain.db (is brain.db complete?)
For every required fact in the schema above, verify brain.db has it:
- Missing required facts = **FAIL** (insert them)
- All required facts present = PASS

### 3c. Codebase validation (lightweight)
Spot-check that facts match reality:
- File structure in `project.structure` fact matches actual directories
- Tech stack matches package.json / Cargo.toml / *.csproj / etc.
- Build commands in `tech.build` actually work

### 3d. Archive check
Read `documentation/archive/` if it exists. Historical context worth preserving as facts should be extracted. Archive content is valid — it was moved for size management, not because it stopped being true.

**Present all proposed inserts, updates, and removals as a table. Wait for user confirmation before applying.**

---

## 4. FACT HEALTH

### 4a. Audit

```bash
node .ava/dal.mjs fact audit
```

- **Unclassified facts** → classify using the permanence heuristics below
- **Stale facts** (>90 days, not immutable/persistent) → confirm or remove
- **Expired ephemeral** (3+ sessions since confirm) → prune

### 4b. Permanence classification

| Content Pattern | Permanence |
|----------------|-----------|
| Mission, vision, identity, founding principles | `immutable` |
| Architecture, tech stack, build commands, conventions, ports | `persistent` |
| Current state, version, recent patterns, working knowledge | `standard` |
| Session-specific findings, temp workarounds, current blockers | `ephemeral` |

### 4c. Prune

```bash
node .ava/dal.mjs fact prune --dry-run
```

Show results. If approved: `node .ava/dal.mjs fact prune --execute`

### 4d. Verify

```bash
node .ava/dal.mjs fact audit
```

Target: 0 unclassified, 0 stale, 0 expired.

---

## 5. DECISION HEALTH

```bash
node .ava/dal.mjs decision list
```

- **Still relevant?** Superseded decisions should be marked.
- **Correctly scoped?** Too broad or too narrow → flag.
- **Missing?** Documentation has architectural choices without matching decisions → add them.

---

## 6. SESSION HEALTH

```bash
node .ava/dal.mjs session list
```

- Interrupted/crashed sessions in last 7 days → investigate
- Older interrupted sessions → note as historical
- Pattern of unclean exits → flag as systemic issue

---

## 7. NOTE HYGIENE

```bash
node .ava/dal.mjs note list
node .ava/dal.mjs note counts
```

- Completed notes → clear
- Notes older than 30 days → still relevant?
- Report by category

---

## 8. COVERAGE REPORT

This is the critical output. Not just "is brain.db healthy" but "is brain.db COMPLETE enough to be useful."

```
DAL Reconciliation Report
=========================
Mode:              {first-run hydration | ongoing maintenance}
Schema version:    v{N}
Integrity:         {ok|FAILED}

COVERAGE (required facts):
  project.name:      {present|MISSING}
  project.version:   {present|MISSING}
  project.identity:  {present|MISSING}
  tech.stack:        {present|MISSING}
  tech.build:        {present|MISSING}
  project.vision:    {present|MISSING}
  Coverage:          {N}/{total} required facts present

Facts:             {total} total
  - immutable:     {N}
  - persistent:    {N}
  - standard:      {N}
  - ephemeral:     {N}

Decisions:         {total} ({active} active)
Sessions:          {total}
Notes:             {open} open

Reconciliation:
  - Facts inserted: {N}
  - Facts updated: {N}
  - Facts removed: {N}
  - Decisions added: {N}
  - Contradictions found: {N}
  - Gaps filled: {N}

VERDICT: {PASS — brain.db is complete and accurate |
          FAIL — {N} required facts missing, {N} contradictions}
```

**A cleanup that reports PASS with missing required facts is a failed cleanup.**

---

## 9. RULES

- **Always dry-run first.** Never delete without showing the user what will be affected.
- **Present recommendations, don't auto-apply.** User confirms inserts, classifications, and deletions.
- **Docs are truth, brain.db is the cache.** When they contradict, docs win.
- **Don't invent facts.** Only insert knowledge explicitly stated in docs or verifiable in codebase.
- **Coverage is mandatory.** A brain.db without the required facts is not "clean" — it's incomplete.
- **Be honest.** If the brain.db is a mess, say so. "Clean bill of health" requires actual coverage.
