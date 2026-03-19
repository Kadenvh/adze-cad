# Session Closeout Prompt

Perform end-of-session documentation updates and synchronize project prompts with current state.

---

## PART A: CAPTURE SESSION STATE

### Step 1: Inventory Changes (Do This First)

Before touching any file, explicitly enumerate what happened:

**Features/Implementations:**
- (list each with one-line description)

**Bug Fixes:**
- (list each with root cause if known)

**Files Modified:**
- `path/to/file` — (what changed)

**API/Interface Changes:**
- (new/modified endpoints or functions)

**Schema/Data Changes:**
- (database or data structure changes)

**Decisions Made:**
- (architectural or design choices — include ANY judgment calls or deviations from the original plan, no matter how small)

**Issues Discovered (Not Fixed):**
- (for next session's handoff)

**New Directories Created:**
- (list any new major folders added this session — these will need README.md files)

---

### Step 2: Determine Version Increment

| This Session Had... | Increment | Example |
|---------------------|-----------|---------|
| Only bug fixes, no new features | Patch | 1.0.0 -> 1.0.1 |
| New features or endpoints | Minor | 1.0.x -> 1.1.0 |
| Breaking changes, major refactors | Major | 1.x.x -> 2.0.0 |

**New Version:** ___________

---

### Step 3: Extract Implicit Knowledge

Step 1 captured what you *consciously* did. This step scans the full conversation for knowledge you didn't think to list — the implicit learnings that would otherwise be lost.

**Review the conversation and extract:**

| Look For | Example | Permanence |
|----------|---------|------------|
| Conventions established | "We agreed to always use slugs for IDs" | persistent |
| Patterns discovered | "The API rejects requests without header X" | persistent |
| Gotchas / failure modes | "SQLite can't ALTER CHECK constraints — must rebuild table" | standard |
| Environment specifics | "Works on Linux but PowerShell needs different quoting" | persistent |
| Tool/command discoveries | "Use `dal.mjs render` not manual doc edits" | standard |
| Architecture clarified | "Component A talks to B via C, not directly" | persistent |
| Performance insights | "FTS5 is fast enough — no need for sqlite-vss" | standard |
| Configuration learned | "SSH key id_ed25519_zoe works for both Frank and Zoe" | persistent |

**Quality filter — only extract facts that:**
- Would prevent a future agent from repeating work or making the same mistake
- Can't be derived by reading the current codebase (if you can `grep` for it, don't store it)
- Are durable enough to matter next session (don't store dead-end debugging attempts)
- Aren't already captured in Step 1's explicit inventory

**For each extracted fact, produce:**
```
Key: <domain.descriptive_key>
Value: <concise statement of the knowledge>
Permanence: <tier>
```

List these alongside the explicit Step 1 inventory. They all get recorded in Part A-2 below.

**If nothing was extracted:** That's fine — not every session produces implicit knowledge. But sessions involving debugging, configuration, cross-system work, or architecture discussions almost always do. If you found nothing, scan once more for gotchas and failure modes.

---

## PART A-2: RECORD SESSION KNOWLEDGE TO brain.db (Do This BEFORE Updating Docs)

> **If `.ava/brain.db` does NOT exist, skip to Part B.** This section requires an active DAL.

**This is the most important step for session continuity.** The facts and decisions you record here are what the next agent will see at session start. If you skip this, the next session starts blind.

### Record facts from this session

For each item in your Part A inventory, record durable knowledge:

```bash
# Record each fact with appropriate permanence
node .ava/dal.mjs fact set "<key>" --value "<value>" --permanence <tier>
```

**What to record as facts:**
- New tech stack choices or changes → `persistent`
- New conventions or patterns established → `persistent`
- Version changes → `standard` (update `project.version`)
- Current blockers or handoff context → `ephemeral`
- Bug root causes worth remembering → `standard`

**What to record as decisions:**
- Any architectural or design choice made this session
- Any judgment call that deviated from the plan
- Any "we chose X over Y because Z"

```bash
node .ava/dal.mjs decision add --title "Title" --context "Why it came up" --chosen "What was chosen" --rationale "Short summary" --rationale-long "Full narrative with context, alternatives considered, and why this was the right call"
```

### Verify existing facts still accurate

Spot-check 3-5 existing `persistent` facts against current reality. If any are wrong, update them. If you relied on a fact this session, reconfirm it:

```bash
node .ava/dal.mjs fact confirm "<key>"
```

### Coverage check

Verify brain.db has the required minimum facts (see `/cleanup` for the full schema):
- `project.name`, `project.version`, `project.identity` — present?
- `tech.stack`, `tech.build` — present?
- `project.vision` — present?
- At least 1 active decision — present?

If any are missing, extract from CLAUDE.md or PROJECT_ROADMAP.md now. **A brain.db without these facts provides zero continuity value.**

---

## PART B: UPDATE DOCUMENTATION

### Document Boundaries (Respect the Routing Rule)

Each piece of information belongs in exactly one file. Use this guide:

| "What question does this answer?" | It belongs in... |
|:-----------------------------------|:-----------------|
| "What must I never do?" | `CLAUDE.md` |
| "How do I run/build this?" | `CLAUDE.md` |
| "Where are the important files?" | `CLAUDE.md` |
| "Why was this decision made?" | `PROJECT_ROADMAP.md` |
| "How did we get to this version?" | `PROJECT_ROADMAP.md` |
| "Where is this project headed?" | `PROJECT_ROADMAP.md` |
| "What should I do next?" | `IMPLEMENTATION_PLAN.md` |
| "What's currently broken?" | `IMPLEMENTATION_PLAN.md` |
| "What happened last session?" | `IMPLEMENTATION_PLAN.md` |

**Expanded reference:**

| File | Contains | Does NOT Contain |
|------|----------|------------------|
| `CLAUDE.md` | Current version, quick start, schema/data reference, anti-patterns, commands | Version history, sprint tasks, architecture rationale |
| `PROJECT_ROADMAP.md` | Version history, architecture decisions, tech stack, future roadmap | Sprint checklists, file modification lists, debugging notes |
| `IMPLEMENTATION_PLAN.md` | Current tasks, files modified, blockers, debugging notes, handoff | Full schema docs, architectural philosophy |

**Rule:** Information lives in ONE file. Reference from others, never duplicate.

---

### Step 3: Update IMPLEMENTATION_PLAN.md

- [ ] Add new version section at top (copy structure from previous)
- [ ] Mark completed tasks with [x]
- [ ] Add "Files Modified (V{X.Y.Z})" section
- [ ] Update header: "Updated" date and "Status" line
- [ ] Add new issues to "Known Issues" or "Blockers"
- [ ] Refresh "Handoff Notes" for next session (also write to brain.db: `node .ava/dal.mjs note add "note text" --category handoff`)
- [ ] Include any "silent decisions" or deviations documented during the session

---

### Step 4: Update PROJECT_ROADMAP.md (if milestone)

- [ ] Add row to VERSION HISTORY table
- [ ] Add "V{X.Y.Z} COMPLETE" section with feature descriptions
- [ ] Update header: version, "Last Updated" date
- [ ] Document any architectural decisions made (with rationale)

---

### Step 5: Update CLAUDE.md

Remember: CLAUDE.md is auto-read by Claude Code — front-load the most critical information.

- [ ] Update header: Version, Last Updated, Status (this is the very first line agents see)
- [ ] Update "Recent Changes" section
- [ ] Add new anti-patterns to "DO NOT" section if discovered
- [ ] Update schema/API reference if changed
- [ ] Update file structure section if new directories were added
- [ ] Update commands if build/run process changed

---

### Step 6: Create Subfolder READMEs (if new directories were added)

If new major folders were created during this session (listed in Part A, Step 1):
- [ ] Create a `README.md` in each new major directory
- [ ] Include: 1-2 sentence purpose, contents table, key interfaces
- [ ] Skip trivial folders (single-file utilities, config-only, framework-generated)

A "major folder" is one with multiple files serving a distinct purpose. When in doubt, create the README — it costs little and helps a lot.

---

## PART C: VERIFY PROMPT SYSTEM

### Step 7: Confirm Prompts Are Present

The `.prompts/` directory should contain the 19 canonical prompts. Verify the core set exists:

- [ ] `init.md` — session initialization (orient, read docs, verify state)
- [ ] `closeout.md` — this file (end-of-session documentation updates)
- [ ] `bootstrap.md` — create the 3-file documentation system (first time only)
- [ ] `discovery.md` — brainstorming and research before development
- [ ] `readme.md` — audit, create, and update directory READMEs
- [ ] `testing.md` — test strategy, generation, and coverage auditing
- [ ] `code-review.md` — structured code review with prioritized feedback
- [ ] `debugging.md` — systematic bug investigation and resolution
- [ ] `architecture.md` — system design and architectural decisions
- [ ] `requirements.md` — requirements gathering and specification
- [ ] `refactor.md` — code restructuring and improvement
- [ ] `release.md` — version release process
- [ ] `migration.md` — data and system migrations
- [ ] `incident.md` — production incident response
- [ ] `dependency-audit.md` — dependency review and updates
- [ ] `explore.md` — mid-project open thinking and assumption questioning
- [ ] `together.md` — relationship mode, human-first dialogue
- [ ] `cleanup.md` — DAL health audit and fact curation
- [ ] `dal-setup.md` — DAL setup, configuration, and reference (sessions, facts, notes, dual-session)

If any are missing, copy from the canonical source at `/home/ava/Prompt_Engineering/template/.prompts/`.

These prompts are universal — they are NOT project-specific. Do not generate per-project versions. Project-specific context lives in `CLAUDE.md` and the documentation files, not in the prompts.

---

## PART D: CLEAN UP PROJECT NOTES

Check for notes in this order:

### DAL Notes (if `.ava/brain.db` exists)

```bash
node .ava/dal.mjs note list
node .ava/dal.mjs note counts
```

Review open notes against this session's work. Mark resolved items as completed. Add new notes for issues discovered but not fixed.

### Markdown / UI Notes (if applicable)

For `.tab-notes.json`: set `"completed": true` on resolved notes. Do NOT delete entries — the UI handles cleanup. For markdown-based notes (`TODO.md`, `NOTES.md`, `notes/`): update or remove resolved items.

### For all note sources:

1. **Review all open notes** against this session's completed work.
2. **Mark resolved** any notes that describe:
   - Bugs that were fixed this session
   - Improvements that were implemented
   - Questions that were answered
   - Items that are no longer relevant (feature removed, approach changed)
3. **Update remaining notes** with any new context from this session (e.g., "Investigated this — root cause is X, fix requires Y").
4. **Add new notes** for issues discovered but not fixed this session (these also go in IMPLEMENTATION_PLAN handoff notes).

**If no notes system exists**, skip this part. Session state is captured in IMPLEMENTATION_PLAN.md handoff notes.

**Be aggressive about cleanup.** Stale notes are noise that slow down the next session. If it's done, remove it. If it's outdated, remove it. Only keep notes that represent real, actionable work.

---

## PART D-2: FINAL FACT AUDIT

> **If `.ava/brain.db` does NOT exist, skip this section.**

Session facts were recorded in Part A-2. Now do the final cleanup:

```bash
node .ava/dal.mjs fact audit
```

1. **Classify** any unclassified facts (immutable/persistent/standard/ephemeral).
2. **Prune** expired ephemeral: `node .ava/dal.mjs fact prune --dry-run` then `--execute` if clean.
3. **Verify** audit is clean: 0 unclassified, 0 stale, 0 expired.

If Part A-2 was skipped or incomplete, go back and do it now. **Closeout without fact recording is an incomplete closeout.**

---

## PART E: CHECK SCALING THRESHOLDS

After updating documentation, check if any core docs have exceeded advisory thresholds:

- **CLAUDE.md > 300 lines or 16KB** → Flag for the user. Consider moving detailed reference to spoke docs or annexes.
- **IMPLEMENTATION_PLAN.md > 400 lines** → Archive older sessions (keep last 3-5). Move detail to `documentation/archive/SESSION_ARCHIVE.md`. Leave a one-line summary and reference in the live doc.
- **PROJECT_ROADMAP.md > 400 lines** → Move deep-dive sections to `documentation/decisions/` or `documentation/archive/`.

If archiving is needed, ensure the live document retains a reference (e.g., "See `documentation/archive/` for sessions 1-20"). **Archived content must remain discoverable.**

If no thresholds are exceeded, skip this step.

---

## PART F: VERIFICATION

### Step 8: Clean Up Notes
(See Part D above — complete before verification.)

### Step 9: Cross-File Consistency Check

- [ ] Version numbers match: CLAUDE.md, PROJECT_ROADMAP.md, IMPLEMENTATION_PLAN.md
- [ ] Dates are consistent (all show today for "Updated")
- [ ] No contradictions between files
- [ ] No orphaned references to removed/renamed features
- [ ] Completed items marked complete, not left pending
- [ ] New subfolder READMEs created for any new major directories

### Step 10: Quality Check

- [ ] A new agent using the init prompt would orient correctly and not make critical mistakes
- [ ] Handoff notes provide enough context to continue next session
- [ ] "Recent Changes" accurately reflects this session
- [ ] CLAUDE.md front-loads critical rules before file structure/commands
- [ ] No information duplicated across files (routing rule respected)

---

## PART G: COMMIT & PUSH

### Step 11: Stage and Commit Changes

After all documentation updates and verification are complete:

```bash
git add -A
git commit -m "docs: session closeout v{X.Y.Z} — {1-line summary of session work}"
```

If a remote is configured and you have push access:

```bash
git push
```

If the push fails (auth, permissions, etc.), output the command for the user to run manually:

```bash
git push origin {branch}
```

**Do not skip this step.** An uncommitted closeout means the next session starts with a dirty working tree and potentially stale documentation.

---

## EXECUTE NOW

1. Complete Part A (inventory changes + determine version)
2. Update IMPLEMENTATION_PLAN.md
3. Update PROJECT_ROADMAP.md (if milestone reached)
4. Update CLAUDE.md
5. Create subfolder READMEs for new directories (use the readme prompt for guidance)
6. Verify prompt system is present
7. Clean up project notes (remove resolved, update remaining, add new)
8. Run verification checklists
9. Commit and push all changes
10. Summarize changes made to each file

Documentation is the bridge between sessions. Build it well.
