# Tool Schemas

This directory contains the request contracts for the current Wave 1 grounding tools plus the shared result envelope.

## Contents

| File | Purpose |
|------|---------|
| `get_active_document.request.schema.json` | Active-document inspection request |
| `get_document_summary.request.schema.json` | Summary and metadata request |
| `get_selection_context.request.schema.json` | Current-selection inspection request |
| `get_feature_tree_slice.request.schema.json` | Feature-tree slice request |
| `get_dimensions.request.schema.json` | Dimension inspection request |
| `get_configurations.request.schema.json` | Configuration-list request |
| `get_custom_properties.request.schema.json` | Custom-property inspection request |
| `get_mates.request.schema.json` | Assembly-mate inspection request |
| `get_rebuild_diagnostics.request.schema.json` | Rebuild-diagnostic request |
| `get_reference_graph.request.schema.json` | Dependency/reference-graph request |
| `tool-result-envelope.schema.json` | Common response envelope for tool output |

## Conventions

- `tool_name` values must match `ToolNames.cs`.
- All current tool requests are read-only and therefore use `action_class = green`.
- Add request parameters only when the host and tool handler can actually honor them.

## Adding New Items

- Add the request schema for the new tool.
- Update `ToolNames.cs`, the matching request/response contracts, and the tool handler.
- Extend validation scripts and benchmarks before considering the tool usable.
