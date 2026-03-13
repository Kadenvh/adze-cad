# Host Services

This directory contains the host-side builders that turn live SOLIDWORKS state into Task Pane output and typed session context.

## Contents

| File | Purpose |
|------|---------|
| `GroundingAnswerBuilder.cs` | Builds the grounded assistant-response text shown in the Task Pane |
| `SessionContextBuilder.cs` | Builds the broker-facing `SessionContext` from the live SOLIDWORKS session |
| `GroundingDashboardBuilder.cs` | Builds the Task Pane status snapshot, summary text, and session guidance |
| `GroundingExecutionService.cs` | Executes the broker-recommended read-only tool plan and returns a typed report |
| `GroundingPlanBuilder.cs` | Builds the structured broker-plan preview text shown in the host |
| `GroundingSynthesisService.cs` | Feeds executed tool results through the model path for grounded answer synthesis and falls back to the deterministic answer builder |

## Conventions

- Keep these services focused on data shaping, not UI event handling.
- Build compact context objects instead of exposing raw COM details to the rest of the system.
- Update schemas and contracts when a context field changes here.

## Adding New Items

- Add a new builder or formatter only when it serves a distinct host-side data-shaping role.
- Keep direct persistence and UI state changes outside this directory.
