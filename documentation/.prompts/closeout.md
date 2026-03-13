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
- [ ] Refresh "Handoff Notes" for next session
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

The `.prompts/` directory should contain the 18 canonical prompts. Verify the core set exists:

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

If any are missing, copy from the canonical source at `/home/ava/Prompt_Engineering/template/.prompts/`.

These prompts are universal — they are NOT project-specific. Do not generate per-project versions. Project-specific context lives in `CLAUDE.md` and the documentation files, not in the prompts.

---

## PART D: CLEAN UP PROJECT NOTES

If the project has a notes/issues/task tracking system (same sources checked during init):

For `.tab-notes.json`: set `"completed": true` on resolved notes. Do NOT delete entries — the UI handles cleanup. Match notes to the relevant tab key.

1. **Review all open notes** against this session's completed work.
2. **Mark resolved** (set `completed: true`) any notes that describe:
   - Bugs that were fixed this session
   - Improvements that were implemented
   - Questions that were answered
   - Items that are no longer relevant (feature removed, approach changed)
3. **Update remaining notes** with any new context from this session (e.g., "Investigated this — root cause is X, fix requires Y").
4. **Add new notes** for issues discovered but not fixed this session (these also go in IMPLEMENTATION_PLAN handoff notes).

**Be aggressive about cleanup.** Stale notes are noise that slow down the next session. If it's done, remove it. If it's outdated, remove it. Only keep notes that represent real, actionable work.

---

## PART D-2: CURATE SESSION FACTS

### Curate Facts (if DAL is active)

If `.ava/brain.db` exists, review facts created or modified this session:

**Step 0: Audit current state** (if DAL supports `fact audit`)

```bash
node .ava/dal.mjs fact audit
```

Review the output: unclassified facts, stale facts, and expired ephemeral facts.

> **Backward compatibility:** If `fact audit` is not recognized (DAL < v2.0), skip Steps 0, 4, and 5 — proceed with manual classification below.

1. **Classify permanence** for new and unclassified facts using these heuristics:
   - `immutable`: Mission, vision, identity, founding principles — NEVER pruned
   - `persistent`: Architecture patterns, tech stack, service ports — rarely change
   - `standard` (default): Working knowledge, current patterns — normal lifecycle
   - `ephemeral`: Session-specific debug findings, temp workarounds — pruned after 3 sessions without reconfirmation

   Use: `node .ava/dal.mjs fact set "<key>" --permanence <tier>`

2. **Flag stale facts**: Any fact with `last_confirmed_at` older than 90 days
   and not `immutable` or `persistent` — confirm or archive.

3. **Reconfirm used facts**: If you relied on a fact this session, run
   `node .ava/dal.mjs fact confirm "<key>"` to refresh its `last_confirmed_at` timestamp.

4. **Prune expired ephemeral facts** (if DAL supports `fact prune`):
   ```bash
   node .ava/dal.mjs fact prune --dry-run
   ```
   Review what would be deleted. If acceptable:
   ```bash
   node .ava/dal.mjs fact prune --execute
   ```

5. **Verify**: Run `node .ava/dal.mjs fact audit` again. Target: 0 unclassified facts remaining.

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
