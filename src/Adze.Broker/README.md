# Adze.Broker

This project contains the orchestration boundary that reasons over typed session context without touching SOLIDWORKS COM directly.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `Abstractions/` | Broker interface definitions |
| `Clients/` | Model-provider transport clients |
| `Configuration/` | Environment-driven model settings and broker config helpers |
| `Formatting/` | Prompt/context formatting helpers |
| `Models/` | Broker turn, recommendation, and future model payloads |
| `Orchestration/` | Deterministic and hybrid broker turn planners |
| `Adze.Broker.csproj` | Broker project definition |

## Conventions

- Keep this layer free of COM, registry, and UI dependencies.
- Consume `SessionContext` and emit typed turn state, recovery guidance, recommendations, or future execution plans.
- Treat provider access as API-key-based custom-app usage; do not assume Claude subscription plans apply here.
- Prefer small, testable orchestration units over host-coupled logic.

## Adding New Items

- Add new broker payloads under `Models/`.
- Add new provider clients under `Clients/`.
- Add environment or provider settings under `Configuration/`.
- Add new orchestration strategies under `Orchestration/` behind the shared abstraction.
- Keep prompt-formatting concerns in `Formatting/`, not in the host or tools.
