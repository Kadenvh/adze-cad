# Cleanup — DAL Maintenance & Knowledge Hygiene

You are performing a comprehensive health check and cleanup of the project's persistent state (brain.db). This is the deep clean — not the daily tidying of session closeout, but a holistic audit across sessions.

---

## 1. PREREQUISITES

Verify the DAL is available:

```bash
node .ava/dal.mjs status
```

If brain.db doesn't exist or status fails, stop and report. Cleanup requires an active DAL.

---

## 2. FACT HEALTH

### 2a. Run the audit

```bash
node .ava/dal.mjs fact audit
```

Report findings:
- **Unclassified facts** (permanence = 'standard') — these need classification. Present each with a recommended permanence tier based on content.
- **Stale facts** (>90 days without confirmation, not immutable/persistent) — present each and recommend: confirm, reclassify, or delete.
- **Expired ephemeral facts** (3+ sessions since last confirm) — recommend pruning.

### 2b. Classify unclassified facts

For each unclassified fact, apply the heuristic:

| Content Pattern | Permanence |
|----------------|-----------|
| Mission, vision, identity, founding principles | `immutable` |
| Architecture patterns, tech stack, service ports | `persistent` |
| Current implementation patterns, recent learnings | `standard` (keep as-is) |
| Debug findings, temp workarounds, session-specific | `ephemeral` |

Present your recommendations as a table and ask the user to confirm before applying.

Use: `node .ava/dal.mjs fact set "<key>" --permanence <tier> --value "<existing value>"`

### 2c. Prune expired ephemeral facts

```bash
node .ava/dal.mjs fact prune --dry-run
```

Show what would be deleted. If the user approves:

```bash
node .ava/dal.mjs fact prune --execute
```

### 2d. Verify

```bash
node .ava/dal.mjs fact audit
```

Target: 0 unclassified, 0 stale, 0 expired ephemeral.

---

## 3. DECISION HEALTH

Review all active decisions:

```bash
node .ava/dal.mjs decision list
```

For each active decision, evaluate:
- **Still relevant?** If the project has moved past it, recommend marking as superseded.
- **Correctly scoped?** If it covers too much or too little, flag it.
- **Missing decisions?** If you notice implicit architectural choices without a recorded decision, suggest adding one.

Present findings as a table: Decision | Status | Recommendation.

---

## 4. SESSION HEALTH

Check for interrupted or crashed sessions:

```bash
node .ava/dal.mjs session list
```

For any session with `exit_reason` of `interrupted` or `crashed`:
- Check if it's recent (last 7 days) — may need recovery investigation.
- Check if it's old (>7 days) — just note it as historical.
- Report the pattern: are sessions closing cleanly? If not, what might be causing interruptions?

---

## 5. NOTE HYGIENE

```bash
node .ava/dal.mjs note list
node .ava/dal.mjs note counts
```

For open notes:
- Flag completed notes that should be cleared.
- Flag notes older than 30 days — still relevant?
- Report by category: how many improvements, issues, bugs, ideas are open?

---

## 6. OVERALL HEALTH REPORT

Compile a final report:

```
DAL Health Report
=================
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

Actions taken:
  - Facts classified: {N}
  - Facts pruned: {N}
  - Decisions updated: {N}
  - Notes cleared: {N}
```

---

## 7. RULES

- **Always dry-run first.** Never delete facts or prune without showing the user what will be affected.
- **Present recommendations, don't auto-apply.** The user confirms every classification change and every deletion.
- **Be honest about uncertainty.** If you're not sure whether a fact is persistent or standard, say so and let the user decide.
- **Report everything.** Even if the DB is healthy, say so — "clean bill of health" is valuable information.
