# Adze.Tools/Write

First-wave write tool implementations. Each tool implements `IWriteTool<TParams>` with Preview, Apply, Verify, and BuildUndoLabel.

| Tool | Parameters | Rebuild? | Risk |
|------|-----------|----------|------|
| `SetCustomPropertyTool` | `SetCustomPropertyParameters` | No | Lowest |
| `SetDimensionValueTool` | `SetDimensionValueParameters` | Yes | Low |
| `SuppressFeatureTool` | `SuppressFeatureParameters` | Yes | Medium (cascade) |
| `UnsuppressFeatureTool` | `UnsuppressFeatureParameters` | Yes | Low |

Feature-gated behind `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true`. Apply methods use `dynamic` COM dispatch — the `object application` parameter is cast to ISldWorks at the Host layer.
