# Tests

This directory contains compiled test projects for the Adze solution.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `Adze.Tests/` | NUnit 3 unit tests covering broker, tools, and trace layers (130 tests) |
| `contracts/` | Placeholder boundary for future schema and contract tests |

## Running

```powershell
pwsh -NoProfile -File scripts\setup\run-tests.ps1
```

## Conventions

- The current executable validation path lives in `scripts/setup/`.
- Add compiled tests here per stable boundary such as `contracts`, `grounding`, or `integration`.
- Keep the README coverage in sync when a new test boundary is added.
