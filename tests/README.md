# Tests

This directory is the reserved boundary for compiled test projects.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `contracts/` | Placeholder boundary for future schema, serializer, and contract tests |

## Conventions

- The current executable validation path lives in `scripts/setup/`.
- Add compiled tests here only after the runtime surface and tool contracts are stable enough to justify them.

## Adding New Items

- Create a new test project per stable boundary such as `contracts`, `grounding`, or `integration`.
- Keep the README coverage in sync when a new test boundary is added.
