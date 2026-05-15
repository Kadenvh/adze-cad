# Adze.Index

Closed-file SOLIDWORKS metadata indexer. Reads `.SLDPRT`, `.SLDASM`, and `.SLDDRW` files **without opening them in SOLIDWORKS** by parsing the OLE Structured Storage layer directly via the `OpenMcdf` NuGet package. Powers the `search_project_files` retrieval tool.

**Type:** Library (.NET Framework 4.8). AssemblyVersion 1.1.0.0.

## Why this exists

SOLIDWORKS files are OLE compound documents. Their `\005SummaryInformation` and custom-property streams hold author, title, keywords, comments, and custom properties — all readable without round-tripping through SW COM. Adze's agent can search large project folders for files matching keyword / property criteria in seconds, no SW launch required.

## Layout

```
src/Adze.Index/
├── OlePropertyReader.cs       # Low-level OLE compound document → typed properties
├── OlePropertySetParser.cs    # Property-set stream parser (FMTID / property IDs)
├── ClosedFileIndexer.cs       # Walks a folder, builds + persists a JSON index
└── ClosedFileSearchService.cs # Queries the index by keyword / property / type / path
```

## Dependencies

- `Adze.Contracts` — `ClosedFileRecord`, `ClosedFileSearchQuery`, `ClosedFileSearchResult`, `SearchProjectFilesParameters`, `IndexRunResult`
- `OpenMcdf` 2.3.1 NuGet (referenced via `packages\OpenMcdf.2.3.1\lib\net40\OpenMcdf.dll`)

**Note on OpenMcdf:** 2.3.1 → 3.1.2 has a breaking-changes major version jump (see brain.db note `xfuot4440t`). Migration is non-trivial; deferred until justified by feature need.

## Persistent index location

The indexer writes its JSON cache under `%LOCALAPPDATA%\Adze\state\closed-file-index\<folder-hash>.json`. Subsequent searches of the same folder reuse the cached metadata when files haven't changed (mtime + size check).

## How it's used

The `search_project_files` tool (in `src/Adze.Tools/Grounding/SearchProjectFilesTool.cs`) wraps `ClosedFileIndexer` + `ClosedFileSearchService`. Gated behind `SOLIDWORKS_AI_RETRIEVAL=true`.

```csharp
var indexer = new ClosedFileIndexer();
indexer.BuildIndex(rootFolderPath);

var search = new ClosedFileSearchService(indexer);
var matches = search.Search(rootFolderPath, new ClosedFileSearchQuery {
    Keyword = "bracket",
    FileType = "part",
    MaxResults = 20
});
```

## Conventions

- **Never call SW COM here.** OLE access is the whole point — keep COM out so this stays usable when SW isn't running.
- **Tolerate malformed property sets.** Real-world SW files have weird metadata; the parser logs warnings and returns partial results rather than throwing.
- **Trust mtime + size for cache invalidation.** Hashing every closed file on every search would defeat the purpose of caching.

## Cross-refs

- `plans/project-index.md` §1 — project inventory
- `plans/research-closed-file-retrieval.md` (archive) — original feasibility brief
- `src/Adze.Tools/Grounding/SearchProjectFilesTool.cs` — the agent-callable tool that wraps this
- `tests/Adze.Tests/Index/OleIndexerTests.cs` — automated tests
- brain.db note `xfuot4440t` — OpenMcdf 3.x migration analysis
