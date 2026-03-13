# Notes System — Implementation Reference

Reference for implementing a per-context notes/task-tracking system backed by `brain.db`.

> **Standard:** Notes use `brain.db` (DAL migration 002 adds the `notes` table).
> The DAL CLI provides `note list` and `note counts` commands.
> The REST API surface works against `brain.db` via the DAL notes module.

---

## Architecture Overview

Three layers: **`brain.db` persistence** (server via DAL) + **REST API** (4 endpoints) + **React hook + popup UI** (client).

```
User types note
    -> React hook (useTabNotes) updates local state immediately
    -> Debounced POST (500ms) saves to server
    -> Server writes to brain.db notes table via DAL
```

Notes are **scoped to a context key** (e.g., a page, project, or module). All contexts
are stored in the `notes` table, keyed by `tab_key`. This keeps the system trivially
simple while supporting arbitrary scoping.

---

## 1. Data Model

### Note Object

```typescript
type NoteCategory = "improvement" | "issue" | "bug" | "idea";

interface Note {
  id: string;          // Generated: Date.now().toString(36) + Math.random().toString(36).slice(2, 8)
  category: NoteCategory;
  text: string;
  createdAt: number;   // Date.now() timestamp
  completed: boolean;
}
```

### Database Schema (DAL migration 002)

```sql
CREATE TABLE IF NOT EXISTS notes (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    tab_key     TEXT NOT NULL,
    category    TEXT NOT NULL CHECK (category IN ('improvement', 'issue', 'bug', 'idea')),
    text        TEXT NOT NULL,
    completed   INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_notes_tab ON notes(tab_key);
CREATE INDEX IF NOT EXISTS idx_notes_category ON notes(tab_key, category);
CREATE INDEX IF NOT EXISTS idx_notes_completed ON notes(completed);
```

### Counts (derived, not stored)

```typescript
interface NoteCounts {
  improvement: number;
  issue: number;
  bug: number;
  idea: number;
  total: number;
}
```

Computed client-side from uncompleted notes only. Used for badge counts in the UI.

---

## 2. Client Hook (React)

The hook manages all CRUD locally and debounces saves to the server. This means the
UI feels instant — no loading spinners on add/toggle/remove.

### Key Design Decisions

- **Optimistic updates**: State changes immediately, server save is async + debounced
- **500ms debounce**: Rapid edits (typing, toggling multiple) batch into one save
- **Full array replacement**: POST sends the entire notes array, not individual diffs.
  Simpler than PATCH operations, and the array is small (typically <50 notes)
- **ID generation**: `Date.now().toString(36) + Math.random().toString(36).slice(2, 8)`
  avoids `crypto.randomUUID()` which requires HTTPS context

### Hook Interface

```typescript
function useNotes(contextKey: string): {
  notes: Note[];
  counts: NoteCounts;         // Derived from uncompleted notes
  addNote: (category: NoteCategory, text: string) => void;
  toggleNote: (id: string) => void;
  removeNote: (id: string) => void;
  updateNote: (id: string, text: string) => void;
  clearCompleted: () => void;
  loaded: boolean;
}
```

### Implementation Pattern

```typescript
function useNotes(contextKey: string) {
  const [notes, setNotes] = useState<Note[]>([]);
  const [loaded, setLoaded] = useState(false);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Load on mount / context change
  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoaded(false);
      try {
        const r = await fetch(`/api/notes/${encodeURIComponent(contextKey)}`);
        const data = await r.json();
        if (!cancelled) setNotes(data.notes ?? []);
      } catch {
        if (!cancelled) setNotes([]);
      } finally {
        if (!cancelled) setLoaded(true);
      }
    }
    load();
    return () => { cancelled = true; };
  }, [contextKey]);

  // Debounced save
  const scheduleSave = useCallback((nextNotes: Note[]) => {
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => {
      fetch(`/api/notes/${encodeURIComponent(contextKey)}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ notes: nextNotes }),
      }).catch(() => {});
    }, 500);
  }, [contextKey]);

  // Cleanup timer on unmount
  useEffect(() => {
    return () => { if (saveTimer.current) clearTimeout(saveTimer.current); };
  }, []);

  // CRUD operations — all follow the same pattern:
  // 1. Update local state
  // 2. Schedule debounced save with the new array
  const addNote = useCallback((category: NoteCategory, text: string) => {
    const note: Note = {
      id: Date.now().toString(36) + Math.random().toString(36).slice(2, 8),
      category, text, createdAt: Date.now(), completed: false,
    };
    setNotes((prev) => { const next = [note, ...prev]; scheduleSave(next); return next; });
  }, [scheduleSave]);

  const toggleNote = useCallback((id: string) => {
    setNotes((prev) => {
      const next = prev.map((n) => n.id === id ? { ...n, completed: !n.completed } : n);
      scheduleSave(next);
      return next;
    });
  }, [scheduleSave]);

  const removeNote = useCallback((id: string) => {
    setNotes((prev) => { const next = prev.filter((n) => n.id !== id); scheduleSave(next); return next; });
  }, [scheduleSave]);

  const updateNote = useCallback((id: string, text: string) => {
    setNotes((prev) => {
      const next = prev.map((n) => n.id === id ? { ...n, text } : n);
      scheduleSave(next);
      return next;
    });
  }, [scheduleSave]);

  const clearCompleted = useCallback(() => {
    setNotes((prev) => prev.filter((n) => !n.completed));
    fetch(`/api/notes/${encodeURIComponent(contextKey)}/clear-completed`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "{}",
    }).catch(() => {});
  }, [contextKey]);

  // Derived counts (uncompleted only)
  const counts = useMemo<NoteCounts>(() => {
    const active = notes.filter((n) => !n.completed);
    return {
      improvement: active.filter((n) => n.category === "improvement").length,
      issue: active.filter((n) => n.category === "issue").length,
      bug: active.filter((n) => n.category === "bug").length,
      idea: active.filter((n) => n.category === "idea").length,
      total: active.length,
    };
  }, [notes]);

  return { notes, counts, addNote, toggleNote, removeNote, updateNote, clearCompleted, loaded };
}
```

---

## 3. UI Component (Floating Popup)

The popup is a **fixed-position overlay** triggered by a button in the top-right corner.
It works on every page without needing per-page integration.

### Structure

```
[Notes button] (fixed top-right, shows badge count)
    |
    v (click)
[Popup panel] (fixed, 320px wide, max 70vh tall)
    +-- Header (accent dot + "Notes" + context key)
    +-- Category sub-tabs (Improvements | Issues | Bugs | Ideas) with counts
    +-- Scrollable note list (filtered by active category)
    |     +-- Each note: checkbox + text + relative time + delete (hover)
    +-- Add input (text field + Add button, Enter to submit)
    +-- Footer (X completed — Clear completed button)
```

### Key UI Behaviors

- **Close on Escape** or click outside the panel
- **Auto-focus** input when popup opens
- **Category sub-tabs** filter the visible list; add input scopes to active category
- **Completed notes** show with strikethrough + reduced opacity
- **Delete button** appears on hover only (reduces visual noise)
- **Relative timestamps**: "just now", "5m ago", "2h ago", "3d ago"
- **Badge on trigger button**: Shows total uncompleted count, changes color when > 0

---

## 4. Testing

### Smoke Test Pattern (curl)

```bash
BASE="http://localhost:YOUR_PORT"

# Save a note
curl -s -X POST "$BASE/api/notes/test-context" \
  -H "Content-Type: application/json" \
  -d '{"notes":[{"id":"test1","category":"bug","text":"test note","createdAt":1700000000000,"completed":false}]}' \
  | grep -q '"ok":true'

# Read it back
curl -s "$BASE/api/notes/test-context" | grep -q '"test note"'

# Mark completed and clear
curl -s -X POST "$BASE/api/notes/test-context" \
  -H "Content-Type: application/json" \
  -d '{"notes":[{"id":"test1","category":"bug","text":"test note","createdAt":1700000000000,"completed":true}]}'

curl -s -X POST "$BASE/api/notes/test-context/clear-completed" \
  -H "Content-Type: application/json" -d '{}' \
  | grep -q '"removed":1'

# Get all contexts
curl -s "$BASE/api/notes" | grep -q "test-context"

# Cleanup
curl -s -X POST "$BASE/api/notes/test-context" \
  -H "Content-Type: application/json" -d '{"notes":[]}'
```

### Input Validation

- Context key must match `/^[a-zA-Z0-9_-]+$/` — rejects paths, dots, spaces
- `notes` body field must be an array — rejects objects, strings, nulls
- Missing context returns `{ notes: [], updatedAt: 0 }` (not 404)
