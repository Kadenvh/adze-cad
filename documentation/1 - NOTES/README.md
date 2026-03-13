# 1 - NOTES — Deployment Reference

Deployment playbook for the Notes system. Consult this when implementing notes in a new project.

## Contents

| File | Description |
|------|-------------|
| `notes-system-reference.md` | Complete Notes system: data model, API pattern, client hook, UI component, and testing guide |

## When to Use

Use `notes-system-reference.md` as the reference when:
- Adding note-taking capability to a project's DAL
- Building a notes UI component
- Implementing the `useTabNotes()` hook pattern

## Architecture Note

Two distinct notes systems exist in this ecosystem — do not conflate them:

| System | Storage | Access | Scope |
|--------|---------|--------|-------|
| DAL notes (per-project) | Each project's `.ava/brain.db` | `node .ava/dal.mjs note ...` | Project-scoped |
| ava_hub PWA notes (UI) | Ava_Main's `brain.db` via REST API | `useTabNotes()` hook in PWA | Tab UI only |
