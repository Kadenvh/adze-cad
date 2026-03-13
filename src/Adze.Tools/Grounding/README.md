# Grounding Tools

This directory contains the ten live read-only Wave 1 grounding tool handlers.

## Contents

| File | Purpose |
|------|---------|
| `GetActiveDocumentTool.cs` | Returns the active-document identity and basic state |
| `GetDocumentSummaryTool.cs` | Returns summary metadata such as type, path, units, and configuration |
| `GetSelectionContextTool.cs` | Returns the current selection preview |
| `GetFeatureTreeSliceTool.cs` | Returns a bounded feature-tree slice |
| `GetDimensionsTool.cs` | Returns bounded display-dimension data |
| `GetConfigurationsTool.cs` | Returns available configurations and the active configuration |
| `GetCustomPropertiesTool.cs` | Returns document and custom-property data |
| `GetMatesTool.cs` | Returns assembly-mate summaries |
| `GetRebuildDiagnosticsTool.cs` | Returns rebuild state and warnings |
| `GetReferenceGraphTool.cs` | Returns direct and transitive dependency information |

## Conventions

- Keep handlers read-only at this stage.
- Prefer bounded result sets so the host context remains compact.
- Match handler names, tool names, contracts, and request schemas consistently.

## Adding New Items

- Add the handler file here.
- Update `ToolCatalog.cs`, contracts, schemas, benchmarks, and progression rules together.
- Treat a tool as incomplete until it is reachable from the host and covered by validation.
