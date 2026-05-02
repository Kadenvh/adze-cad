# Tool Schemas

This directory contains the request contracts for the 18 production tools (10 read + 1 retrieval + 7 write) plus the shared result envelope.

## Contents

### Read (action_class = green)
| File | Purpose |
|------|---------|
| `get_active_document.request.schema.json` | Active-document inspection |
| `get_document_summary.request.schema.json` | Summary and metadata |
| `get_selection_context.request.schema.json` | Current-selection inspection |
| `get_feature_tree_slice.request.schema.json` | Feature-tree slice |
| `get_dimensions.request.schema.json` | Dimension inspection |
| `get_configurations.request.schema.json` | Configuration list |
| `get_custom_properties.request.schema.json` | Custom-property inspection |
| `get_mates.request.schema.json` | Assembly-mate inspection |
| `get_rebuild_diagnostics.request.schema.json` | Rebuild diagnostics |
| `get_reference_graph.request.schema.json` | Dependency/reference graph |

### Retrieval (action_class = green)
| File | Purpose |
|------|---------|
| `search_project_files.request.schema.json` | Closed-file OLE search by keyword/property/type/path |

### Write — first-wave (action_class = yellow)
| File | Purpose |
|------|---------|
| `set_custom_property.request.schema.json` | Set/update a document or configuration custom property |
| `set_dimension_value.request.schema.json` | Set a dimension's value (rebuild required) |
| `suppress_feature.request.schema.json` | Suppress a feature (rebuild required, cascade risk) |
| `unsuppress_feature.request.schema.json` | Unsuppress a feature (rebuild required) |
| `rename_object.request.schema.json` | Rename a feature |

### Write — Class 3 advanced (action_class = red)
| File | Purpose |
|------|---------|
| `insert_component.request.schema.json` | Insert a component into an assembly |
| `create_drawing_view.request.schema.json` | Create a standard drawing view (front/iso/etc.) |

### Envelope
| File | Purpose |
|------|---------|
| `tool-result-envelope.schema.json` | Common response envelope for all tool output |

## Conventions

- `tool_name` values must match `ToolNames.cs`.
- `action_class` reflects risk: `green` (read-only), `yellow` (low-risk write), `red` (advanced write — Class 3, elevated confirmation UI).
- Parameter property names use `snake_case` even though C# parameter classes use `PascalCase` (the broker maps between the two).
- Add request parameters only when the host and tool handler can actually honor them.

## Adding New Items

- Add the request schema for the new tool.
- Update `ToolNames.cs`, the matching request/response contracts, and the tool handler.
- Extend validation scripts and benchmarks before considering the tool usable.
