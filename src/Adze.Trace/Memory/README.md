# Adze.Trace/Memory

Per-document and per-user memory storage for cross-session continuity.

| Class | Purpose | Storage |
|-------|---------|---------|
| `DocumentMemory` | Learned patterns per document (key dims, workflows, issues, intents) | `%LOCALAPPDATA%\Adze\memory\{hash}\` |
| `UserPreferenceMemory` | Answer mode, verbosity, focus areas, diagnostics preference | `%LOCALAPPDATA%\Adze\state\user-preferences.json` |
| `MemoryStore` | Load/save/record operations for both memory types | — |

Document key is a SHA256 hash of the lowercase document path (first 16 hex chars).
