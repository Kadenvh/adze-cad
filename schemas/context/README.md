# Context Schema

Holds the broker-facing `SessionContext` schema — the contract for what the SOLIDWORKS add-in captures at every session-start / document-change / selection-change event, and the shape every read tool consumes.

## Files

| File | Purpose |
|---|---|
| `session-context.schema.json` | Top-level shape of `SessionContext` plus every slice (`session`, `environment`, `document`, `selection`, `feature_tree`, `configurations`, `dimensions`, `mates`, `reference_graph`, `properties`, `diagnostics`, `policy`) |

## Why this matters

The `SessionContext` is the **single ground-truth payload** every grounding tool reads. Adding a new slice or field touches three places at once:

1. `src/Adze.Contracts/Models/SessionContext.cs` — the C# class
2. `src/Adze.Host/Infrastructure/HostState.cs` (`BuildContextUnsafe`) — the live-SW capture path
3. `schemas/context/session-context.schema.json` — this schema

If those three drift, the broker prompt composer sends one shape, tools expect another, and snapshots replayed in `Adze.UiHarness` blow up. The schema validator (`pwsh scripts\setup\validate-json-schemas.ps1`) catches structural breaks; ModelJsonMapper round-trip tests catch serialization breaks.

## Slices

| Slice | Captures |
|---|---|
| `session` | RequestId, timestamp, approval state, user mode |
| `environment` | SW version, add-in version, machine name, diagnostics mode |
| `document` | Type / title / path / active config / units / dirty / read-only |
| `selection` | Currently selected entities, kinds, owners |
| `feature_tree` | Feature nodes with name / kind / state, anchored slice |
| `configurations` | Active config + full list with active flag |
| `dimensions` | All dimensions with values + driven state, paginated |
| `mates` | Assembly mates with kind / status / entity count, paginated |
| `reference_graph` | Direct + transitive references, broken count |
| `properties` | Document + configuration custom properties |
| `diagnostics` | Rebuild state, warnings, missing references |
| `policy` | Enabled tools, trust tier, exploration percent |

## Validation

```powershell
pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1
```

Validates every JSON schema in the repo, including this one.

## Cross-refs

- `schemas/tools/tool-result-envelope.schema.json` — what tools return
- `src/Adze.Contracts/Models/SessionContext.cs` — C# class that mirrors this schema
- `src/Adze.Trace/Serialization/ModelJsonMapper.cs` — round-trip serialization
- `tests/Adze.Tests/Trace/ModelJsonMapperRoundTripTests.cs` — schema-equivalence tests
- `plans/project-index.md` §3 — schema directory inventory
