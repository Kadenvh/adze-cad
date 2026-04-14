# Research: Closed-File Retrieval for SOLIDWORKS Metadata

**Date:** 2026-03-15
**Status:** Research complete
**Purpose:** Determine what SOLIDWORKS file metadata can be read without opening files through full COM automation, and recommend an architecture for project-level indexing

---

## Context

Adze currently captures rich context from the **active open document** via SOLIDWORKS COM inside the in-process add-in (see `SessionContextBuilder.cs`). The data collected includes:

- Document info (type, title, path, active configuration, units, dirty/read-only state)
- Feature tree (name, kind, suppression state)
- Dimensions (name, full name, value, unit source)
- Configurations (names, active flag)
- Custom properties (document-level and configuration-level, with resolution and link status)
- Mates (name, kind, entity count, component names)
- Reference graph (direct and transitive dependencies, broken references)
- Diagnostics (rebuild state, warnings, missing references)
- File properties (name, directory, extension, size, last-write time)

Phase 6 of the end-goal (`END-GOAL.md`) calls for "Retrieval without COM: Index closed SOLIDWORKS file metadata (custom properties, feature names, dimension names) by reading OLE structured storage from the file format directly." This research evaluates which approaches are viable and what data they expose.

---

## 1. OLE Structured Storage / Compound File Binary Format

### How It Works

SOLIDWORKS `.SLDPRT`, `.SLDASM`, and `.SLDDRW` files are **Microsoft OLE Structured Storage** (also called Compound Document / Compound Binary File) containers. This is the same container format used by older `.doc`, `.xls`, and `.ppt` files. The file contains multiple internal "streams" and "storages" organized in a hierarchy, much like a filesystem within a file.

### What Can Be Read

**Custom Properties and Summary Information (HIGH confidence, PROVEN approach):**

OLE Structured Storage files contain standard property sets that Windows itself can read:

| Property Set | OLE Stream | Contents |
|---|---|---|
| Summary Information | `\005SummaryInformation` | Title, subject, author, keywords, comments, template, last author, revision number, creation date, last save date, page count, word count, application name |
| Document Summary Information | `\005DocumentSummaryInformation` | Manager, company, category, plus a "User-Defined Properties" section |
| User-Defined Properties | Part of Document Summary Information | **All custom properties** stored at the document level |

This is the most reliable and well-understood extraction path. The Windows Shell, `StgOpenStorage` / `IPropertySetStorage` Win32 APIs, and multiple managed libraries can read these streams without any SOLIDWORKS dependency.

**What custom properties look like in practice:**
- Standard SOLIDWORKS custom properties (Description, Part Number, Material, Weight, etc.) are stored in the User-Defined property set
- Configuration-specific custom properties are stored differently (see limitations below)
- Linked/computed property values (e.g., `"SW-Material@Part1.SLDPRT"`) store the **expression**, not the resolved value, in the OLE stream

**Thumbnail / Preview Image:**

SOLIDWORKS stores a preview bitmap in the OLE stream. This can be extracted for visual indexing or search result display. The thumbnail is typically stored in the `\001CompObj` or a dedicated preview stream. Windows Explorer uses this for file thumbnail display.

### What Cannot Be Read (or Is Unreliable)

**Feature tree, dimensions, mates, sketch geometry, and model data:**

The actual parametric model data is stored in proprietary binary streams within the OLE container. SOLIDWORKS uses internal stream names like `SwDocMgrTempStorage`, `Contents`, and version-specific binary blobs. These are:

- Undocumented proprietary binary format
- Version-dependent (format changes between SOLIDWORKS releases)
- Not reliably parseable without SOLIDWORKS or the Document Manager API
- Reverse-engineering them would be fragile and unsupported

**Configuration-specific custom properties:**

Configuration-specific properties are stored within the model's proprietary data streams, not in the standard OLE property sets. They require either the Document Manager API or full COM to read.

**Resolved/computed property values:**

Properties that use SOLIDWORKS expressions (e.g., linked to mass, material, or dimension values) store the expression text in OLE, not the resolved value. Resolution requires SOLIDWORKS or Document Manager.

### .NET Implementation for OLE Reading

Several approaches for reading OLE Structured Storage from C#:

**Option A: OpenMcdf (recommended for v1)**

[OpenMcdf](https://github.com/ironfede/openmcdf) is a pure .NET library (no COM dependency) for reading and writing OLE Compound Files. It is:
- MIT-licensed
- Available as a NuGet package (`OpenMcdf`)
- Actively maintained
- Targets .NET Standard / .NET Framework
- Can read any named stream within the OLE container
- Does not require SOLIDWORKS or any Dassault dependency

To read summary/custom properties, you would:
1. Open the file with `CompoundFile.Open(path)`
2. Navigate to the `\005SummaryInformation` and `\005DocumentSummaryInformation` streams
3. Parse the property set binary format (Microsoft's documented OLEPS format)
4. Extract property names and values

**Option B: Win32 COM Interop (StgOpenStorage)**

The Win32 Structured Storage API (`StgOpenStorage`, `IPropertySetStorage`, `IPropertyStorage`) can be called via P/Invoke or COM interop:
- No external NuGet dependency
- Works on any Windows machine
- Well-documented by Microsoft
- Slightly more verbose to set up in C# but extremely reliable
- This is what Windows Explorer itself uses for property display

**Option C: WindowsAPICodePack / Microsoft.WindowsAPICodePack.Shell**

The Windows API Code Pack provides managed wrappers for shell property access, which can read OLE properties. However, this library has had maintenance gaps and may pull in unnecessary dependencies.

### Performance Characteristics

Reading OLE properties is fast. For a typical SOLIDWORKS file:
- File open + property read + close: **1-5 ms per file**
- Scanning 500 files in a project folder: **under 3 seconds**
- No SOLIDWORKS process needed, no COM registration needed
- Files can be read while SOLIDWORKS has them open (shared read access)
- Read-only access eliminates any risk of file corruption

---

## 2. SOLIDWORKS Document Manager API

### What It Is

The SOLIDWORKS Document Manager API (`SolidWorks.Interop.swdocumentmgr.dll`) is a separate, lightweight API provided by Dassault Systemes specifically for reading (and limited writing of) SOLIDWORKS file data **without launching SOLIDWORKS**. It runs out-of-process and does not require a SOLIDWORKS installation to be running.

### License Requirements

This is the critical constraint:

- The Document Manager API requires a **separate license key** obtained from SOLIDWORKS
- The key is a long string that must be compiled into the application or provided at runtime
- To obtain a key, you must:
  1. Have an active SOLIDWORKS subscription/maintenance agreement
  2. Submit a request through the SOLIDWORKS Customer Portal or API Support
  3. Agree to the Document Manager API license terms
  4. The key is typically issued per-application, not per-machine
- The key is **free** for SOLIDWORKS subscription holders but requires an explicit request
- Distribution: the key can be embedded in a redistributable application, but the DLL itself (`SolidWorks.Interop.swdocumentmgr.dll`) has redistribution restrictions
- The interop DLL ships with SOLIDWORKS installations and the SOLIDWORKS SDK

### What Data It Exposes

The Document Manager API provides significantly more data than raw OLE property reading:

| Data Category | Available | Notes |
|---|---|---|
| Custom properties (document-level) | Yes | Full read/write with resolved values |
| Custom properties (per-configuration) | Yes | Full read/write with resolved values |
| Configuration names | Yes | Full enumeration |
| Configuration properties (description, etc.) | Yes | Read access |
| External references / dependencies | Yes | File paths of referenced components |
| Feature count | Partial | Total count available, not full tree |
| Mass properties | Partial | If previously calculated and stored |
| Thumbnail / preview image | Yes | Bitmap extraction |
| Sheet metal data | Partial | Bend table, flat pattern info |
| Dimension names and values | No | Requires full COM |
| Feature tree (names, types, states) | No | Requires full COM |
| Sketch geometry | No | Requires full COM |
| Mate definitions | No | Requires full COM |
| Rebuild state | No | Requires full COM |
| Selection context | No | Requires full COM (and a live UI) |

### API Shape

```csharp
// Initialization requires the license key
SwDMApplication dmApp = new SwDMApplication();
// or via: SwDMClassFactory classFactory = new SwDMClassFactory();
//         SwDMApplication dmApp = classFactory.GetApplication(licenseKey);

SwDMDocument doc = dmApp.GetDocument(filePath, docType, readOnly, out openError);

// Custom properties
SwDMCustomPropertyManager propMgr = doc.GetCustomPropertyManager();
string[] names = propMgr.GetNames();
string value = propMgr.Get(propertyName);

// Configuration-specific properties
string[] configNames = doc.ConfigurationManager.GetConfigurationNames();
SwDMConfiguration config = doc.ConfigurationManager.GetConfigurationByName(configName);
SwDMCustomPropertyManager configPropMgr = config.GetCustomPropertyManager();

// External references
object[] deps = doc.GetAllExternalReferences4(out status, out paths);

doc.CloseDoc();
```

### Performance

- Initialization: ~50-100 ms for the first document
- Per-file property read: ~10-50 ms (slower than raw OLE, faster than full COM)
- Does not launch SOLIDWORKS GUI
- Can handle hundreds of files in seconds
- Separate process — no interference with a running SOLIDWORKS session

### Assembly vs Part vs Drawing Differences

| File Type | Doc Manager Coverage |
|---|---|
| Parts (.SLDPRT) | Custom properties, configurations, external refs, preview |
| Assemblies (.SLDASM) | Custom properties, configurations, external refs (component list), preview |
| Drawings (.SLDDRW) | Custom properties, sheet info, referenced model paths, preview |

For assemblies, the Document Manager can enumerate component references (which parts/sub-assemblies are used) but cannot traverse the mate structure or read assembly-level features.

---

## 3. Third-Party Libraries and Approaches

### SolidDNA / CADSharp

AngelSix's SolidDNA is a .NET wrapper for SOLIDWORKS COM, not for offline reading. It requires a running SOLIDWORKS instance. Not applicable for closed-file retrieval.

### xCAD.NET

xCAD.NET (by xarial) provides a higher-level .NET API for SOLIDWORKS that includes Document Manager support. It wraps both the full COM API and the Document Manager API behind a unified interface. If the project adopts Document Manager, xCAD.NET could reduce boilerplate, but it adds a dependency layer.

### eDrawings API

The eDrawings API can open and render SOLIDWORKS files without a full SOLIDWORKS license, but it is viewer-oriented (geometry display, markup) rather than metadata-oriented. It does not expose custom properties, configurations, or reference data in a programmatically useful way for indexing.

### Direct Binary Parsing

Some community tools and scripts have attempted to parse SOLIDWORKS binary streams directly. This approach is:
- Fragile across SOLIDWORKS versions
- Undocumented and unsupported
- Not recommended for a production product
- Only useful as a last resort for very specific extraction needs

### Windows Search / IFilter

SOLIDWORKS installs an IFilter that allows Windows Search to index SOLIDWORKS file properties. This is the same OLE property data. If Windows Search indexing is enabled on the project folder, the Windows Search API (`ISearchQueryHelper`) can be used to query already-indexed metadata. However:
- Depends on Windows Search service being configured
- Index freshness is not guaranteed
- Limited to the same property set available via OLE
- Adds an external dependency on Windows Search configuration

### PropertySystem / Shell Property Handlers

SOLIDWORKS registers shell property handlers that let Windows Explorer display custom properties in columns. The `IPropertyStore` / Windows Property System API can read these. This is essentially the same data as the OLE property path but accessed through the Windows Shell layer.

---

## 4. Data Accessibility Matrix

This table maps every data slice currently captured by `SessionContextBuilder` against each offline reading approach:

| Data Slice | OLE Properties | Document Manager | Full COM Only |
|---|---|---|---|
| Document type (part/asm/drw) | Yes (from extension) | Yes | Yes |
| Document title | Yes (Summary Info) | Yes | Yes |
| File path, size, dates | Yes (filesystem) | Yes (filesystem) | Yes |
| Active configuration | No | Yes | Yes |
| Configuration names | No | Yes | Yes |
| Configuration count | No | Yes | Yes |
| Custom properties (document) | Yes (values, not resolved) | Yes (resolved values) | Yes |
| Custom properties (per-config) | No | Yes | Yes |
| Feature tree (names, types) | No | No | Yes |
| Feature suppression states | No | No | Yes |
| Dimension names | No | No | Yes |
| Dimension values | No | No | Yes |
| Mates | No | No | Yes |
| Selection context | No | No | Yes (live UI) |
| Reference graph (direct) | No | Yes (partial) | Yes |
| Reference graph (transitive) | No | Yes (partial) | Yes |
| Broken references | No | Partial | Yes |
| Rebuild state | No | No | Yes |
| Units | No | Partial (stored property) | Yes |
| Preview thumbnail | Yes | Yes | Yes |
| Material | Partial (if custom prop) | Yes (if custom prop) | Yes |
| Mass properties | No | Partial (if cached) | Yes |

---

## 5. Limitations by File Type

### Parts (.SLDPRT)

Best coverage. OLE properties work well. Document Manager adds configurations and resolved properties. The main gaps (features, dimensions, sketches) require full COM regardless.

### Assemblies (.SLDASM)

OLE properties work. Document Manager can enumerate component references (which files are used). However:
- Mate structure is COM-only
- Component transforms/positions are COM-only
- Assembly feature tree is COM-only
- BOM-style data (quantity, instance count) requires either Document Manager with careful reference parsing or full COM

### Drawings (.SLDDRW)

Most limited for offline reading:
- OLE properties and Document Manager both work for custom properties
- Sheet names and count available via Document Manager
- View definitions, annotations, dimensions on the drawing sheet are COM-only
- The referenced model path is available via Document Manager
- Drawing-specific metadata (title block fields, revision tables) is mostly COM-only unless stored as custom properties

---

## 6. Semantic Retrieval Feasibility

### What "Semantic Retrieval Over Closed Files" Means for Adze

The goal is for a user to ask a question like "Which part has the 25mm bore?" or "Find all parts with Material = Aluminum 6061" and get results from files that are not currently open.

### What Is Realistic Now

**Keyword/property search (v1 — achievable with OLE only):**
- Search custom properties by name and value across a project folder
- Filter by file type, last modified date, file size
- Match property values against query terms
- Display results with file path, matching properties, and thumbnail

**Structured property search (v1+ — achievable with Document Manager):**
- All of the above, plus:
- Search across configuration-specific properties
- Filter by configuration name
- Enumerate component references to find "which assemblies use this part"
- Resolved property values (computed material, weight, etc.)

**Natural language over indexed metadata (v2 — achievable with model assistance):**
- Index the extracted metadata into a structured JSON store
- When the user asks a natural language question, the broker can search the index
- The model can interpret queries like "heaviest part" or "parts with more than 3 configurations"
- This works well for property-based queries but cannot answer geometry/dimension questions

### What Is Not Realistic Without Full COM

- "Find the part with a 25mm hole" — dimension data is not available offline
- "Which parts have suppressed fillets?" — feature state is not available offline
- "Show me parts with over-defined sketches" — rebuild diagnostics are COM-only
- Any query that requires geometric understanding, spatial relationships, or parametric model traversal

### Practical Assessment

Semantic retrieval over closed files is **realistic and valuable for property-based queries in an early version**. The indexed metadata (custom properties, configuration names, file references, thumbnails) covers a significant share of real-world "find me the right file" queries in engineering workflows. The gap — no feature/dimension/geometry data — is inherent to the file format and cannot be closed without either Document Manager (which still lacks features/dimensions) or batch-opening files through COM.

---

## 7. Architecture Recommendation

### v1: Minimal Viable Retrieval (OLE Properties Only)

**Approach:** Read OLE Structured Storage properties from closed files using OpenMcdf or Win32 `StgOpenStorage` interop. No SOLIDWORKS dependency. No license key needed.

**New project:** `src/Adze.Index` — a pure .NET library with no SOLIDWORKS interop references.

**Capabilities:**
- Scan a user-specified project folder for `.SLDPRT`, `.SLDASM`, `.SLDDRW` files
- Extract from each file:
  - Summary Information (title, author, subject, keywords, comments, creation date, last save date, application name/version)
  - Document Summary Information (company, manager, category)
  - User-Defined Properties (all document-level custom properties — raw values only)
  - File metadata (path, size, extension, last write time)
  - Thumbnail bitmap (for search result display)
- Store extracted metadata as JSON under `%LOCALAPPDATA%\Adze\index\{project-hash}\`
- Provide a query interface for the broker to search by property name, property value, file type, and file path patterns
- Incremental re-indexing: track file last-write timestamps, only re-read changed files
- Full re-index on demand

**Data contract (new, additive):**

```csharp
public sealed class ClosedFileRecord
{
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public string FileType { get; set; } // "part", "assembly", "drawing"
    public long FileSizeBytes { get; set; }
    public DateTimeOffset LastWriteUtc { get; set; }
    public DateTimeOffset? CreatedUtc { get; set; }
    public DateTimeOffset? LastSavedUtc { get; set; }
    public string? Author { get; set; }
    public string? Title { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public string? Comments { get; set; }
    public string? Company { get; set; }
    public string? ApplicationVersion { get; set; }
    public Dictionary<string, string> CustomProperties { get; set; }
    public bool HasThumbnail { get; set; }
    public DateTimeOffset IndexedAtUtc { get; set; }
}
```

**New grounding tool:** `search_project_files` — accepts a query (property name/value filter, file type filter, path pattern) and returns matching `ClosedFileRecord` entries. This tool runs against the local index, not against live files, so it is fast and safe.

**Performance target:** Index 500 files in under 5 seconds. Query response under 50 ms.

**Dependencies:** OpenMcdf NuGet package (MIT, pure .NET) or zero-dependency Win32 interop.

**Risk:** Low. Read-only file access. No SOLIDWORKS dependency. No license key. No COM. No network calls.

### v1.5: Document Manager Enhancement (Optional, If License Key Available)

**Approach:** If the user has a SOLIDWORKS Document Manager license key (available free to subscription holders), enhance the index with richer data.

**Additional capabilities beyond v1:**
- Configuration names and count per file
- Configuration-specific custom properties with resolved values
- Resolved document-level property values (expressions evaluated)
- External reference paths (component list for assemblies)
- "Which assemblies contain this part?" reverse-reference queries

**Implementation:** Add a `DocumentManagerIndexEnricher` that runs after the OLE pass when a Document Manager key is configured. Store the enriched data in the same `ClosedFileRecord` with additional fields.

**Configuration:** `SOLIDWORKS_AI_DOCMGR_KEY` environment variable. When absent, fall back to v1 OLE-only behavior. The `EnvironmentInfo.DocumentManagerAvailable` field already exists in the contracts.

**Risk:** Medium. Requires the user to obtain and configure a license key. The interop DLL must be present (ships with SOLIDWORKS). Adds a SOLIDWORKS SDK dependency to the index project.

### v2: Full Project Graph and Natural Language Search

**Approach:** Build a project-level graph from indexed metadata and reference relationships. Enable the broker to answer natural language questions about the project structure.

**Additional capabilities beyond v1.5:**
- Assembly-component graph: "What parts are in Assembly X?" / "Which assemblies use Part Y?"
- Property aggregation: "List all unique materials across this project"
- Change detection: "What files changed since last Tuesday?"
- Stale index warnings when files have been modified since indexing
- Natural language query routing: the broker interprets user questions and generates structured queries against the index
- Cross-file property comparison: "Compare Material property across all parts in folder X"

**New grounding tools:**
- `search_project_files` (enhanced with NL query interpretation)
- `get_project_graph` (returns assembly-component relationships)
- `get_project_statistics` (aggregate property summaries)

### v3: Background Indexing and Watch Mode

**Approach:** File system watcher for automatic re-indexing when files change. Background service that maintains the index without user intervention.

**Additional capabilities:**
- `FileSystemWatcher` on configured project folders
- Debounced re-indexing (wait for file writes to settle before re-reading)
- Index health monitoring in the Status tab
- Configurable folder inclusion/exclusion patterns
- Index size management and retention policy

---

## 8. Implementation Guidance

### Where v1 Fits in the Phase Plan

v1 closed-file retrieval maps to **Phase 6** in the end-goal, but the OLE-only approach is simple enough to begin earlier as a parallel workstream. It has no dependency on the agent loop (Phase 2), write tools (Phase 3), or learning activation (Phase 5). The only prerequisite is a place to wire the results into the broker — which the current tool infrastructure already supports.

Recommended sequencing:
1. Implement `Adze.Index` as a standalone project with unit tests
2. Add `search_project_files` as tool #11 in the catalog
3. Wire into the broker's tool selection logic
4. Add benchmark/eval cases for project search queries

### Build Constraints

- `Adze.Index` must build with the existing MSBuild/Visual Studio toolchain
- No `dotnet` SDK dependency (consistent with project convention)
- NuGet package (OpenMcdf) restored via `tools/nuget.exe`
- Unit tests added to `tests/Adze.Tests` — OLE reading can be tested against small sample `.SLDPRT` files committed to a test fixtures directory

### Boundary Rules

- Closed-file indexing stays **outside** the live COM execution loop (per `BUILD_SPEC.md`)
- The index is a read-only cache of file metadata, not a writable store
- Index data supplements but does not replace live `SessionContext` from open documents
- The broker must clearly distinguish "from index (may be stale)" vs "from live session (current)" in its answers

### What Not to Do

- Do not attempt to parse SOLIDWORKS proprietary binary streams
- Do not require SOLIDWORKS to be running for indexing
- Do not write to SOLIDWORKS files during indexing
- Do not index files on network shares without explicit user opt-in (performance and permission concerns)
- Do not cache resolved property values from OLE reading (they are not resolved — only Document Manager provides resolution)
- Do not block the SOLIDWORKS UI thread during indexing operations

---

## 9. Open Questions for Discovery

| Question | When to Resolve | Notes |
|---|---|---|
| Should the index persist across SOLIDWORKS sessions? | Before v1 implementation | Likely yes — stored under `%LOCALAPPDATA%\Adze\index\` |
| Should thumbnail extraction be part of v1 or deferred? | Before v1 implementation | Thumbnails add visual value but increase index size and implementation scope |
| What is the maximum practical folder size for v1? | During v1 testing | Target: 500 files. Test with 1000+. Set a configurable cap. |
| Should the user configure project folders, or auto-detect from open assembly references? | Before v1 implementation | Start with explicit configuration; auto-detection is a v2 enhancement |
| How should stale index entries be presented to the user? | Before v1 implementation | Likely: show last-indexed timestamp, warn if file is newer than index |
| Is the Document Manager license key obtainable for this project? | Before v1.5 | Requires SOLIDWORKS subscription and API support request |
| Should v1 support SOLIDWORKS 3DEXPERIENCE file formats (3D XML)? | Before v1 implementation | Likely no — the 3DEXPERIENCE platform uses different storage; defer to later |

---

## 10. Summary

| Approach | Data Richness | Complexity | Dependencies | License Cost |
|---|---|---|---|---|
| OLE Structured Storage | Low-Medium (custom props, summary info, thumbnail) | Low | OpenMcdf or Win32 interop | None |
| Document Manager API | Medium (adds configs, resolved props, references) | Medium | `swdocumentmgr.dll`, license key | Free with SW subscription |
| Full COM (batch open) | High (everything) | High | Running SOLIDWORKS instance | SOLIDWORKS license |

**Recommendation:** Start with v1 (OLE-only). It delivers immediate value for property search with zero external dependencies and zero licensing friction. Treat Document Manager as a v1.5 enhancement for users who can obtain the key. Never batch-open files through full COM for indexing — it is too slow, too fragile, and conflicts with the live session.

The v1 approach is sufficient for the most common retrieval queries ("find parts by material", "find parts by author", "which files were modified recently") and establishes the index infrastructure that later phases build on.
