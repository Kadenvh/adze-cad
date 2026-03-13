# Contract Tests

This directory is reserved for compiled tests that validate schema and contract behavior once the runtime surface stabilizes.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `README.md` | Documents the intended contract-test boundary until executable tests are added |

## Conventions

- Keep executable schema checks in `scripts/setup/validate-json-schemas.ps1` for now.
- Move stable contract assertions here once a compiled test harness is introduced.

## Adding New Items

- Add tests for contract-model parity, serializer behavior, and schema compatibility.
- Keep fixture data small and checked into the repo.
