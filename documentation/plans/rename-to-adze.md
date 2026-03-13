# Rename Plan: SolidWorksAi → Adze

**Status:** Completed on 2026-03-13
**Priority:** P1
**Created:** 2026-03-13

---

## Context

The product name is **Adze**. The codebase currently uses `SolidWorksAi` as the namespace, assembly name, and solution name. This must be renamed to `Adze` before public release. "SolidWorks" is a Dassault Systèmes trademark and cannot appear in a third-party product name.

Repo: `https://github.com/Kadenvh/adze-cad.git`
Tagline: *"Adze — Native AI Assistant for SOLIDWORKS"*

Execution result:
- Solution, project folders, project files, namespaces, scripts, runtime storage, and schema IDs were renamed to `Adze`.
- COM ProgIds now use `Adze.Host.*`.
- Runtime storage now uses `%LOCALAPPDATA%\Adze`.
- Canonical docs and READMEs were updated to the `Adze` name.
- Post-rename validation passed: build, schema validation, broker evals, host validation, and grounding benchmarks.

---

## Scope

### Solution file
- `SolidWorksAi.sln` → `Adze.sln`

### Project folders
- `src/SolidWorksAi.Host/` → `src/Adze.Host/`
- `src/SolidWorksAi.Contracts/` → `src/Adze.Contracts/`
- `src/SolidWorksAi.Tools/` → `src/Adze.Tools/`
- `src/SolidWorksAi.Trace/` → `src/Adze.Trace/`
- `src/SolidWorksAi.Broker/` → `src/Adze.Broker/`

### Project files (.csproj)
In each `.csproj`:
- `<RootNamespace>SolidWorksAi.*</RootNamespace>` → `<RootNamespace>Adze.*</RootNamespace>`
- `<AssemblyName>SolidWorksAi.*</AssemblyName>` → `<AssemblyName>Adze.*</AssemblyName>`
- Project reference paths updated to match new folder names
- `<Product>` and `<Description>` fields updated

### Namespace declarations
All `.cs` files: `namespace SolidWorksAi.*` → `namespace Adze.*`
All `.cs` files: `using SolidWorksAi.*` → `using Adze.*`

### COM registration
In `src/Adze.Host/AddIn/SolidWorksAiAddIn.cs`:
- `[ProgId("SolidWorksAi.Host.AddIn")]` → `[ProgId("Adze.Host.AddIn")]`
- Class name: `SolidWorksAiAddIn` → `AdzeAddIn`
- Registry title: Update from "SolidWorks AI Assistant" to "Adze for SOLIDWORKS"

In `src/Adze.Host/UI/TaskPaneControl.cs`:
- `[ProgId("SolidWorksAi.Host.TaskPaneControl")]` → `[ProgId("Adze.Host.TaskPaneControl")]`

**Note on GUIDs:** The COM class GUIDs (`A2E09EE4-BB43-4A0C-945F-14711F792EFA` and `F4068202-600A-4D6F-973B-DA2048A949CF`) can stay the same OR be regenerated. If regenerated, update all registry scripts to match.

### Registration scripts
In `scripts/setup/register-host-addin.ps1`:
- Update ProgId strings from `SolidWorksAi.Host.*` to `Adze.Host.*`
- Update `HKCU\Software\Classes\SolidWorksAi.*` paths to `Adze.*`
- Update Title/Description registry values

In `scripts/setup/unregister-host-addin.ps1`:
- Update all registry key paths from `SolidWorksAi.*` to `Adze.*`

### Broker eval scripts
In `scripts/setup/run-broker-evals.ps1`:
- Assembly load paths: `SolidWorksAi.Contracts.dll` → `Adze.Contracts.dll`
- Assembly load paths: `SolidWorksAi.Broker.dll` → `Adze.Broker.dll`
- Type references: `[SolidWorksAi.Contracts.*]` → `[Adze.Contracts.*]`
- Type references: `[SolidWorksAi.Broker.*]` → `[Adze.Broker.*]`

### Build scripts
In `scripts/setup/build-all.ps1`:
- Solution path: `SolidWorksAi.sln` → `Adze.sln`

In `scripts/setup/build-host.ps1`:
- Project path: update to `src/Adze.Host/Adze.Host.csproj`

### Other scripts referencing assembly names
Check all `.ps1` files in `scripts/setup/` for references to `SolidWorksAi` and update.

### Runtime storage paths
In `src/Adze.Trace/Storage/StatePaths.cs`:
- The base path uses `SolidWorksAi` folder under `%LOCALAPPDATA%`
- Decision: Keep as `%LOCALAPPDATA%\SolidWorksAi` for backward compat OR rename to `%LOCALAPPDATA%\Adze`
- **Recommended:** Rename to `%LOCALAPPDATA%\Adze` since there are no production users yet

### JSON schema IDs
In all files under `schemas/`:
- `https://sw-plugin.local/schemas/` → `https://adze.dev/schemas/` (or chosen domain)

### Documentation
- `CLAUDE.md` — Update title, all references to "SolidWorksAi" and "SolidWorks AI Assistant"
- `documentation/PROJECT_ROADMAP.md` — Update product name references
- `documentation/IMPLEMENTATION_PLAN.md` — Update product name references
- `documentation/BUILD_SPEC.md` — Update product name references
- `documentation/FIRST_USABLE_BUILD.md` — Update product name references
- All `README.md` files — Update references
- Keep "SOLIDWORKS" (the host application name) unchanged — that's the platform, not our product

### UI text
In `src/Adze.Host/UI/TaskPaneControl.cs`:
- Title label: "SOLIDWORKS AI ASSISTANT" → "ADZE" or "ADZE FOR SOLIDWORKS"

### AssemblyInfo.cs files
In all `Properties/AssemblyInfo.cs`:
- Update `AssemblyTitle`, `AssemblyProduct`, `AssemblyDescription`

---

## Execution Order

1. Rename folder names under `src/`
2. Rename `.csproj` files to match new folder names
3. Update `.sln` file (project paths and names)
4. Update all `.csproj` contents (namespace, assembly name, references)
5. Find-and-replace `SolidWorksAi` → `Adze` across all `.cs` files (namespaces + usings)
6. Update COM ProgIds and class names
7. Update all PowerShell scripts
8. Update schema IDs
9. Update `StatePaths.cs` base directory
10. Update all documentation and README files
11. Update UI text
12. Build: `pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks`
13. Run broker evals: `powershell.exe -NoProfile -File scripts\setup\run-broker-evals.ps1`
14. Run schema validation: `pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1`

---

## Exit Criteria

- [x] Solution builds with zero errors
- [x] No remaining references to `SolidWorksAi` in any source file except historical rename-plan notes
- [x] Broker evals pass 12/12
- [x] Schema validation passes
- [x] Registration scripts use new ProgIds
- [x] `%LOCALAPPDATA%\Adze` used for runtime storage
- [x] All docs updated to reference "Adze"
