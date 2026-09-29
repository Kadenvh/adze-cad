---
name: readme
description: "Audit, create, and update AGENTS.md files (source of truth for agents) and directory README.md files across the project"
allowed-tools:
  - Read
  - Edit
  - Write
  - Glob
  - Grep
  - Bash
  - Agent
---

# AGENTS.md + README Maintenance

Audit, create, and update AGENTS.md files (the agent source of truth) and directory README.md files.

**AGENTS.md is the priority.** It's auto-read by Codex on every session. Every project MUST have one. README.md is secondary (for humans browsing repos).

## AGENTS.md Protocol

### 1. Audit existing AGENTS.md

Read the current AGENTS.md. Check for:
- **Version** — `**Version:** X.Y.Z` in header. Must match package.json or brain.db identity.
- **Critical Rules** — DO NOT and ALWAYS sections. Are they current? Missing any?
- **Tech Stack** — table with components, versions, notes. Accurate?
- **Quick Reference** — build/test/deploy commands. Do they work?
- **File Structure** — does it reflect reality? Stale entries?
- **API Endpoints** — count and grouping. Accurate?
- **Known Issues** — any resolved bugs still listed?

### 2. Gather current state

```bash
# Version from package.json or brain.db
node .ava/dal.mjs identity get project.version 2>/dev/null || grep '"version"' package.json
# Endpoint count (if Express)
grep -c 'router\.\(get\|post\|put\|delete\|patch\)' server/routes/*.mjs 2>/dev/null
# Test count
npm test -- --reporter=verbose 2>/dev/null | tail -5
# File structure
find . -maxdepth 3 -type f -name "*.ts" -o -name "*.tsx" -o -name "*.mjs" | head -50
```

### 3. Create or update AGENTS.md

**Required sections (in order):**

```markdown
# Project Name

**Version:** X.Y.Z | **Status:** summary | **Updated:** YYYY-MM-DD

---

## Quick Reference
(build, test, deploy, lint commands — copy-pasteable)

## Critical Rules
### DO NOT
(anti-patterns, forbidden approaches, explicit architectural decisions)
### ALWAYS
(mandatory practices, required checks before commit)

## Tech Stack
(table: Component | Choice | Notes)

## File Structure
(tree with annotations — key files only, not exhaustive)

## API Endpoints (if applicable)
(grouped table with counts)

## Known Issues / Tech Debt
(current, not aspirational)
```

**Rules for AGENTS.md:**
- Be factual, not aspirational. Describe what IS.
- Front-load anti-patterns — agents often start acting before finishing the read.
- Keep under 5KB for root AGENTS.md. Move details to brain.db or spoke docs.
- No duplication — information lives in ONE place.
- Strategic context (plans, decisions, architecture) belongs in brain.db, not AGENTS.md.
- IMPLEMENTATION_PLAN.md and PROJECT_ROADMAP.md remain as physical files in `documentation/`.

### 4. Cross-project AGENTS.md audit

If asked to audit multiple projects, check each for:
- Existence of AGENTS.md
- Version accuracy (matches package.json / brain.db)
- Staleness (updated_at > 7 days)
- Missing required sections
- Stale endpoint counts

Report findings as a table.

## README Protocol (secondary)

For directory README.md files within a project:

**Create a README when:** 3+ files with shared purpose, non-obvious conventions, boundary directory (components/, hooks/, features/).

**Skip when:** Fewer than 3 files, parent README covers it, directory has its own AGENTS.md.

Each directory README should contain:
- 1-2 sentence purpose statement
- File table: `| File | Purpose |`
- Conventions (naming, patterns, how to add new items)
- Key interfaces or exports

**Rules:** Be factual. Don't duplicate content from AGENTS.md. Keep under 80 lines.
