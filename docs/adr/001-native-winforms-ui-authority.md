# ADR-001: Native WinForms as v1.1+ UI Rendering Authority

**Status:** Accepted
**Date:** 2026-04-28 (Session 4)
**Authors:** Kaden VanHoecke (VH Tech LLC) with Claude Code agents
**Brain.db cross-references:** Decision #21 (this ADR's primary anchor — partial supersession of Decision #12), Decision #22 (release-path PAUSE — companion to #21)
**Plan cross-reference:** [`plans/ui-rebuild-v1.1.md`](ui-rebuild-v1.1.md)
**Supersedes:** Partial supersession of Decision #12 ("Expand UI beyond Task Pane over v0.1.2 through v0.x"). The multi-surface roadmap from #12 is preserved (sidebar / Manager / toolbar all still in scope). What is replaced is the **rendering authority** for those surfaces: WebBrowser/IE11 + JS for the sidebar is rejected; native WinForms primitives become the authority going forward. The Manager has always been WinForms; the toolbar has always been native ribbon — those surfaces were never affected by the WebBrowser stack.

---

## Context

Adze v0.1.0–v1.0 used `System.Windows.Forms.WebBrowser` (IE11 trident engine) inside a SOLIDWORKS Task Pane to render the entire chat experience. The single `TaskPaneControl.cs` file grew to 1912 lines, packed 13 distinct surfaces (chat, write cards, recipes, write history, tools log, status, telemetry, plan, quick actions, clarification panel, probe banner, health banner, budget bar) into one HTML page, and re-rendered the entire DocumentText on every state change. Three concrete problems emerged:

1. **Full-page re-render on every state change** (line 657: `_contentBrowser.DocumentText = html`) — fired 8+ times per session, blowing away scroll position, DOM, and JS state. This is the root cause of "every prompt scrolls back to the top".
2. **Clarification panel is WinForms layered over WebBrowser** (line 248) — a WinForms `Panel` floats above the WebBrowser, with no HTML representation. Incoherent affordance, hard to maintain.
3. **"Tabs" at the bottom were actually accordions** (line 1556 `toggleSection`) — code labelled them as tabs; users experienced them as collapsible sections. Conceptual mislabel propagated bad UX.

Beyond those code-level problems, the strategic perception gap was the larger driver: Adze is positioned as a **native CAD AI assistant for serious SOLIDWORKS engineers**. The IE11 sidebar feels (founder's words, Session 4) like *"ChatGPT shoved into a cramped IE11 panel"* — a tech demo wrapped in someone else's app, not a serious CAD tool. That perception gap blocks partner reviewers, beta testers, and serious adoption.

The Adze.Manager was already WinForms. The toolbar (SW Ribbon + ContextMenu) has always been native via the SOLIDWORKS COM API. The sidebar was the only surface trapped in the WebBrowser stack.

---

## Decision Drivers

- **Perception parity with the host.** SOLIDWORKS itself is a native MFC/WPF/WinForms application. A native sidebar visually matches the host; an IE11 sidebar does not.
- **Iteration speed.** Round-tripping every UI change through a SOLIDWORKS install/launch cycle (~5 min round trip) is unworkable. We need a hot-iteration dev shell that does not require SW. WinForms primitives + a standalone harness are mechanically easier to iterate than HTML strings inside a WebBrowser host.
- **State preservation across re-renders.** Streaming text, scroll position, and incremental updates all require DOM-preserving control updates. WinForms gives us that natively (`AppendText`, `Invalidate`); the WebBrowser path requires reconstructing JS bridges for every value type and discipline we currently lack.
- **Code intelligence (GitNexus + graphify).** C# code is first-class indexed; HTML strings embedded in C# files are not. Native primitives mean refactoring tools can see the full call graph.
- **No 120 MB WebView2 dependency.** Migrating to WebView2 was the obvious "modern HTML" alternative; rejected because it adds ~120 MB to the install zip, requires Edge runtime distribution governance, and offers marginal benefit when we are willing to drop HTML entirely.
- **Founder iteration velocity.** Founder explicitly asked (Session 4): *"If you think C# over WebView2, let's do it; another benefit to C# is that... it should be faster AND GitNexus will allow us to thrive (really good for code indexing?)."* Decision aligns with founder preference.

---

## Considered Options

### Option A — Stay on WebBrowser/IE11 (status quo)

Keep `TaskPaneControl.cs` and patch the worst issues incrementally (DOM-preserving updates, externalised JS, cleaner state model).

- **Pros:** No new project, no migration risk, HTML/CSS gives rich layout cheaply.
- **Cons:** IE11 trident is a deprecated engine (not even Edge-equivalent). Modern HTML/CSS features unreliable. JS bridge fragile. The 1912-line file is already unmaintainable; patching makes it longer.
- **Verdict:** Rejected. Patches do not address the strategic perception gap, and the tech debt grows under us.

### Option B — Migrate to WebView2

Replace WebBrowser with WebView2 (Chromium) so we can keep the HTML/CSS surface area but on a modern engine.

- **Pros:** Modern HTML/CSS/JS. Familiar web idioms. Reuses existing markup investment.
- **Cons:** ~120 MB Edge runtime dependency in install zip. Runtime distribution governance (when does WebView2 update? what version is shipped? what if user disables Edge?). Still requires re-architecting state management (full re-render is still wrong on Chromium too). Does not solve the perception gap if the chrome still looks "embedded web".
- **Verdict:** Rejected. The runtime cost is non-trivial for a small free CAD add-in and the benefit is marginal versus a clean native rewrite.

### Option C — Native WinForms (chosen)

Build a new `Adze.UI` project (Class Library, .NET 4.8) hosting `NativeTaskPaneControl` and supporting controls. SOLIDWORKS loads it via a thin COM-registered shim (`NativeTaskPaneControlShim`) that mounts the inner control. Old `TaskPaneControl` remains as fallback behind a feature gate.

- **Pros:** Zero runtime dependency added (WinForms ships in .NET Framework 4.8). Native primitives are state-friendly by construction. Code intelligence indexes everything. Standalone `Adze.UiHarness` lets us iterate without SW. Visual parity with the SOLIDWORKS host. Founder velocity-positive.
- **Cons:** More C# code than the equivalent HTML. No CSS — manual paint for rounded corners, gradients, shadows. Dark mode requires duplicating colour tokens (no media query equivalent). Markdown rendering needs a custom translator (`MarkdownToRichText`) instead of cheap HTML rendering. Layout is more verbose than flexbox. WinForms is a 2003 framework — primitives like `RichTextBox` have known quirks (no native streaming smoothness, image alignment is finicky). Custom controls require owner-drawing for any non-stock affordance.
- **Verdict:** Accepted. The cons are real and acknowledged; they are paid in writing more code, not in runtime cost or strategic risk. Founder accepts the trade.

### Option D — Hybrid: WinForms shell, WebView2 inside for chat

A WinForms outer shell (tabs, banners, write cards) with a WebView2 control specifically for the streaming chat region.

- **Pros:** Native chrome, modern web for the rich-text-heavy surface.
- **Cons:** Worst of both — still 120 MB runtime, still HTML state issues for the most-rendered surface, plus the boundary between the two stacks introduces new failure modes.
- **Verdict:** Rejected. The perceived best of both is the actual worst of both.

---

## Decision Outcome

**Native WinForms (Option C)** is the rendering authority for the v1.1+ sidebar. Specifically:

- New project `src/Adze.UI/` hosts the runtime UI library — Class Library, .NET 4.8, AssemblyVersion 1.1.0.0, no SOLIDWORKS interop dependencies.
- `NativeTaskPaneControl` (in `Adze.UI/V2/`) is the new primary sidebar control. Supporting controls: `ChatMessageView`, `WriteCardView`, `QuickActionsBar`. Markdown rendered via `Adze.UI/Rendering/MarkdownToRichText.cs`. Theme tokens in `UiPalette.cs` (indigo accent `#4F46E5`).
- `ITaskPaneHost` interface (in `Adze.Contracts.Abstractions`) is the contract between the UI control and any host (production `HostStateAdapter`, test `StubHostState`, future MCP/sidecar host). UI mounts via constructor injection — no static singletons.
- `HostStateAdapter` (in `Adze.Host.Infrastructure`) is the production instance bridge from the static `HostState` to the new control.
- `NativeTaskPaneControlShim` (in `Adze.Host.UI`) is the COM-registered shim — stable `ProgID Adze.Host.NativeTaskPaneControl` and `Guid {C8B41F45-D2A6-4B5E-9F7C-3E0A1D8B2F61}`. SOLIDWORKS instantiates the shim via mscoree.dll; on `OnHandleCreated`, the shim mounts the inner native control filling its client area.
- `src/Adze.UiHarness/` is the out-of-SOLIDWORKS dev shell. Mounts the same `NativeTaskPaneControl` against `StubHostState` for hot iteration without launching SW.
- `Adze.Manager` is refactored from a single form into a 4-tab `TabControl` (Logs / Settings / Agent Profile / Status) backed by `LogsTab.cs`, `SettingsTab.cs`, `AgentProfileTab.cs`, `StatusTab.cs`. The 5 sidebar accordion sections that don't belong in a daily-driver chat panel (recipes, write history, tools log, status, telemetry) move to the Manager Logs/Status tabs.
- `SOLIDWORKS_AI_NATIVE_SIDEBAR` feature gate (in `FeatureGateRegistry`) controls cutover. **Default OFF.** Legacy `TaskPaneControl.cs` (1912 lines) remains the default sidebar until the founder verifies the new native sidebar works live in SOLIDWORKS. Deletion of the legacy control deferred until founder verification.
- The toolbar (SW Ribbon + ContextMenu) is unaffected — it has always been native via the SOLIDWORKS COM API. Phase 4 toolbar enrichment work (ribbon icons, FlyoutGroups, adaptive context menu) proceeds independently.

This decision **partially supersedes Decision #12**. The multi-surface roadmap from #12 (sidebar / Manager / toolbar all in scope) stands. The rendering authority for the sidebar changes from "WebBrowser + Markdown→HTML" to "WinForms primitives + Markdown→RichText".

This decision **is paired with Decision #22**, which paused the v1.0.0 release path and the SOLIDWORKS partner application to make this UI rebuild the primary track.

---

## Consequences

### Positive

- **Visual parity with the host.** The sidebar will look like a SOLIDWORKS task pane, not a web page jammed into one.
- **State preservation by construction.** Streaming text appends instead of full re-renders; scroll position survives every state change. Eliminates the "scrolls to top on every prompt" bug.
- **Hot iteration via `Adze.UiHarness`.** UI work no longer requires a 5-min SW round-trip. Founder gets instant visual feedback.
- **Code intelligence.** Every label, panel, and event handler is indexed by GitNexus and graphify. HTML strings inside C# were not.
- **Strict separation enforced by project boundary.** `Adze.UI` has zero SOLIDWORKS dependencies — it builds and tests without SW. The COM boundary is one shim file (`NativeTaskPaneControlShim`).
- **Test surface expanded.** New unit tests under `tests/Adze.Tests/UI/` cover `MarkdownToRichText`, `TaskPaneHostContract`, and `QuickActionsBar`. `HostStateAdapterMappingTests` covers the production-side bridge. Test count rose from 702 → 751 across Session 4.
- **Decision unblocks the partner application track.** The "Adze looks like a tech demo" perception was a real partner-application risk. Native WinForms eliminates it.

### Negative

- **More code than HTML.** Equivalent layout in HTML is ~3× shorter. We pay this in raw lines of C#.
- **No CSS.** Rounded corners, gradients, shadows all require manual `OnPaint` overrides. Anti-aliasing has to be configured per-control. Existing native paint code in `NativeTaskPaneControl.cs` is non-trivial.
- **Dark mode requires duplicating tokens.** Every colour in `UiPalette.cs` needs a light + dark variant. No CSS media query equivalent. Round 2 work (in flight at the time of this ADR) is rebuilding `UiPalette` to support both modes.
- **Markdown is not free.** `MarkdownToRichText` is a custom translator. Code blocks, tables, images, and complex inline markup are limited. The current implementation handles the chat-message subset and explicitly does not target full CommonMark / GitHub Flavoured Markdown.
- **WinForms quirks.** `RichTextBox` has known rough edges (image alignment, RTF parser variance, no native fade). Streaming chunks need careful `AppendText` discipline to avoid visual stutter.
- **Owner-drawn tabs and chips.** Stock WinForms `TabControl` and `Button` look like Windows 2003. Every modern affordance (rounded chip, owner-drawn tab strip) is custom paint code.
- **Two sidebar implementations live in the codebase during cutover.** `TaskPaneControl.cs` (legacy, 1912 lines) and `Adze.UI/V2/NativeTaskPaneControl.cs` coexist behind a feature gate. Until the founder verifies the new sidebar in SW, the legacy file is on the maintenance burden.
- **COM shim adds a small registration surface.** `NativeTaskPaneControlShim` requires a stable GUID + ProgID and adds one regfile entry to install / uninstall. Documented in `install/install-adze.ps1` and `install/uninstall-adze.ps1`.
- **No CSS-equivalent design system.** Visual consistency depends on every control honouring `UiPalette` tokens. There is no compiler-enforced linter for this; review discipline carries the load.
- **Round 2 dark-mode work and clarification-panel rebuild are in flight in a parallel agent.** State at the time of this ADR is fluid; some claims here may have moved by the time you read this. Verify against current code.

### Neutral

- The legacy `TaskPaneControl` becomes a deletion candidate once founder verification is complete. No rush to delete; reversibility is a feature.
- The Manager's existing single-form code is replaced by four tab files, but the entry point (`Program.cs`) and overall app shape are unchanged.
- `ChatEntry` and `PendingWriteAction` types moved from `Adze.Host` internal namespaces into `Adze.Contracts.Models` as public types so both implementations of `ITaskPaneHost` can produce them.

---

## Compliance

- **Always rule (CLAUDE.md):** "Always read the nearest directory `README.md` before editing a boundary directory." Compliance: `src/README.md` and any future `src/Adze.UI/README.md` should reflect this ADR before structural edits.
- **Always rule (CLAUDE.md):** "Always update C# contracts and JSON schemas together." Compliance: `ITaskPaneHost`, `ChatEntry`, `PendingWriteAction` are C# contracts only — no JSON schema changes required for these (they are runtime-only types, not persisted contracts). `ModelJsonMapper` round-trip work in Chunk 1 did update `SessionContext` (de)serialisation but stayed within existing schema.
- **Do-not rule (CLAUDE.md):** "Do not let the broker, scripts, Python, Node, or any external process call SOLIDWORKS COM directly." Compliance: `Adze.UI` has no SOLIDWORKS interop reference. All COM execution stays in `Adze.Host` via `HostStateAdapter` → `HostState`.
- **Do-not rule (CLAUDE.md):** "Do not use UI automation, desktop clicking, or computer-use control as the core product path." Compliance: this decision moves us *further* from any UI-automation surface — native WinForms primitives are first-class controls, not screen-scraping targets.
- **Do-not rule (CLAUDE.md):** "Do not duplicate core documentation scopes. Architecture rationale belongs in brain.db decisions, tactical next steps belong in brain.db notes and `plans/`." Compliance: this ADR captures the architectural rationale (one place); brain.db Decision #21 is the canonical pointer with cross-reference to this file. Tactical next steps live in `plans/ui-rebuild-v1.1.md` and brain.db notes.

---

## Reversibility

This decision is reversible at the cost of one feature-flag flip and a new sidebar implementation. Specifically:

- Legacy `TaskPaneControl.cs` is preserved (1912 lines, untouched by Chunk 3).
- `SOLIDWORKS_AI_NATIVE_SIDEBAR=false` (the default) keeps SOLIDWORKS loading the legacy WebBrowser sidebar.
- Reversing the decision means: stop developing `Adze.UI`, leave the gate default-OFF permanently, and resume incremental fixes to the legacy file. No data is lost.

If the founder verifies the new sidebar live in SW and chooses to make `Adze.UI` the default, the gate flips ON, and the legacy file becomes a deletion candidate at a future cleanup session. Until then, both paths coexist.

---

## Verification artifacts (Session 4 closeout)

- `Adze.sln` contains 10 projects (verified by `git ls-files Adze.sln` + manual read).
- `src/Adze.UI/Adze.UI.csproj` exists with `<AssemblyName>Adze.UI</AssemblyName>` and 7 `<Compile Include>` items (verified by direct read 2026-04-29).
- `SOLIDWORKS_AI_NATIVE_SIDEBAR` constant present in `src/Adze.Broker/Configuration/FeatureGateRegistry.cs` line 41 with `IsDefaultEnabled` returning `false` for it (verified by grep).
- COM shim GUID and ProgID match the values in this ADR (verified by direct read of `src/Adze.Host/UI/NativeTaskPaneControlShim.cs`).
- `pwsh -NoProfile -File scripts/setup/run-tests.ps1` reports 751 tests, 745 passed, 6 inconclusive, 0 failed (verified Session 4 close).

---

## See Also

- **Brain.db Decision #21** — canonical decision record (this ADR's anchor)
- **Brain.db Decision #22** — companion decision (release pause to prioritise this rebuild)
- **Brain.db Decision #12** — original multi-surface roadmap (partially superseded here)
- **`plans/ui-rebuild-v1.1.md`** — execution plan with phase breakdown
- **`CLAUDE.md`** — Current Working Baseline section captures the v1.1 progression in living-doc form
- **`plans/discovery-clarification-ui.md`** — older clarification-UI design; the Round 2 rebuild lands native equivalents
