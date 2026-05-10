# Adze.Mesh

Pure-managed mesh I/O and analysis. **No SOLIDWORKS interop dependencies** — this assembly compiles and runs anywhere .NET Framework 4.8 does, including unit tests, the `Adze.UiHarness` dev shell, and (eventually) any cross-CAD port (Fusion 360, Onshape, etc.).

Phase 11 of the Adze roadmap. Foundation for the mesh-to-solid track and any agent operations that need to reason about geometry from imported scan / mesh data.

## What's in here

| Folder | Purpose |
|---|---|
| `Models/` | `Vector3d`, `Triangle`, `Mesh` (triangle soup), `AxisAlignedBoundingBox`, `MeshStatistics`, `MeshAnalysis`, `PlaneFit`, `PrimitiveFitResults`. Doubles (not floats) because CAD coordinate ranges + downstream primitive fitting need precision. |
| `Io/` | `StlReader` — binary + ASCII STL with format auto-detect. Handles the "binary STL whose 80-byte header starts with `solid`" trap (compares declared count × 50 + 84 against file size; only treats as ASCII if size doesn't match). |
| `Analysis/` | `MeshStatsCalculator` (bbox / area / volume / manifold / closure / consistency), `MeshAnalyzer` (convertibility verdict + recommended import strategy + advisories), `PrimitiveFitter` (deterministic normal-clustering plane detection). |

## Quick reference

```csharp
// Read an STL
Mesh mesh = StlReader.ReadFromFile(@"C:\models\bracket.stl");

// Compute statistics
MeshStatistics stats = MeshStatsCalculator.Compute(mesh);
//   stats.TriangleCount, stats.BoundingBox, stats.SurfaceArea,
//   stats.EnclosedVolume, stats.IsClosed, stats.IsConsistentlyOriented,
//   stats.OpenEdgeCount, stats.NonManifoldEdgeCount, stats.DegenerateTriangleCount

// Higher-level analysis with convertibility verdict + advisories
MeshAnalysis analysis = MeshAnalyzer.Analyze(mesh, includePrimitives: true);
//   analysis.Convertibility ∈ { Direct, NeedsRepair, NeedsReverseEngineering, NotConvertible }
//   analysis.RecommendedStrategy ∈ { GraphicsBodyOnly, GraphicsThenConvert, ScanTo3DAutoSurface, AdzeReverseEngineer }
//   analysis.Primitives.Planes — detected planar regions if includePrimitives
//   analysis.Advisories — human-readable strings for the preview card

// Primitive fitting alone
PrimitiveFitResults primitives = PrimitiveFitter.Fit(mesh);
//   primitives.Planes — list of PlaneFit (Normal, Offset, InlierTriangleIndices, InlierAreaFraction)
//   primitives.TotalCoveredAreaFraction — how much of the surface area landed on a primitive
```

## Algorithms — design notes

### STL format detection
The standard "starts with `solid`" check is unreliable: many binary exporters write `solid <name>` into the 80-byte header. We use the size heuristic instead:

1. Files smaller than 84 bytes → ASCII (a valid binary STL can't be smaller).
2. Files starting with `solid` → check declared triangle count: if `header_and_count_size + count × 50 == file_size`, it's binary despite the prefix.
3. Otherwise binary.

### Manifold and closure detection
Two dictionaries built in one O(T) pass:

- **Undirected edge dictionary** keyed on sorted vertex pair → count of triangles using that edge. Edge counts of 1 = open edge (boundary), 2 = manifold interior, 3+ = non-manifold (T-junction or self-intersection).
- **Directed edge dictionary** keyed on (from, to) ordered pair → count. In a properly oriented closed manifold, every directed edge appears exactly once. Duplicates indicate inconsistent winding (one triangle's normal will point inward).

Closed = no open edges AND no non-manifold edges. Consistent orientation = no directed-edge duplicates.

### Volume from divergence theorem
Each triangle contributes `V₀ · (V₁ × V₂) / 6` to the signed enclosed volume. Summing across a closed manifold yields the volume; sign is positive when triangles face outward and negative when inward. `EnclosedVolume = |SignedVolume|` is reported regardless; sign mismatch on a closed mesh surfaces an advisory ("normals are likely inverted").

### Plane detection (deterministic, RANSAC-free)
Normal-clustering greedy approach:

1. Compute per-triangle unit normal, area, centroid.
2. Sort triangles by descending area.
3. Pick the largest unassigned triangle. Its normal seeds a candidate plane.
4. Claim every unassigned triangle whose unit normal is within 8° of the seed normal AND whose centroid is within 0.5% of the bbox diagonal of the candidate plane.
5. Refit the plane via area-weighted least squares using the inliers.
6. Mark inliers assigned, repeat from step 3 until inlier-area fraction falls below the threshold or `MaxPlanes` reached.

This trades RANSAC's noise robustness for determinism + speed on CAD-exported geometry, which is the primary input. Unit-cube test: detects all 6 faces with 100% area coverage. Open-box test: detects 4 faces.

For noisy scan data (point clouds disguised as meshes, surface tessellations from photogrammetry), RANSAC will land in a future session as a separate fitter without disturbing this baseline.

## Tests

`tests/Adze.Tests/Mesh/` covers:

- `StlReaderTests` (9) — binary + ASCII parsing, format detection edge cases, missing-file handling, vertex coordinate fidelity
- `MeshStatsCalculatorTests` (6) — empty mesh, unit cube area / volume / closure / bbox, open-box detection
- `MeshAnalyzerTests` (4) — Direct / NeedsRepair verdicts, advisories
- `PrimitiveFitterTests` (5) — empty mesh, unit-cube 6-plane detection, axis-alignment, per-plane triangle count, open-box partial coverage

Test fixtures (`MeshTestFixtures`) build STL byte arrays procedurally — no binary assets committed to the repo.

## Boundaries

This project must NOT:

- Reference any `SolidWorks.Interop.*` assembly.
- Take a dependency on `Adze.Host`, `Adze.Tools`, or any other Adze.* project. Mesh code is consumed by them, not the other way around.
- Add NuGet dependencies without explicit decision capture. The point of this assembly is "deploy-anywhere mesh primitives"; pulling in a heavy meshing library would defeat that.

If geometry primitives need to grow beyond planes (cylinders, spheres, tori), that's a `PrimitiveFitter` extension or a sibling `CylinderFitter` class — not a new assembly.
