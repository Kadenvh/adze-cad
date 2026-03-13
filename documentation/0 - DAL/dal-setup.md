# DAL (Durable Agentic Layer) Setup Guide

## What This Is

The DAL is a SQLite-backed persistent state engine for Claude Code projects. It tracks sessions, tasks, facts, decisions, and notes across conversations — giving every new session full context on what happened before.

**How it works:**
1. A `SessionStart` hook fires when Claude Code opens → runs `node .ava/dal.mjs context` → injects state into the conversation
2. During work, the agent records tasks, facts, and decisions via the CLI
3. At session close, the agent logs what was done → next session picks up where this one left off
4. The renderer can auto-populate sections of your markdown docs from the DB

---

## Architecture

```
.ava/                          # DAL root (lives at project root)
  dal.mjs                      # CLI entry (14 commands)
  package.json                 # Only dependency: better-sqlite3
  brain.db                     # SQLite DB (created by bootstrap, gitignored)
  lib/
    db.mjs                     # Connection, migrations, integrity check
    sessions.mjs               # Start/close/list sessions
    tasks.mjs                  # Task CRUD + tree view + stale blocker detection
    facts.mjs                  # Fact upsert + FTS5 search
    decisions.mjs              # Decision CRUD + supersede
    notes.mjs                  # Per-tab sticky notes (4 categories)
    context.mjs                # Generates the init context payload
    renderer.mjs               # Writes DB data into markdown delimiters
  migrations/
    001_initial.sql            # Core tables (tasks, sessions, facts, decisions, snapshots)
    002_add_notes.sql          # Notes table
    003_dual_session.sql       # Permanence tiers, agent roles, explorations

.claude/
  hooks/
    session-context.js         # SessionStart hook — injects DAL + git state
    log-util.js                # Shared hook logger (dependency of session-context)
  settings.json                # Must include SessionStart hook entry
```

---

## Setup Steps

### Step 1: Create the `.ava/` directory structure

```bash
mkdir -p .ava/lib .ava/migrations
```

### Step 2: Create `package.json`

```json
{
  "name": "project-dal",
  "version": "1.0.0",
  "type": "module",
  "description": "Durable Agentic Layer — SQLite-backed persistent state",
  "dependencies": {
    "better-sqlite3": "^11"
  }
}
```

Then run: `cd .ava && npm install`

### Step 3: Create migration files

**`.ava/migrations/001_initial.sql`:**
```sql
-- DAL Schema v1 — Initial tables
-- Applied by: dal.mjs bootstrap / dal.mjs migrate

PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS schema_version (
    version     INTEGER NOT NULL,
    applied_at  TEXT NOT NULL DEFAULT (datetime('now')),
    description TEXT
);

CREATE TABLE IF NOT EXISTS tasks (
    id              TEXT PRIMARY KEY,
    title           TEXT NOT NULL,
    description     TEXT,
    status          TEXT NOT NULL DEFAULT 'not_started'
                    CHECK (status IN ('not_started', 'in_progress', 'blocked', 'done', 'cancelled')),
    parent_task_id  TEXT REFERENCES tasks(id),
    blocked_by      TEXT DEFAULT '[]',
    priority        INTEGER DEFAULT 0,
    assigned_agent  TEXT,
    component       TEXT,
    session_created TEXT,
    session_closed  TEXT,
    created_at      TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks(status);
CREATE INDEX IF NOT EXISTS idx_tasks_parent ON tasks(parent_task_id);
CREATE INDEX IF NOT EXISTS idx_tasks_component ON tasks(component);
CREATE INDEX IF NOT EXISTS idx_tasks_priority ON tasks(priority DESC);

-- NOTE: This trigger's UPDATE does not re-fire because SQLite's recursive_triggers
-- is OFF by default. Do NOT enable PRAGMA recursive_triggers or this will loop.
CREATE TRIGGER IF NOT EXISTS trg_tasks_updated
    AFTER UPDATE ON tasks
    FOR EACH ROW
BEGIN
    UPDATE tasks SET updated_at = datetime('now') WHERE id = NEW.id;
END;

CREATE TABLE IF NOT EXISTS sessions (
    id              TEXT PRIMARY KEY,
    start_time      TEXT NOT NULL DEFAULT (datetime('now')),
    end_time        TEXT,
    exit_reason     TEXT CHECK (exit_reason IN ('normal', 'interrupted', 'crashed', 'context_limit')),
    summary         TEXT,
    version_bump    TEXT,
    agent_model     TEXT,
    tasks_completed TEXT DEFAULT '[]',
    tasks_created   TEXT DEFAULT '[]',
    files_modified  TEXT DEFAULT '[]',
    decisions_made  TEXT DEFAULT '[]',
    created_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_sessions_start ON sessions(start_time);
CREATE INDEX IF NOT EXISTS idx_sessions_exit ON sessions(exit_reason);

CREATE TABLE IF NOT EXISTS facts (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    key                 TEXT NOT NULL UNIQUE,
    value               TEXT NOT NULL,
    confidence          REAL NOT NULL DEFAULT 1.0
                        CHECK (confidence >= 0.0 AND confidence <= 1.0),
    domain              TEXT,
    source_session_id   TEXT REFERENCES sessions(id),
    tags                TEXT DEFAULT '[]',
    supersedes          INTEGER REFERENCES facts(id),
    created_at          TEXT NOT NULL DEFAULT (datetime('now')),
    last_confirmed_at   TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_facts_domain ON facts(domain);
CREATE INDEX IF NOT EXISTS idx_facts_confidence ON facts(confidence);
CREATE INDEX IF NOT EXISTS idx_facts_confirmed ON facts(last_confirmed_at);

CREATE VIRTUAL TABLE IF NOT EXISTS facts_fts USING fts5(
    key, value, tags,
    content='facts',
    content_rowid='id'
);

CREATE TRIGGER IF NOT EXISTS trg_facts_ai AFTER INSERT ON facts BEGIN
    INSERT INTO facts_fts(rowid, key, value, tags)
    VALUES (new.id, new.key, new.value, new.tags);
END;
CREATE TRIGGER IF NOT EXISTS trg_facts_ad AFTER DELETE ON facts BEGIN
    INSERT INTO facts_fts(facts_fts, rowid, key, value, tags)
    VALUES ('delete', old.id, old.key, old.value, old.tags);
END;
CREATE TRIGGER IF NOT EXISTS trg_facts_au AFTER UPDATE ON facts BEGIN
    INSERT INTO facts_fts(facts_fts, rowid, key, value, tags)
    VALUES ('delete', old.id, old.key, old.value, old.tags);
    INSERT INTO facts_fts(rowid, key, value, tags)
    VALUES (new.id, new.key, new.value, new.tags);
END;

CREATE TABLE IF NOT EXISTS snapshots (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id      TEXT NOT NULL REFERENCES sessions(id),
    timestamp       TEXT NOT NULL DEFAULT (datetime('now')),
    current_task_id TEXT,
    modified_files  TEXT DEFAULT '[]',
    git_diff_stat   TEXT,
    agent_plan      TEXT,
    state_blob      TEXT DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS idx_snapshots_session ON snapshots(session_id);
CREATE INDEX IF NOT EXISTS idx_snapshots_time ON snapshots(timestamp);

CREATE TABLE IF NOT EXISTS decisions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    title           TEXT NOT NULL,
    context         TEXT NOT NULL,
    alternatives    TEXT DEFAULT '[]',
    chosen          TEXT NOT NULL,
    rationale       TEXT NOT NULL,
    component       TEXT,
    session_id      TEXT REFERENCES sessions(id),
    status          TEXT DEFAULT 'active'
                    CHECK (status IN ('active', 'superseded', 'revisit')),
    superseded_by   INTEGER REFERENCES decisions(id),
    created_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_decisions_component ON decisions(component);
CREATE INDEX IF NOT EXISTS idx_decisions_status ON decisions(status);

INSERT INTO schema_version (version, description) VALUES (1, 'Initial DAL schema');
```

**`.ava/migrations/002_add_notes.sql`:**
```sql
-- DAL Schema v2 — Notes table
-- Migrates per-tab notes from .tab-notes.json to brain.db

CREATE TABLE IF NOT EXISTS notes (
    id          TEXT PRIMARY KEY,
    tab_key     TEXT NOT NULL,
    category    TEXT NOT NULL
                CHECK (category IN ('improvement', 'issue', 'bug', 'idea')),
    text        TEXT NOT NULL,
    completed   INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_notes_tab ON notes(tab_key);
CREATE INDEX IF NOT EXISTS idx_notes_category ON notes(tab_key, category);
CREATE INDEX IF NOT EXISTS idx_notes_completed ON notes(completed);

CREATE TRIGGER IF NOT EXISTS trg_notes_updated
    AFTER UPDATE ON notes
    FOR EACH ROW
BEGIN
    UPDATE notes SET updated_at = datetime('now') WHERE id = NEW.id;
END;

INSERT INTO schema_version (version, description) VALUES (2, 'Add notes table');
```

**`.ava/migrations/003_dual_session.sql`:**
```sql
-- DAL Schema v3 — Dual-session support
-- Adds permanence tiers, agent role tracking, and explorations table

-- Permanence column on facts: controls injection behavior and pruning lifecycle
ALTER TABLE facts ADD COLUMN permanence TEXT NOT NULL DEFAULT 'standard'
    CHECK (permanence IN ('immutable', 'persistent', 'standard', 'ephemeral'));

CREATE INDEX IF NOT EXISTS idx_facts_permanence ON facts(permanence);

-- Agent role column on sessions: tracks which cognitive mode ran the session
ALTER TABLE sessions ADD COLUMN agent_role TEXT;

-- Explorations table: the general agent's domain for open-ended proposals
CREATE TABLE IF NOT EXISTS explorations (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT NOT NULL,
    type        TEXT NOT NULL,
    status      TEXT NOT NULL DEFAULT 'open'
                CHECK (status IN ('open', 'proposed', 'accepted', 'rejected')),
    description TEXT,
    session_id  TEXT REFERENCES sessions(id),
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_explorations_status ON explorations(status);
CREATE INDEX IF NOT EXISTS idx_explorations_session ON explorations(session_id);

INSERT INTO schema_version (version, description) VALUES (3, 'Dual-session support: permanence tiers, agent roles, explorations');
```

### Step 4: Create the library modules

Create each file below in `.ava/lib/`. These are **exact copies** from the reference implementation — they are project-agnostic by design (all paths are relative to `.ava/`).

**`.ava/lib/db.mjs`:**
```javascript
// db.mjs — Database connection, auto-migration, integrity check
import Database from "better-sqlite3";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const AVA_DIR = path.resolve(__dirname, "..");
const DB_PATH = path.join(AVA_DIR, "brain.db");
const MIGRATIONS_DIR = path.join(AVA_DIR, "migrations");

let _db = null;

export function getDb({ create = false } = {}) {
  if (_db) return _db;
  if (!create && !fs.existsSync(DB_PATH)) return null;

  _db = new Database(DB_PATH);
  _db.pragma("journal_mode = WAL");
  _db.pragma("synchronous = NORMAL");
  _db.pragma("foreign_keys = ON");
  runMigrations(_db);
  return _db;
}

export function requireDb() {
  const db = getDb();
  if (!db) {
    process.stderr.write("Warning: .ava/brain.db not found. Skipping DAL operation.\n");
    process.exit(0);
  }
  return db;
}

function runMigrations(db) {
  const hasSchemaTable = db
    .prepare("SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version'")
    .get();

  let currentVersion = 0;
  if (hasSchemaTable) {
    const row = db.prepare("SELECT MAX(version) as v FROM schema_version").get();
    currentVersion = row?.v || 0;
  }

  if (!fs.existsSync(MIGRATIONS_DIR)) return;

  const files = fs.readdirSync(MIGRATIONS_DIR).filter((f) => f.endsWith(".sql")).sort();
  const pending = files.filter((f) => {
    const num = parseInt(f.split("_")[0], 10);
    return num > currentVersion;
  });

  if (pending.length === 0) return;
  if (currentVersion > 0) backup();

  for (const file of pending) {
    const sql = fs.readFileSync(path.join(MIGRATIONS_DIR, file), "utf8");
    const lines = sql.split("\n");
    const pragmaLines = [];
    const otherLines = [];
    for (const line of lines) {
      if (/^\s*PRAGMA\b/i.test(line)) {
        pragmaLines.push(line);
      } else {
        otherLines.push(line);
      }
    }
    for (const pragma of pragmaLines) db.exec(pragma);
    const remaining = otherLines.join("\n").trim();
    if (remaining) db.exec(remaining);
  }
}

export function backup() {
  if (!fs.existsSync(DB_PATH)) return null;
  const timestamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
  const bakPath = `${DB_PATH}.bak-${timestamp}`;
  fs.copyFileSync(DB_PATH, bakPath);
  return bakPath;
}

export function integrityCheck() {
  const db = requireDb();
  const row = db.prepare("PRAGMA integrity_check").get();
  const result = row?.integrity_check || "unknown";
  return { ok: result === "ok", result };
}

export function schemaVersion() {
  const db = requireDb();
  const row = db.prepare("SELECT MAX(version) as v FROM schema_version").get();
  return row?.v || 0;
}

export function dbSize() {
  if (!fs.existsSync(DB_PATH)) return 0;
  return fs.statSync(DB_PATH).size;
}

export function closeDb() {
  if (_db) { _db.close(); _db = null; }
}

export { DB_PATH, AVA_DIR };
```

**`.ava/lib/sessions.mjs`:**
```javascript
// sessions.mjs — Session management (start, close, list)
import { requireDb } from "./db.mjs";
import crypto from "crypto";

export function startSession({ id, model } = {}) {
  const db = requireDb();
  const sessionId = id || crypto.randomUUID();
  db.prepare("INSERT INTO sessions (id, agent_model) VALUES (?, ?)").run(sessionId, model || null);
  return sessionId;
}

export function closeSession({ summary, exitReason = "normal", version, filesModified, tasksCompleted, tasksCreated } = {}) {
  const db = requireDb();
  let session = db.prepare("SELECT id FROM sessions WHERE end_time IS NULL ORDER BY start_time DESC LIMIT 1").get();

  if (!session) {
    process.stderr.write("No open session found. Created minimal session record.\n");
    const id = crypto.randomUUID();
    db.prepare("INSERT INTO sessions (id) VALUES (?)").run(id);
    session = { id };
  }

  db.prepare(`
    UPDATE sessions SET
      end_time = datetime('now'), exit_reason = ?, summary = ?, version_bump = ?,
      files_modified = ?, tasks_completed = ?, tasks_created = ?
    WHERE id = ?
  `).run(exitReason, summary || null, version || null, filesModified || "[]", tasksCompleted || "[]", tasksCreated || "[]", session.id);

  return session.id;
}

export function getOpenSession() {
  const db = requireDb();
  return db.prepare("SELECT * FROM sessions WHERE end_time IS NULL ORDER BY start_time DESC LIMIT 1").get() || null;
}

export function listSessions({ limit = 10 } = {}) {
  const db = requireDb();
  return db.prepare("SELECT * FROM sessions ORDER BY start_time DESC LIMIT ?").all(limit);
}

export function getSession(id) {
  const db = requireDb();
  return db.prepare("SELECT * FROM sessions WHERE id = ?").get(id) || null;
}

export function sessionCounts() {
  const db = requireDb();
  return db.prepare(`
    SELECT COUNT(*) as total,
      SUM(CASE WHEN end_time IS NULL THEN 1 ELSE 0 END) as open,
      SUM(CASE WHEN exit_reason = 'normal' THEN 1 ELSE 0 END) as normal,
      SUM(CASE WHEN exit_reason = 'interrupted' THEN 1 ELSE 0 END) as interrupted,
      SUM(CASE WHEN exit_reason = 'crashed' THEN 1 ELSE 0 END) as crashed,
      SUM(CASE WHEN exit_reason = 'context_limit' THEN 1 ELSE 0 END) as context_limit
    FROM sessions
  `).get();
}
```

**`.ava/lib/tasks.mjs`:**
```javascript
// tasks.mjs — Task CRUD operations
import { requireDb } from "./db.mjs";
import { getOpenSession } from "./sessions.mjs";

const VALID_STATUSES = ["not_started", "in_progress", "blocked", "done", "cancelled"];

export function addTask({ id, title, description, parent, component, priority = 0, assignedAgent, status = "not_started", blockedBy }) {
  const db = requireDb();
  const existing = db.prepare("SELECT id FROM tasks WHERE id = ?").get(id);
  if (existing) throw new Error(`Task ID "${id}" already exists.`);
  if (parent) {
    const parentExists = db.prepare("SELECT id FROM tasks WHERE id = ?").get(parent);
    if (!parentExists) throw new Error(`Parent task "${parent}" does not exist.`);
  }
  const session = getOpenSession();
  db.prepare(`
    INSERT INTO tasks (id, title, description, status, parent_task_id, blocked_by, priority, assigned_agent, component, session_created)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).run(id, title, description || null, status, parent || null, blockedBy || "[]", priority, assignedAgent || null, component || null, session?.id || null);
  return id;
}

export function updateTask(id, updates) {
  const db = requireDb();
  const existing = db.prepare("SELECT * FROM tasks WHERE id = ?").get(id);
  if (!existing) throw new Error(`Task "${id}" not found.`);

  const fields = [];
  const values = [];

  if (updates.status !== undefined) {
    if (!VALID_STATUSES.includes(updates.status)) throw new Error(`Invalid status "${updates.status}".`);
    fields.push("status = ?"); values.push(updates.status);
    if (updates.status === "done" || updates.status === "cancelled") {
      const session = getOpenSession();
      fields.push("session_closed = ?"); values.push(session?.id || null);
    }
  }
  if (updates.title !== undefined) { fields.push("title = ?"); values.push(updates.title); }
  if (updates.description !== undefined) { fields.push("description = ?"); values.push(updates.description); }
  if (updates.priority !== undefined) { fields.push("priority = ?"); values.push(updates.priority); }
  if (updates.assignedAgent !== undefined) { fields.push("assigned_agent = ?"); values.push(updates.assignedAgent); }
  if (updates.blockedBy !== undefined) { fields.push("blocked_by = ?"); values.push(updates.blockedBy); }
  if (updates.component !== undefined) { fields.push("component = ?"); values.push(updates.component); }

  if (fields.length === 0) throw new Error("No fields to update.");
  values.push(id);
  db.prepare(`UPDATE tasks SET ${fields.join(", ")} WHERE id = ?`).run(...values);
  return id;
}

export function listTasks({ status, component, includeCompleted = false } = {}) {
  const db = requireDb();
  let sql = "SELECT * FROM tasks WHERE 1=1";
  const params = [];
  if (status) { sql += " AND status = ?"; params.push(status); }
  else if (!includeCompleted) { sql += " AND status NOT IN ('done', 'cancelled')"; }
  if (component) { sql += " AND component = ?"; params.push(component); }
  sql += " ORDER BY priority DESC, id";
  return db.prepare(sql).all(...params);
}

export function taskTree({ root } = {}) {
  const db = requireDb();
  if (root) return db.prepare("SELECT * FROM tasks WHERE id = ? OR id LIKE ? ORDER BY id").all(root, `${root}.%`);
  return db.prepare("SELECT * FROM tasks ORDER BY id").all();
}

export function formatTaskTree(tasks) {
  const STATUS_ICONS = { not_started: "○", in_progress: "◐", blocked: "⊘", done: "●", cancelled: "✗" };
  const byParent = new Map();
  for (const t of tasks) {
    const parent = t.parent_task_id || "__root__";
    if (!byParent.has(parent)) byParent.set(parent, []);
    byParent.get(parent).push(t);
  }
  const lines = [];
  function render(parentId, prefix = "") {
    const children = byParent.get(parentId) || [];
    for (let i = 0; i < children.length; i++) {
      const t = children[i];
      const isLast = i === children.length - 1;
      const icon = STATUS_ICONS[t.status] || "?";
      const connector = prefix === "" ? "" : isLast ? "└── " : "├── ";
      lines.push(`${prefix}${connector}${t.id}  [${icon} ${t.status}] ${t.title}`);
      const nextPrefix = prefix === "" ? "" : prefix + (isLast ? "    " : "│   ");
      render(t.id, nextPrefix || "    ");
    }
  }
  render("__root__");
  return lines.join("\n");
}

export function taskCounts() {
  const db = requireDb();
  const rows = db.prepare("SELECT status, COUNT(*) as count FROM tasks GROUP BY status").all();
  const counts = { total: 0 };
  for (const row of rows) { counts[row.status] = row.count; counts.total += row.count; }
  return counts;
}

export function staleBlockers() {
  const db = requireDb();
  const blockedTasks = db.prepare("SELECT id, title, blocked_by FROM tasks WHERE status = 'blocked'").all();
  const stale = [];
  for (const task of blockedTasks) {
    const blockerIds = JSON.parse(task.blocked_by || "[]");
    for (const bid of blockerIds) {
      const blocker = db.prepare("SELECT id, status FROM tasks WHERE id = ?").get(bid);
      if (blocker && (blocker.status === "done" || blocker.status === "cancelled")) {
        stale.push({ taskId: task.id, taskTitle: task.title, blockerId: bid, blockerStatus: blocker.status });
      }
    }
  }
  return stale;
}
```

**`.ava/lib/facts.mjs`:**
```javascript
// facts.mjs — Fact CRUD + FTS search
import { requireDb } from "./db.mjs";
import { getOpenSession } from "./sessions.mjs";

export function setFact({ key, value, confidence = 1.0, domain, tags, permanence }) {
  const db = requireDb();
  const session = getOpenSession();
  const tagsJson = tags || "[]";
  const perm = permanence || "standard";
  const existing = db.prepare("SELECT id FROM facts WHERE key = ?").get(key);

  if (existing) {
    db.prepare(`UPDATE facts SET value = ?, confidence = ?, domain = ?, tags = ?, permanence = ?, last_confirmed_at = datetime('now') WHERE key = ?`)
      .run(value, confidence, domain || null, tagsJson, perm, key);
    return { action: "updated", id: existing.id, key };
  } else {
    const result = db.prepare(`INSERT INTO facts (key, value, confidence, domain, source_session_id, tags, permanence) VALUES (?, ?, ?, ?, ?, ?, ?)`)
      .run(key, value, confidence, domain || null, session?.id || null, tagsJson, perm);
    return { action: "inserted", id: result.lastInsertRowid, key };
  }
}

export function searchFacts(query) {
  const db = requireDb();
  const safeQuery = query.replace(/['"*()]/g, " ").trim();
  if (!safeQuery) return [];
  const terms = safeQuery.split(/\s+/).filter(Boolean);
  const ftsQuery = terms.join(" OR ");
  return db.prepare(`
    SELECT f.key, f.value, f.confidence, f.domain, f.tags, f.created_at, f.last_confirmed_at
    FROM facts f WHERE f.id IN (SELECT rowid FROM facts_fts WHERE facts_fts MATCH ?)
    ORDER BY f.confidence DESC
  `).all(ftsQuery);
}

export function listFacts({ domain, minConfidence = 0.0, permanence } = {}) {
  const db = requireDb();
  let sql = "SELECT * FROM facts WHERE confidence >= ?";
  const params = [minConfidence];
  if (domain) { sql += " AND domain = ?"; params.push(domain); }
  if (permanence) { sql += " AND permanence = ?"; params.push(permanence); }
  sql += " ORDER BY last_confirmed_at DESC";
  return db.prepare(sql).all(...params);
}

export function confirmFact(key) {
  const db = requireDb();
  const result = db.prepare("UPDATE facts SET last_confirmed_at = datetime('now') WHERE key = ?").run(key);
  if (result.changes === 0) throw new Error(`Fact "${key}" not found.`);
  return key;
}

export function deleteFact(key) {
  const db = requireDb();
  const result = db.prepare("DELETE FROM facts WHERE key = ?").run(key);
  if (result.changes === 0) throw new Error(`Fact "${key}" not found.`);
  return key;
}

export function factCounts() {
  const db = requireDb();
  return db.prepare("SELECT COUNT(*) as total, ROUND(AVG(confidence), 2) as avg_confidence FROM facts").get();
}

export function auditFacts() {
  const db = requireDb();

  // Facts still at default 'standard' that may need reclassification
  const unclassified = db.prepare(
    `SELECT key, value, permanence, last_confirmed_at FROM facts
     WHERE permanence = 'standard'
     ORDER BY last_confirmed_at ASC`
  ).all();

  // Facts not confirmed in 90+ days (excluding immutable/persistent which don't decay)
  const stale = db.prepare(
    `SELECT key, value, permanence, last_confirmed_at FROM facts
     WHERE last_confirmed_at < datetime('now', '-90 days')
       AND permanence NOT IN ('immutable', 'persistent')
     ORDER BY last_confirmed_at ASC`
  ).all();

  // Ephemeral facts past the 3-session expiry window
  const ephemeralPastWindow = db.prepare(
    `SELECT * FROM (
       SELECT f.key, f.value, f.last_confirmed_at,
         (SELECT COUNT(*) FROM sessions WHERE start_time > f.last_confirmed_at) as sessions_since
       FROM facts f
       WHERE f.permanence = 'ephemeral'
     ) WHERE sessions_since >= 3`
  ).all();

  return { unclassified, stale, ephemeralPastWindow };
}

export function pruneFacts({ dryRun = true } = {}) {
  const db = requireDb();

  const candidates = db.prepare(
    `SELECT * FROM (
       SELECT f.id, f.key, f.value, f.last_confirmed_at,
         (SELECT COUNT(*) FROM sessions WHERE start_time > f.last_confirmed_at) as sessions_since
       FROM facts f
       WHERE f.permanence = 'ephemeral'
     ) WHERE sessions_since >= 3`
  ).all();

  if (dryRun) {
    return { dryRun: true, count: candidates.length, candidates };
  }

  const deleteStmt = db.prepare("DELETE FROM facts WHERE id = ?");
  const tx = db.transaction(() => {
    for (const c of candidates) deleteStmt.run(c.id);
  });
  tx();
  return { dryRun: false, deleted: candidates.length, candidates };
}
```

**`.ava/lib/decisions.mjs`:**
```javascript
// decisions.mjs — Decision CRUD
import { requireDb } from "./db.mjs";
import { getOpenSession } from "./sessions.mjs";

export function addDecision({ title, context, chosen, rationale, alternatives, component }) {
  const db = requireDb();
  const session = getOpenSession();
  const result = db.prepare(`
    INSERT INTO decisions (title, context, alternatives, chosen, rationale, component, session_id)
    VALUES (?, ?, ?, ?, ?, ?, ?)
  `).run(title, context, alternatives || "[]", chosen, rationale, component || null, session?.id || null);
  return result.lastInsertRowid;
}

export function listDecisions({ component, status = "active" } = {}) {
  const db = requireDb();
  let sql = "SELECT * FROM decisions WHERE 1=1";
  const params = [];
  if (status) { sql += " AND status = ?"; params.push(status); }
  if (component) { sql += " AND component = ?"; params.push(component); }
  sql += " ORDER BY created_at DESC";
  return db.prepare(sql).all(...params);
}

export function supersedeDecision(oldId, newId) {
  const db = requireDb();
  if (!db.prepare("SELECT id FROM decisions WHERE id = ?").get(oldId)) throw new Error(`Decision ${oldId} not found.`);
  if (!db.prepare("SELECT id FROM decisions WHERE id = ?").get(newId)) throw new Error(`Decision ${newId} not found.`);
  db.prepare("UPDATE decisions SET status = 'superseded', superseded_by = ? WHERE id = ?").run(newId, oldId);
  return oldId;
}

export function decisionCounts() {
  const db = requireDb();
  const rows = db.prepare("SELECT status, COUNT(*) as count FROM decisions GROUP BY status").all();
  const counts = { total: 0 };
  for (const row of rows) { counts[row.status] = row.count; counts.total += row.count; }
  return counts;
}
```

**`.ava/lib/notes.mjs`:**
```javascript
// notes.mjs — Notes CRUD operations (per-tab sticky notes)
import { requireDb } from "./db.mjs";

const VALID_CATEGORIES = ["improvement", "issue", "bug", "idea"];

export function listAllNotes() {
  const db = requireDb();
  const rows = db.prepare("SELECT * FROM notes ORDER BY created_at DESC").all();
  const grouped = {};
  for (const row of rows) {
    if (!grouped[row.tab_key]) grouped[row.tab_key] = { notes: [], updatedAt: 0 };
    grouped[row.tab_key].notes.push(rowToNote(row));
    const ts = new Date(row.updated_at).getTime();
    if (ts > grouped[row.tab_key].updatedAt) grouped[row.tab_key].updatedAt = ts;
  }
  return grouped;
}

export function listNotesByTab(tabKey) {
  const db = requireDb();
  const rows = db.prepare("SELECT * FROM notes WHERE tab_key = ? ORDER BY created_at DESC").all(tabKey);
  let updatedAt = 0;
  const notes = rows.map((row) => {
    const ts = new Date(row.updated_at).getTime();
    if (ts > updatedAt) updatedAt = ts;
    return rowToNote(row);
  });
  return { notes, updatedAt };
}

export function saveNotesForTab(tabKey, notes) {
  const db = requireDb();
  const insert = db.prepare("INSERT INTO notes (id, tab_key, category, text, completed, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)");
  const txn = db.transaction((notesArr) => {
    db.prepare("DELETE FROM notes WHERE tab_key = ?").run(tabKey);
    for (const n of notesArr) {
      insert.run(n.id, tabKey,
        VALID_CATEGORIES.includes(n.category) ? n.category : "improvement",
        n.text || "", n.completed ? 1 : 0,
        n.createdAt ? new Date(n.createdAt).toISOString() : new Date().toISOString(),
        new Date().toISOString());
    }
  });
  txn(notes);
}

export function clearCompleted(tabKey) {
  const db = requireDb();
  return db.prepare("DELETE FROM notes WHERE tab_key = ? AND completed = 1").run(tabKey).changes;
}

export function noteCounts() {
  const db = requireDb();
  const rows = db.prepare("SELECT tab_key, category, COUNT(*) as count FROM notes WHERE completed = 0 GROUP BY tab_key, category").all();
  const total = db.prepare("SELECT COUNT(*) as count FROM notes WHERE completed = 0").get();
  return { byTab: rows, total: total.count };
}

function rowToNote(row) {
  return { id: row.id, category: row.category, text: row.text, completed: !!row.completed, createdAt: new Date(row.created_at).getTime() };
}
```

**`.ava/lib/context.mjs`:**
```javascript
// context.mjs — Role-aware context payload generator for init injection
import { requireDb } from "./db.mjs";

export function generateContext({ role = "general" } = {}) {
  const db = requireDb();
  const lines = [];

  // --- Tier 1: Immutable facts (ALWAYS injected, both roles) ---
  const immutableFacts = db.prepare(
    "SELECT key, value FROM facts WHERE permanence = 'immutable' ORDER BY key"
  ).all();

  if (immutableFacts.length > 0) {
    lines.push("**Mission & Vision (immutable):**");
    for (const f of immutableFacts) {
      lines.push(`- \`${f.key}\`: ${f.value}`);
    }
    lines.push("");
  } else {
    lines.push("⚠ No immutable vision facts set. Consider adding mission/north-star facts with `--permanence immutable`.");
    lines.push("");
  }

  // --- Role-specific context ---
  if (role === "dev") {
    return lines.join("\n") + _devContext(db);
  }
  return lines.join("\n") + _generalContext(db);
}

function _devContext(db) {
  const lines = [];

  // Last session
  const lastSession = db.prepare(
    "SELECT id, start_time, end_time, exit_reason, summary, version_bump FROM sessions ORDER BY start_time DESC LIMIT 1"
  ).get();

  if (lastSession) {
    const ver = lastSession.version_bump ? ` — v${lastSession.version_bump}` : "";
    const reason = lastSession.exit_reason || "open";
    lines.push(`**Last Session:** ${lastSession.start_time}${ver} — "${lastSession.summary || "No summary"}" (${reason} exit)`);
    lines.push("");
  }

  // Interrupted session recovery
  const interrupted = db.prepare(
    `SELECT id, start_time, summary FROM sessions
     WHERE exit_reason IN ('interrupted', 'crashed') AND end_time > datetime('now', '-24 hours')
     ORDER BY end_time DESC LIMIT 1`
  ).get();

  if (interrupted) {
    lines.push("**⚠ INTERRUPTED SESSION DETECTED:**");
    lines.push(`Session ${interrupted.start_time} ended without closeout.`);
    if (interrupted.summary) lines.push(`Last known state: ${interrupted.summary}`);
    const snap = db.prepare(
      "SELECT current_task_id, modified_files, git_diff_stat FROM snapshots WHERE session_id = ? ORDER BY timestamp DESC LIMIT 1"
    ).get(interrupted.id);
    if (snap) {
      if (snap.current_task_id) {
        const task = db.prepare("SELECT title FROM tasks WHERE id = ?").get(snap.current_task_id);
        lines.push(`- Working on: [${snap.current_task_id}] ${task?.title || "unknown"}`);
      }
      if (snap.modified_files && snap.modified_files !== "[]") {
        lines.push(`- Modified files: ${JSON.parse(snap.modified_files).join(", ")}`);
      }
      if (snap.git_diff_stat) lines.push(`- Git diff: ${snap.git_diff_stat}`);
    }
    lines.push("Review and decide whether to resume or start fresh.");
    lines.push("");
  }

  // Active tasks (full table, max 15)
  const activeTasks = db.prepare(
    "SELECT id, status, priority, component, title FROM tasks WHERE status NOT IN ('done', 'cancelled') ORDER BY priority DESC, id"
  ).all();

  if (activeTasks.length > 0) {
    lines.push(`**Active Tasks (${activeTasks.length}):**`);
    lines.push("");
    lines.push("| ID | Status | Pri | Component | Title |");
    lines.push("|----|--------|-----|-----------|-------|");
    for (const t of activeTasks.slice(0, 15)) {
      lines.push(`| ${t.id} | ${t.status} | ${t.priority} | ${t.component || "-"} | ${t.title} |`);
    }
    if (activeTasks.length > 15) lines.push(`_...and ${activeTasks.length - 15} more_`);
    lines.push("");
  }

  // Recent facts (recency + confidence filtered)
  const facts = db.prepare(
    "SELECT key, value, confidence, domain FROM facts WHERE last_confirmed_at > datetime('now', '-7 days') AND confidence >= 0.5 AND permanence != 'immutable' ORDER BY confidence DESC, last_confirmed_at DESC LIMIT 5"
  ).all();

  if (facts.length > 0) {
    lines.push(`**Recent Facts (${facts.length}):**`);
    for (const f of facts) {
      const domain = f.domain ? ` [${f.domain}]` : "";
      lines.push(`- \`${f.key}\` (${f.confidence}): ${f.value}${domain}`);
    }
    lines.push("");
  }

  // Recent decisions (3)
  const decisions = db.prepare(
    "SELECT title, chosen, rationale, status FROM decisions WHERE status = 'active' ORDER BY created_at DESC LIMIT 3"
  ).all();

  if (decisions.length > 0) {
    lines.push(`**Recent Decisions (${decisions.length}):**`);
    for (const d of decisions) lines.push(`- "${d.title}" — ${d.chosen} [${d.status}]`);
    lines.push("");
  }

  return lines.join("\n");
}

function _generalContext(db) {
  const lines = [];

  // Persistent facts (architecture truths)
  const persistentFacts = db.prepare(
    "SELECT key, value FROM facts WHERE permanence = 'persistent' ORDER BY key"
  ).all();

  if (persistentFacts.length > 0) {
    lines.push(`**Architecture Facts (persistent, ${persistentFacts.length}):**`);
    for (const f of persistentFacts) {
      lines.push(`- \`${f.key}\`: ${f.value}`);
    }
    lines.push("");
  }

  // Recent decisions (5)
  const decisions = db.prepare(
    "SELECT title, chosen, rationale, status FROM decisions WHERE status = 'active' ORDER BY created_at DESC LIMIT 5"
  ).all();

  if (decisions.length > 0) {
    lines.push(`**Recent Decisions (${decisions.length}):**`);
    for (const d of decisions) lines.push(`- "${d.title}" — ${d.chosen} [${d.status}]`);
    lines.push("");
  }

  // Task summary (count only, no table)
  const taskCount = db.prepare(
    "SELECT COUNT(*) as count FROM tasks WHERE status NOT IN ('done', 'cancelled')"
  ).get();
  const componentCounts = db.prepare(
    "SELECT component, COUNT(*) as count FROM tasks WHERE status NOT IN ('done', 'cancelled') GROUP BY component"
  ).all();

  if (taskCount.count > 0) {
    const components = componentCounts.map(c => `${c.component || "general"}: ${c.count}`).join(", ");
    lines.push(`**Task Summary:** ${taskCount.count} active across ${componentCounts.length} components (${components})`);

    // Show in_progress tasks as brief detail
    const inProgress = db.prepare(
      "SELECT id, title FROM tasks WHERE status = 'in_progress' ORDER BY priority DESC"
    ).all();
    if (inProgress.length > 0) {
      for (const t of inProgress) lines.push(`  - [${t.id}] ${t.title} (in progress)`);
    }
    lines.push("");
  }

  // Open explorations
  const explorations = db.prepare(
    "SELECT id, title, type, status FROM explorations WHERE status IN ('open', 'proposed') ORDER BY created_at DESC LIMIT 10"
  ).all();

  if (explorations.length > 0) {
    lines.push(`**Open Explorations (${explorations.length}):**`);
    for (const e of explorations) {
      lines.push(`- [${e.id}] ${e.title} (${e.type}, ${e.status})`);
    }
    lines.push("");
  }

  // Last session
  const lastSession = db.prepare(
    "SELECT start_time, summary, version_bump, exit_reason FROM sessions ORDER BY start_time DESC LIMIT 1"
  ).get();

  if (lastSession) {
    const ver = lastSession.version_bump ? ` — v${lastSession.version_bump}` : "";
    const reason = lastSession.exit_reason || "open";
    lines.push(`**Last Session:** ${lastSession.start_time}${ver} — "${lastSession.summary || "No summary"}" (${reason} exit)`);
    lines.push("");
  }

  return lines.join("\n");
}
```

**`.ava/lib/renderer.mjs`:**
```javascript
// renderer.mjs — Delimiter-based markdown renderer
import fs from "fs";
import path from "path";
import { requireDb, AVA_DIR } from "./db.mjs";

const PROJECT_DIR = path.resolve(AVA_DIR, "..");

const DELIMITER_REGEX =
  /<!-- DAL:AUTO:BEGIN (\w+) -->\n[\s\S]*?\n<!-- DAL:AUTO:END \1 -->/g;

const SECTION_RENDERERS = {
  status(db) {
    const lastSession = db.prepare(
      "SELECT version_bump, start_time, summary FROM sessions WHERE version_bump IS NOT NULL ORDER BY start_time DESC LIMIT 1"
    ).get();
    const taskCounts = db.prepare("SELECT status, COUNT(*) as c FROM tasks GROUP BY status").all();
    const counts = {};
    let total = 0;
    for (const r of taskCounts) { counts[r.status] = r.c; total += r.c; }
    const ver = lastSession?.version_bump || "?.?.?";
    const date = lastSession?.start_time?.split(" ")[0] || new Date().toISOString().split("T")[0];
    const summary = lastSession?.summary || "No session summary";
    return [
      "_Auto-generated from brain.db — do not edit manually._", "",
      `**Version:** ${ver} | **Updated:** ${date} | **Tasks:** ${total} (${counts.done || 0} done, ${counts.in_progress || 0} active, ${counts.blocked || 0} blocked, ${counts.not_started || 0} pending)`, "",
      `**Latest:** ${summary}`,
    ].join("\n");
  },

  active_tasks(db) {
    const tasks = db.prepare(
      "SELECT id, status, priority, component, title FROM tasks WHERE status NOT IN ('done', 'cancelled') ORDER BY priority DESC, id"
    ).all();
    if (tasks.length === 0) return "_Auto-generated from brain.db._\n\nNo active tasks.";
    const lines = ["_Auto-generated from brain.db._", "", "| ID | Status | Pri | Component | Title |", "|----|--------|-----|-----------|-------|"];
    for (const t of tasks) lines.push(`| ${t.id} | ${t.status} | ${t.priority} | ${t.component || "-"} | ${t.title} |`);
    return lines.join("\n");
  },

  session_log(db) {
    const sessions = db.prepare("SELECT * FROM sessions ORDER BY start_time DESC LIMIT 5").all();
    if (sessions.length === 0) return "_Auto-generated from brain.db._\n\nNo sessions recorded.";
    const lines = ["_Auto-generated from brain.db — last 5 sessions shown._", ""];
    for (const s of sessions) {
      const ver = s.version_bump ? ` — v${s.version_bump}` : "";
      lines.push(`### Session ${s.start_time}${ver} (${s.exit_reason || "open"})`);
      lines.push(s.summary || "No summary.");
      lines.push("");
    }
    return lines.join("\n").trimEnd();
  },

  handoff(db) {
    const lastSession = db.prepare("SELECT summary, version_bump, start_time FROM sessions ORDER BY start_time DESC LIMIT 1").get();
    const inProgress = db.prepare("SELECT id, title, component FROM tasks WHERE status = 'in_progress' ORDER BY priority DESC").all();
    const lines = ["_Auto-generated from brain.db._", ""];
    if (lastSession) {
      const ver = lastSession.version_bump ? ` (v${lastSession.version_bump})` : "";
      lines.push(`**Last session${ver}:** ${lastSession.summary || "No summary"}`);
      lines.push("");
    }
    if (inProgress.length > 0) {
      lines.push("**In progress:**");
      for (const t of inProgress) lines.push(`- [${t.id}] ${t.title}${t.component ? ` (${t.component})` : ""}`);
    } else {
      lines.push("No tasks currently in progress.");
    }
    return lines.join("\n");
  },

  version_history(db) {
    const sessions = db.prepare(
      "SELECT version_bump, start_time, summary FROM sessions WHERE version_bump IS NOT NULL ORDER BY start_time DESC LIMIT 10"
    ).all();
    if (sessions.length === 0) return "_Auto-generated from brain.db._\n\nNo version history.";
    const lines = ["_Auto-generated from brain.db — last 10 versions shown._", "", "| Version | Date | Summary |", "|---------|------|---------|"];
    for (const s of sessions) {
      const date = s.start_time?.split(" ")[0] || "unknown";
      lines.push(`| ${s.version_bump} | ${date} | ${(s.summary || "No summary").slice(0, 120)} |`);
    }
    return lines.join("\n");
  },

  active_decisions(db) {
    const decisions = db.prepare("SELECT * FROM decisions WHERE status = 'active' ORDER BY created_at DESC").all();
    if (decisions.length === 0) return "_Auto-generated from brain.db._\n\nNo active decisions.";
    const lines = ["_Auto-generated from brain.db — active architectural decisions._", ""];
    for (const d of decisions) {
      lines.push(`### ${d.title}`);
      lines.push(`**Context:** ${d.context}`);
      lines.push(`**Chosen:** ${d.chosen}`);
      lines.push(`**Rationale:** ${d.rationale}`);
      lines.push("");
    }
    return lines.join("\n").trimEnd();
  },

  known_issues(db) {
    const facts = db.prepare(
      `SELECT key, value, confidence, domain FROM facts
       WHERE (domain = 'tech-debt' OR tags LIKE '%"bug"%' OR tags LIKE '%"issue"%')
         AND confidence >= 0.3
       ORDER BY confidence DESC, created_at DESC LIMIT 10`
    ).all();
    if (facts.length === 0) return "_Auto-generated from brain.db — top 10 by severity._\n\nNo known issues.";
    const lines = ["_Auto-generated from brain.db — top 10 by severity._", ""];
    for (const f of facts) {
      const domain = f.domain ? `, domain: ${f.domain}` : "";
      lines.push(`- **${f.key}** (confidence: ${f.confidence}${domain}) — ${f.value}`);
    }
    return lines.join("\n");
  },
};

export function renderFile(filePath) {
  const db = requireDb();
  const fullPath = path.isAbsolute(filePath) ? filePath : path.join(PROJECT_DIR, filePath);
  if (!fs.existsSync(fullPath)) return { file: filePath, sectionsRendered: 0, error: "File not found" };

  const content = fs.readFileSync(fullPath, "utf8");
  let sectionsRendered = 0;
  let backedUp = false;

  const newContent = content.replace(DELIMITER_REGEX, (match, sectionName) => {
    const renderer = SECTION_RENDERERS[sectionName];
    if (!renderer) return match;
    sectionsRendered++;
    return `<!-- DAL:AUTO:BEGIN ${sectionName} -->\n${renderer(db)}\n<!-- DAL:AUTO:END ${sectionName} -->`;
  });

  if (newContent !== content) {
    const timestamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
    fs.copyFileSync(fullPath, `${fullPath}.bak-${timestamp}`);
    backedUp = true;
    fs.writeFileSync(fullPath, newContent, "utf8");
  }

  return { file: filePath, sectionsRendered, backed_up: backedUp };
}

export function renderAll() {
  return ["CLAUDE.md", path.join("documentation", "IMPLEMENTATION_PLAN.md"), path.join("documentation", "PROJECT_ROADMAP.md")]
    .map((f) => renderFile(f));
}

export function bootstrapDelimiters() {
  // Insert DAL:AUTO delimiters into known markdown sections.
  // This is project-specific — adapt the header names to match YOUR doc structure.
  // The reference implementation looks for headers like "## Known Bugs / Tech Debt",
  // "## Current Status", "## 10. Version History", etc.
  // For a new project, you may need to customize this function or manually add delimiters.
  const results = [];

  // CLAUDE.md — known_issues after "## Known Bugs / Tech Debt"
  const claudePath = path.join(PROJECT_DIR, "CLAUDE.md");
  if (fs.existsSync(claudePath)) {
    const result = insertDelimitersAfterHeader(claudePath, "## Known Bugs / Tech Debt", "known_issues");
    results.push({ file: "CLAUDE.md", inserted: result ? [result] : [] });
  }

  return results;
}

function insertDelimitersAfterHeader(filePath, header, sectionName) {
  let content = fs.readFileSync(filePath, "utf8");
  const beginTag = `<!-- DAL:AUTO:BEGIN ${sectionName} -->`;
  if (content.includes(beginTag)) return null;

  const hIdx = content.indexOf(header);
  if (hIdx === -1) return null;

  const afterHeader = hIdx + header.length;
  const rest = content.slice(afterHeader);
  const nextSection = rest.match(/\n(?=## )/);
  const sectionEnd = nextSection ? afterHeader + nextSection.index : content.length;

  const timestamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
  fs.copyFileSync(filePath, `${filePath}.bak-${timestamp}`);

  content = content.slice(0, afterHeader) +
    `\n\n${beginTag}\n_Pending first render._\n<!-- DAL:AUTO:END ${sectionName} -->\n` +
    content.slice(sectionEnd);

  fs.writeFileSync(filePath, content, "utf8");
  return sectionName;
}
```

### Step 5: Create `dal.mjs` (the CLI entry point)

```javascript
#!/usr/bin/env node
// dal.mjs v2.0.0 — Durable Agentic Layer CLI
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const DAL_VERSION = "2.0.0";

const args = process.argv.slice(2);
const command = args[0];
const subcommand = args[1];

function parseFlags(startIdx = 2) {
  const flags = {};
  let positional = null;
  for (let i = startIdx; i < args.length; i++) {
    if (args[i].startsWith("--")) {
      const key = args[i].slice(2);
      const val = args[i + 1] && !args[i + 1].startsWith("--") ? args[i + 1] : "true";
      flags[key] = val;
      if (val !== "true") i++;
    } else if (!positional) {
      positional = args[i];
    }
  }
  return { flags, positional };
}

async function main() {
  try {
    switch (command) {
      case "bootstrap": return await cmdBootstrap();
      case "session": return await cmdSession();
      case "task": return await cmdTask();
      case "fact": return await cmdFact();
      case "decision": return await cmdDecision();
      case "note": return await cmdNote();
      case "context": return await cmdContext();
      case "render": return await cmdRender();
      case "status": return await cmdStatus();
      case "version": return await cmdVersion();
      case "migrate": return await cmdMigrate();
      default: printUsage(); process.exit(command ? 1 : 0);
    }
  } catch (err) {
    process.stderr.write(`Error: ${err.message}\n`);
    process.exit(1);
  }
}

async function cmdBootstrap() {
  const { getDb, schemaVersion } = await import("./lib/db.mjs");
  const db = getDb({ create: true });
  const sv = schemaVersion();
  console.log(`Created .ava/brain.db (schema v${sv})`);
}

async function cmdSession() {
  const { startSession, closeSession, listSessions } = await import("./lib/sessions.mjs");
  const { flags } = parseFlags(2);
  switch (subcommand) {
    case "start": { console.log(startSession({ id: flags.id, model: flags.model })); break; }
    case "close": {
      const id = closeSession({
        summary: flags.summary, exitReason: flags["exit-reason"], version: flags.version,
        filesModified: flags["files-modified"], tasksCompleted: flags["tasks-completed"], tasksCreated: flags["tasks-created"],
      });
      console.log(`Session closed: ${id}`); break;
    }
    case "list": {
      const sessions = listSessions({ limit: parseInt(flags.limit || "10", 10) });
      if (sessions.length === 0) { console.log("No sessions found."); return; }
      console.log(padRight("ID", 38) + padRight("Start", 22) + padRight("Exit", 14) + "Summary");
      for (const s of sessions) {
        console.log(padRight(s.id.slice(0, 36), 38) + padRight(s.start_time || "-", 22) + padRight(s.exit_reason || "open", 14) + (s.summary ? s.summary.slice(0, 60) : "-"));
      }
      break;
    }
    default: console.log("Usage: dal.mjs session <start|close|list> [--flags]"); process.exit(1);
  }
}

async function cmdTask() {
  const { addTask, updateTask, listTasks, taskTree, formatTaskTree } = await import("./lib/tasks.mjs");
  const { flags, positional } = parseFlags(2);
  switch (subcommand) {
    case "add": {
      const id = addTask({ id: flags.id, title: flags.title, description: flags.description, parent: flags.parent, component: flags.component, priority: parseInt(flags.priority || "0", 10), assignedAgent: flags["assigned-agent"], status: flags.status || "not_started", blockedBy: flags["blocked-by"] });
      console.log(`Task added: ${id}`); break;
    }
    case "update": {
      if (!positional) { console.log("Usage: dal.mjs task update <id> --status <status>"); process.exit(1); }
      const updates = {};
      if (flags.status) updates.status = flags.status;
      if (flags.title) updates.title = flags.title;
      if (flags.description) updates.description = flags.description;
      if (flags.priority) updates.priority = parseInt(flags.priority, 10);
      if (flags["assigned-agent"]) updates.assignedAgent = flags["assigned-agent"];
      if (flags["blocked-by"]) updates.blockedBy = flags["blocked-by"];
      if (flags.component) updates.component = flags.component;
      updateTask(positional, updates);
      console.log(`Task updated: ${positional}`); break;
    }
    case "list": {
      const format = flags.format || "table";
      const tasks = listTasks({ status: flags.status, component: flags.component, includeCompleted: flags.all === "true" });
      if (format === "json") { console.log(JSON.stringify(tasks, null, 2)); }
      else {
        if (tasks.length === 0) { console.log("No tasks found."); return; }
        console.log(padRight("ID", 8) + padRight("Status", 16) + padRight("Pri", 5) + padRight("Component", 14) + "Title");
        for (const t of tasks) console.log(padRight(t.id, 8) + padRight(t.status, 16) + padRight(String(t.priority), 5) + padRight(t.component || "-", 14) + t.title);
      }
      break;
    }
    case "tree": {
      const tasks = taskTree({ root: flags.root });
      if (tasks.length === 0) { console.log("No tasks found."); return; }
      console.log(formatTaskTree(tasks)); break;
    }
    default: console.log("Usage: dal.mjs task <add|update|list|tree> [--flags]"); process.exit(1);
  }
}

async function cmdFact() {
  const { setFact, searchFacts, listFacts, confirmFact, deleteFact, auditFacts, pruneFacts } = await import("./lib/facts.mjs");
  const { flags, positional } = parseFlags(2);
  switch (subcommand) {
    case "set": {
      if (!positional || !flags.value) { console.log("Usage: dal.mjs fact set <key> --value <value> [--permanence <tier>]"); process.exit(1); }
      const result = setFact({ key: positional, value: flags.value, confidence: parseFloat(flags.confidence || "1.0"), domain: flags.domain, tags: flags.tags, permanence: flags.permanence });
      console.log(`Fact ${result.action}: ${result.key}`); break;
    }
    case "search": {
      if (!positional) { console.log("Usage: dal.mjs fact search <query>"); process.exit(1); }
      const results = searchFacts(positional);
      if (results.length === 0) { console.log("No facts found."); return; }
      for (const f of results) console.log(`  ${f.key} (${f.confidence}): ${f.value}`);
      break;
    }
    case "list": {
      const facts = listFacts({ domain: flags.domain, minConfidence: parseFloat(flags["min-confidence"] || "0.0"), permanence: flags.permanence });
      if (facts.length === 0) { console.log("No facts found."); return; }
      for (const f of facts) console.log(`  ${f.key} (${f.confidence}) [${f.permanence || "standard"}]: ${f.value}`);
      break;
    }
    case "confirm": {
      if (!positional) { console.log("Usage: dal.mjs fact confirm <key>"); process.exit(1); }
      confirmFact(positional); console.log(`Fact confirmed: ${positional}`); break;
    }
    case "delete": {
      if (!positional) { console.log("Usage: dal.mjs fact delete <key>"); process.exit(1); }
      deleteFact(positional); console.log(`Fact deleted: ${positional}`); break;
    }
    case "audit": {
      const result = auditFacts();
      console.log("Fact Audit:");
      console.log(`  Unclassified (standard): ${result.unclassified.length}`);
      for (const f of result.unclassified.slice(0, 10)) console.log(`    ${f.key}: ${f.value.slice(0, 80)}`);
      if (result.unclassified.length > 10) console.log(`    ...and ${result.unclassified.length - 10} more`);
      console.log(`  Stale (>90 days, not immutable/persistent): ${result.stale.length}`);
      for (const f of result.stale.slice(0, 10)) console.log(`    ${f.key} — last confirmed: ${f.last_confirmed_at}`);
      console.log(`  Ephemeral past window (3+ sessions): ${result.ephemeralPastWindow.length}`);
      for (const f of result.ephemeralPastWindow) console.log(`    ${f.key} (${f.sessions_since} sessions since)`);
      break;
    }
    case "prune": {
      const dryRun = flags.execute !== "true";
      const result = pruneFacts({ dryRun });
      if (result.dryRun) {
        console.log(`Dry run — ${result.count} ephemeral facts would be pruned:`);
        for (const c of result.candidates) console.log(`  ${c.key} (${c.sessions_since} sessions since last confirm)`);
        if (result.count > 0) console.log("Run with --execute to delete.");
        else console.log("Nothing to prune.");
      } else {
        console.log(`Pruned ${result.deleted} ephemeral facts.`);
      }
      break;
    }
    default: console.log("Usage: dal.mjs fact <set|search|list|confirm|delete|audit|prune>"); process.exit(1);
  }
}

async function cmdDecision() {
  const { addDecision, listDecisions, supersedeDecision } = await import("./lib/decisions.mjs");
  const { flags, positional } = parseFlags(2);
  switch (subcommand) {
    case "add": {
      if (!flags.title || !flags.context || !flags.chosen || !flags.rationale) { console.log("Usage: dal.mjs decision add --title T --context C --chosen O --rationale R"); process.exit(1); }
      const id = addDecision({ title: flags.title, context: flags.context, chosen: flags.chosen, rationale: flags.rationale, alternatives: flags.alternatives, component: flags.component });
      console.log(`Decision added: #${id}`); break;
    }
    case "list": {
      const decisions = listDecisions({ component: flags.component, status: flags.status || "active" });
      if (decisions.length === 0) { console.log("No decisions found."); return; }
      for (const d of decisions) console.log(`  #${d.id} [${d.status}] ${d.title} → ${d.chosen}`);
      break;
    }
    case "supersede": {
      const oldId = positional ? parseInt(positional, 10) : null;
      const newId = flags.by ? parseInt(flags.by, 10) : null;
      if (!oldId || !newId) { console.log("Usage: dal.mjs decision supersede <id> --by <new-id>"); process.exit(1); }
      supersedeDecision(oldId, newId); console.log(`Decision #${oldId} superseded by #${newId}`); break;
    }
    default: console.log("Usage: dal.mjs decision <add|list|supersede>"); process.exit(1);
  }
}

async function cmdNote() {
  const { listAllNotes, listNotesByTab, noteCounts } = await import("./lib/notes.mjs");
  const { flags, positional } = parseFlags(2);
  switch (subcommand) {
    case "list": {
      const tab = positional || flags.tab;
      if (tab) {
        const { notes } = listNotesByTab(tab);
        if (notes.length === 0) { console.log(`No notes for tab "${tab}".`); return; }
        for (const n of notes) console.log(`  ${n.completed ? "✓" : "○"} [${n.category}] ${n.text.slice(0, 100)}`);
      } else {
        const all = listAllNotes();
        const tabs = Object.keys(all);
        if (tabs.length === 0) { console.log("No notes."); return; }
        for (const t of tabs) {
          const open = all[t].notes.filter((n) => !n.completed).length;
          console.log(`  ${padRight(t, 20)} ${open} open / ${all[t].notes.length} total`);
        }
      }
      break;
    }
    case "counts": {
      const { byTab, total } = noteCounts();
      console.log(`Open notes: ${total}`);
      for (const row of byTab) console.log(`  ${padRight(row.tab_key, 20)} ${padRight(row.category, 14)} ${row.count}`);
      break;
    }
    default: console.log("Usage: dal.mjs note <list|counts> [tab]"); process.exit(1);
  }
}

async function cmdContext() {
  const { generateContext } = await import("./lib/context.mjs");
  const { flags } = parseFlags(1);
  const role = flags.role || "general";
  console.log(generateContext({ role }));
}

async function cmdRender() {
  const { renderFile, renderAll } = await import("./lib/renderer.mjs");
  const { flags } = parseFlags(1);
  if (flags.file) {
    const result = renderFile(flags.file);
    if (result.error) { console.log(`Error: ${result.error}`); process.exit(1); }
    console.log(`Rendered ${result.file}: ${result.sectionsRendered} sections`);
  } else {
    for (const r of renderAll()) {
      console.log(`  ${r.file}: ${r.error || `${r.sectionsRendered} sections`}`);
    }
  }
}

async function cmdStatus() {
  const { integrityCheck, schemaVersion, dbSize, requireDb } = await import("./lib/db.mjs");
  const { taskCounts, staleBlockers } = await import("./lib/tasks.mjs");
  const { sessionCounts } = await import("./lib/sessions.mjs");
  const { factCounts } = await import("./lib/facts.mjs");
  const { decisionCounts } = await import("./lib/decisions.mjs");
  const { noteCounts: noteCountsFn } = await import("./lib/notes.mjs");

  requireDb();
  const integrity = integrityCheck();
  console.log("DAL Status (brain.db)");
  console.log(`  Schema version: ${schemaVersion()}`);
  console.log(`  DB size: ${Math.round(dbSize() / 1024)} KB`);
  console.log(`  Integrity: ${integrity.ok ? "ok" : `FAILED — "${integrity.result}"`}`);
  if (!integrity.ok) return;

  console.log("");
  const tc = taskCounts();
  console.log(`  Tasks: ${tc.total} total`);
  console.log(`  Sessions: ${JSON.stringify(sessionCounts())}`);
  console.log(`  Facts: ${JSON.stringify(factCounts())}`);
  console.log(`  Decisions: ${JSON.stringify(decisionCounts())}`);
  console.log(`  Notes: ${noteCountsFn().total} open`);

  const stale = staleBlockers();
  if (stale.length > 0) {
    console.log("");
    for (const s of stale) console.log(`  Stale blocker: ${s.taskId} blocked by ${s.blockerId} (${s.blockerStatus})`);
  }
}

async function cmdVersion() {
  const dbExists = fs.existsSync(path.join(__dirname, "brain.db"));
  let sv = "unknown";
  if (dbExists) { try { const { schemaVersion } = await import("./lib/db.mjs"); sv = schemaVersion(); } catch { sv = "error"; } }
  console.log(`dal.mjs v${DAL_VERSION} | schema v${sv} | brain.db exists: ${dbExists ? "yes" : "no"}`);
}

async function cmdMigrate() {
  const { getDb, schemaVersion } = await import("./lib/db.mjs");
  const db = getDb({ create: false });
  if (!db) { console.log("No brain.db found. Run 'dal.mjs bootstrap' first."); process.exit(1); }
  console.log(`Schema version: ${schemaVersion()}`);
  console.log("Migrations up to date.");
}

function padRight(str, len) { return (str || "").toString().padEnd(len); }

function printUsage() {
  console.log(`dal.mjs v${DAL_VERSION} — Durable Agentic Layer CLI

Commands:
  bootstrap                      Initialize brain.db
  session start|close|list       Manage sessions
  task add|update|list|tree      Manage tasks
  fact set|search|list|confirm|delete|audit|prune  Manage facts
  fact list --permanence <tier>  Filter facts by permanence tier
  decision add|list|supersede    Manage decisions
  note list|counts               View notes
  context [--role general|dev]   Generate init context payload
  render [--file F]              Render auto-generated markdown sections from DB
  status                         Show DB status and counts
  version                        Show version info
  migrate                        Run pending migrations`);
}

main().catch((err) => { process.stderr.write(`Fatal: ${err.message}\n`); process.exit(1); });
```

### Step 6: Create the SessionStart hook

**`.claude/hooks/log-util.js`:**
```javascript
// log-util.js — hook usage logging (optional, can be a no-op)
function logHook(hookName) {
  // Minimal version — just logs to a local file
  const fs = require("fs");
  const path = require("path");
  try {
    const logDir = process.env.CLAUDE_PROJECT_DIR || path.resolve(__dirname, "../..");
    const logFile = path.join(logDir, ".claude", "hooks", "hook-log.jsonl");
    const entry = JSON.stringify({ tool_name: hookName, tool_type: "hook", action: "hook_fire", timestamp: new Date().toISOString() });
    fs.appendFileSync(logFile, entry + "\n");
  } catch { /* hooks must never block */ }
}
module.exports = { logHook };
```

**`.claude/hooks/session-context.js`:**
```javascript
const { execSync } = require("child_process");
const path = require("path");
const fs = require("fs");
const { logHook } = require("./log-util");
logHook("session-context");

const projectDir = process.env.CLAUDE_PROJECT_DIR || path.resolve(__dirname, "../..");
const context = [];

function run(cmd) {
  try { return execSync(cmd, { cwd: projectDir, encoding: "utf8", timeout: 5000 }).trim(); }
  catch { return null; }
}

// Git context
const branch = run("git branch --show-current");
if (branch) context.push("Branch: " + branch);

const status = run("git status --short");
if (status) context.push("Uncommitted changes:\n" + status);

const log = run("git log --oneline -5");
if (log) context.push("Recent commits:\n" + log);

const unpushed = run("git log --oneline origin/" + (branch || "main") + "..HEAD 2>/dev/null");
if (unpushed) context.push("Unpushed commits:\n" + unpushed);

// Closeout reminder
const closeoutPaths = [
  path.join(projectDir, "documentation", ".prompts", "closeout.md"),
];
for (const p of closeoutPaths) {
  if (fs.existsSync(p)) {
    context.push("REMINDER: Run /session-closeout at end of significant sessions. Closeout prompt located at: " + path.relative(projectDir, p));
    break;
  }
}

// DAL context injection
const brainDbPath = path.join(projectDir, ".ava", "brain.db");
if (fs.existsSync(brainDbPath)) {
  try {
    const dalContext = execSync(
      `node "${path.join(projectDir, ".ava", "dal.mjs")}" context`,
      { cwd: projectDir, encoding: "utf8", timeout: 10000 }
    ).trim();
    if (dalContext) {
      context.push("## DAL State (auto-injected from brain.db)\n\n" + dalContext);
      context.push(
        "NOTE: DAL state loaded from brain.db. " +
        "IMPLEMENTATION_PLAN.md and PROJECT_ROADMAP.md are rendered views of this data — " +
        "you do not need to read them unless brain.db was unavailable. " +
        "CLAUDE.md (auto-loaded) contains the prescriptive rules you still need."
      );
    }
  } catch {
    context.push(
      "DAL: brain.db exists but context query failed. " +
      "Run 'node .ava/dal.mjs status' to check DB health."
    );
  }
}

if (context.length > 0) {
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: "SessionStart",
      additionalContext: context.join("\n\n"),
    },
  }));
}
```

### Step 7: Add the hook to `.claude/settings.json`

Add this to your existing settings.json (merge into whatever hooks already exist):

```json
{
  "hooks": {
    "SessionStart": [
      {
        "matcher": "startup|resume",
        "hooks": [
          {
            "type": "command",
            "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/session-context.js\"",
            "statusMessage": "Loading project context..."
          }
        ]
      }
    ]
  }
}
```

### Step 8: Bootstrap and verify

```bash
cd /path/to/McQueenyML
cd .ava && npm install && cd ..
node .ava/dal.mjs bootstrap
node .ava/dal.mjs status
node .ava/dal.mjs version
```

Expected output:
```
Created .ava/brain.db (schema v2)
DAL Status (brain.db)
  Schema version: 2
  DB size: ~40 KB
  Integrity: ok
  Tasks: 0 total
  ...
```

### Step 9: Add `.ava/brain.db*` to `.gitignore`

```
# DAL
.ava/brain.db
.ava/brain.db-*
.ava/node_modules/
```

The runtime code (`.ava/dal.mjs`, `.ava/lib/`, `.ava/migrations/`, `.ava/package.json`) SHOULD be committed — it's project infrastructure. Only the database and its WAL/SHM files are gitignored.

---

## How It's Used in Practice

**Session start:** The `session-context.js` hook auto-fires → runs `dal.mjs context` → injects tasks/facts/decisions/session history into the conversation context.

**During work:** The agent (or you via CLI) records state:
```bash
node .ava/dal.mjs session start
node .ava/dal.mjs task add --id "feature-auth" --title "Add auth system" --priority 5 --component api
node .ava/dal.mjs fact set jwt-secret --value "Stored in .env, rotated monthly" --domain security
node .ava/dal.mjs decision add --title "Use JWT over sessions" --context "Need stateless auth" --chosen "JWT with refresh tokens" --rationale "Scales horizontally"
```

**Session close:**
```bash
node .ava/dal.mjs session close --summary "Added JWT auth + refresh tokens" --version "0.2.0" --exit-reason normal
node .ava/dal.mjs render  # Updates markdown docs with DB content
```

**Next session:** Hook fires, new session gets full context automatically.

---

## Adaptation Notes for McQueenyML

1. The `renderer.mjs` `bootstrapDelimiters()` function looks for specific markdown headers (like `"## Known Bugs / Tech Debt"`, `"## Current Status"`, `"## 10. Version History"`). If your doc structure uses different headers, either:
   - Manually add `<!-- DAL:AUTO:BEGIN section_name -->` / `<!-- DAL:AUTO:END section_name -->` delimiters to your markdown files
   - Or modify the renderer's section mappings to match your actual headers

2. The notes system uses `tab_key` for grouping — this was designed for Ava's tabbed PWA. For McQueenyML you can use any string as the tab_key (e.g., "general", "ml-pipeline", "data", etc.) or skip notes entirely.

3. The `log-util.js` in this guide is a simplified version. The original logs to an HTTP endpoint (Ava's prompt-log API). The version here just writes to a local JSONL file.
