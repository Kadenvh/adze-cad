# Discovery: Pre-Prompt Clarification UI

**Status:** Design proposal | **Date:** 2026-03-15 | **Version:** 0.1.0

---

## Problem

The current Task Pane flow is: user types free text, clicks "Run assistant", broker infers intent from keywords, tools execute, answer is synthesized. The broker's keyword-based intent detection (`KeywordBrokerOrchestrator.InferIntent`) often cannot distinguish between overlapping intents, and the user has no way to narrow scope before tokens are spent. A vague request like "tell me about this part" triggers a broad 4-tool sweep when the user may only care about one dimension on one feature.

Pre-prompt clarification lets the user steer the run with structured choices populated from the live session, reducing wasted tool calls and improving answer relevance without requiring the user to know tool names or write precise prompts.

---

## 1. What Questions to Ask

Each clarification question maps to data already available in `SessionContext` at the time the user is composing a request. The questions are grouped into four independent axes.

### 1.1 Intent Axis

**Question:** "What do you want to do?"

| Choice | Maps to broker intent | When to show |
|--------|----------------------|--------------|
| Inspect | `general_grounding` | Always (default) |
| Diagnose | `diagnostics_review` | Always |
| Explain | `explanation_review` | Always |
| Compare | `configuration_review` | When `Configurations.Count > 1` |

These are mutually exclusive. A single `ComboBox` is the right control.

**Data source:** Static labels. The "Compare" option appears only when `SessionContext.Configurations.Count > 1`.

### 1.2 Scope Axis

**Question:** "Focus on..." (optional, multi-select)

The scope choices are document-type-sensitive and populated from live data.

#### Part document (`Document.Type == "part"`)

| Choice format | Data source | Example |
|---------------|-------------|---------|
| Feature: `{Name}` (`{Kind}`) | `FeatureTree.Features` filtered to non-origin, non-plane | `Feature: Boss-Extrude1 (Extrusion)` |
| Dimension: `{Name}` = `{Value}` | `Dimensions.Items` | `Dimension: D1@Sketch1 = 50.0` |
| Configuration: `{Name}` | `Configurations.Items` | `Configuration: Default` |
| Custom property: `{Key}` | `Properties` keys with `document_custom.` or `configuration_custom.` prefix | `Custom property: Material` |

#### Assembly document (`Document.Type == "assembly"`)

All part choices plus:

| Choice format | Data source | Example |
|---------------|-------------|---------|
| Component: `{Name}` | `ReferenceGraph.DirectItems` | `Component: Part1.SLDPRT` |
| Mate: `{Name}` (`{Kind}`) | `Mates.Items` | `Mate: Coincident1 (Coincident)` |

#### Drawing document (`Document.Type == "drawing"`)

Scope choices are limited to the document-level items (configurations, custom properties) since the current `SessionContext` does not expose sheet or view entities.

#### When selection exists (`Selection.Count > 0`)

A special "Current selection" choice appears at the top of the scope list, pre-checked. Its label includes the selection preview: `Current selection: Face1 on Boss-Extrude1`.

**Control:** `CheckedListBox` with a maximum visible height of 6 items (scrollable). Items are populated dynamically when the document changes or when the user focuses the request box.

**Item limits:** At most 20 scope items are shown. When the source list is longer (common for dimensions), the list is truncated and a footer label reads: `Showing 20 of {N}. Type a feature or dimension name in the request to narrow further.`

### 1.3 Output Mode Axis

**Question:** "How should I answer?"

| Choice | Meaning | When to show |
|--------|---------|--------------|
| Brief | 2-3 sentence answer | Always (default) |
| Detailed | Full grounded walkthrough | Always |
| Tabular | Structured table format | When scope has >2 items selected |

A single `ComboBox` is the right control.

### 1.4 Diagnostics Flag

**Question:** "Include rebuild diagnostics?"

A single `CheckBox`. Auto-checked when `Diagnostics.RebuildState != "current"` or `Diagnostics.Warnings.Count > 0` or `Diagnostics.MissingReferences.Count > 0`. Unchecked by default otherwise.

---

## 2. UI Layout

### 2.1 Current Task Pane Structure

```
+-------------------------------------------+
| ADZE                              (header) |  row 0: 36px fixed
+-------------------------------------------+
| Request                                    |
| [multiline text box, 78px]                 |  row 1: 156px fixed
| [Run assistant] Ready.                     |  (composerPanel)
+-------------------------------------------+
| Assistant Answer                           |
| [answer text box, fill]                    |  row 2: fill
| [answer footer]                            |  (answerPanel in
|-------------------------------------------|   SplitContainer.Panel1)
| [Plan] [Status] [Tools]                   |
| [tab content, fill]                        |  (detailsTabs in
+-------------------------------------------+   SplitContainer.Panel2)
```

The `composerPanel` is the fixed-height area (row 1, 156px) containing the request label, the request text box, and the run button row.

### 2.2 Proposed Clarification Panel Insertion

The clarification panel inserts between the request text box and the run button row inside `composerPanel`. The composerPanel's row height grows to accommodate it.

```
+-------------------------------------------+
| ADZE                              (header) |  36px fixed
+-------------------------------------------+
| Request                                    |
| [multiline text box, 78px]                 |
|                                            |
| v Refine (click to expand/collapse)        |  <-- NEW: collapsible
|   Intent: [Inspect v]                      |  <-- NEW: ComboBox
|   Focus:  [ ] Current selection: Face1     |  <-- NEW: CheckedListBox
|           [ ] Feature: Boss-Extrude1       |       (6 visible rows max)
|           [ ] Dimension: D1@Sketch1 = 50   |
|           [x] (auto-checked items)         |
|   Output: [Brief v]                        |  <-- NEW: ComboBox
|   [x] Include diagnostics                  |  <-- NEW: CheckBox
|                                            |
| [Run assistant] Ready.                     |
+-------------------------------------------+
| (answer and tabs below, unchanged)         |
+-------------------------------------------+
```

### 2.3 Collapsible Behavior

The clarification panel is wrapped in a collapsible container. A `LinkLabel` reading "Refine" with a `v` / `>` indicator toggles its visibility. When collapsed, the composer panel uses its original 156px height. When expanded, the composer panel grows by the clarification panel's measured height (estimated 130-180px depending on scope list length).

**Default state:**
- Collapsed when no document is open (nothing to populate).
- Collapsed on first launch to avoid overwhelming new users.
- Expanded automatically when the request text box receives focus AND a document is open AND this is not the user's first session (tracked via a simple `bool` in local state).
- The user's last expand/collapse preference is remembered for the session.

### 2.4 Narrow-Width Adaptation (300-400px sidebar)

At 300px width, horizontal space is tight. The layout rules:

- **Intent and Output ComboBoxes:** Full width of the clarification panel minus padding (approximately 260px usable). Label above, not beside, the dropdown to avoid truncation.
- **Focus CheckedListBox:** Full width. Item text is truncated with ellipsis if it exceeds the available width. The full text appears in a `ToolTip`.
- **Diagnostics CheckBox:** Full width, single line.
- **All labels:** Use `Segoe UI` 8.5pt to match existing UI density.
- **Vertical stacking:** All controls stack vertically. No two controls share a horizontal row except the expand/collapse toggle.

### 2.5 Specific Control Recommendations

| Control | WinForms Type | Key Properties |
|---------|---------------|----------------|
| Expand/collapse toggle | `LinkLabel` | `Text = "v Refine"`, `LinkColor = Color.FromArgb(86, 96, 108)`, `Font = Segoe UI 8.5pt` |
| Clarification container | `Panel` | `Visible = false` initially, `Dock = DockStyle.Top`, `AutoSize = true`, `AutoSizeMode = GrowAndShrink` |
| Intent dropdown | `ComboBox` | `DropDownStyle = DropDownList`, `Dock = DockStyle.Top`, 3-4 items |
| Focus list | `CheckedListBox` | `Dock = DockStyle.Top`, `IntegralHeight = false`, `Height = 96` (6 rows at 16px), `CheckOnClick = true` |
| Output dropdown | `ComboBox` | `DropDownStyle = DropDownList`, `Dock = DockStyle.Top`, 2-3 items |
| Diagnostics checkbox | `CheckBox` | `Dock = DockStyle.Top` |
| Section labels | `Label` | `Dock = DockStyle.Top`, `Height = 18`, `Font = Segoe UI 8.5pt Bold` |
| Truncation footer | `Label` | `Dock = DockStyle.Top`, `Height = 16`, `Font = Segoe UI 8pt`, `ForeColor = Color.FromArgb(120, 128, 138)` |

### 2.6 Dynamic Refresh

The clarification panel contents must update when:

1. **The active document changes** (user switches tabs in SOLIDWORKS). This already triggers a status refresh via `HostState`. The clarification panel should re-populate on the same `_refreshTimer` tick that updates the Status tab.
2. **The selection changes.** The "Current selection" item should appear/disappear. This is detected on the same status refresh cycle.
3. **The user types in the request box.** NOT used for dynamic filtering in Phase 1. The scope list stays stable while the user types.

Re-population must not steal focus from the request text box or clear the user's text.

---

## 3. Data Flow

### 3.1 Structured Clarification Payload

The clarification choices are encoded into a `ClarificationPayload` object:

```csharp
public sealed class ClarificationPayload
{
    public string Intent { get; set; } = "inspect";       // inspect | diagnose | explain | compare
    public List<string> FocusItems { get; set; } = new();  // e.g., "Feature:Boss-Extrude1", "Dimension:D1@Sketch1"
    public string OutputMode { get; set; } = "brief";      // brief | detailed | tabular
    public bool IncludeDiagnostics { get; set; }
}
```

### 3.2 Where the Payload Enters the Pipeline

**Option A: Structured prefix to the user prompt (recommended for Phase 1)**

The clarification payload is serialized into a structured prefix that is prepended to the user's free-text request before it reaches `ContextPromptComposer.Compose`. This approach requires zero changes to the broker or model client interfaces.

The prefix format:

```
[clarification]
intent: diagnose
focus: Feature:Boss-Extrude1, Dimension:D1@Sketch1
output: detailed
include_diagnostics: true
[/clarification]

Why is Boss-Extrude1 showing a rebuild warning?
```

The `KeywordBrokerOrchestrator` already scans the full request text for keywords. The structured prefix naturally boosts the right tool candidates because it contains the relevant terms ("Feature", "Dimension", "diagnostics"). The model-backed broker path also benefits because `ContextPromptComposer.BuildUserPrompt` passes the full request string to the model.

**Option B: Direct broker behavior modification (Phase 2)**

A new overload of `CreateGroundingPlan` accepts a `ClarificationPayload` alongside `SessionContext` and `userRequest`. The orchestrator uses the payload to:

- Override `InferIntent` with the explicit intent choice.
- Pre-boost tool candidates for focused items (e.g., if the user selected "Dimension:D1@Sketch1", `get_dimensions` gets a +10 score bonus).
- Set the `GetFeatureTreeSliceParameters.AnchorName` to the selected feature.
- Pass the payload to the model prompt as a dedicated section.

This is cleaner but requires interface changes to `IBrokerOrchestrator`, `ContextPromptComposer`, and `GroundingExecutionService`.

**Option C: Mid-synthesis injection (Phase 3)**

The synthesis prompt (`ContextPromptComposer.ComposeSynthesisPrompt`) receives the clarification payload so the model knows the user wanted a "brief" vs "detailed" vs "tabular" answer. This shapes the final answer format without changing tool execution.

### 3.3 Interaction with Model Planning

The clarification payload does not replace the model's planning. It constrains it:

- **Intent** narrows the model's `turn_status` and `intent` fields. If the user chose "Diagnose", the model should not return `intent: "general_grounding"`.
- **Focus** reduces the model's tool recommendation space. If the user selected specific features and dimensions, the model should prioritize `get_feature_tree_slice` and `get_dimensions` over `get_reference_graph`.
- **Output mode** only affects synthesis, not planning.
- **Diagnostics flag** controls whether `get_rebuild_diagnostics` is included in the tool execution regardless of the model's recommendation.

The model remains free to add tools the user did not explicitly scope (e.g., `get_active_document` as a baseline check). The clarification payload is guidance, not a hard constraint.

---

## 4. Phased Approach

### Phase 1: Static Dropdowns from SessionContext (ship first)

**Scope:** Minimal UI addition to the existing Task Pane. No broker interface changes. No new contracts.

**Deliverables:**

1. **`ClarificationPanel`** -- a new `UserControl` (or inline `Panel` construction in `TaskPaneControl`) containing:
   - Expand/collapse `LinkLabel`
   - Intent `ComboBox` (4 static items)
   - Focus `CheckedListBox` (populated from `SessionContext`)
   - Output mode `ComboBox` (2-3 static items)
   - Diagnostics `CheckBox`

2. **`ClarificationPayload`** -- a simple POCO in `Adze.Contracts.Models` holding the user's choices.

3. **`ClarificationPrefixBuilder`** -- a static class in `Adze.Broker.Formatting` that serializes the payload into the `[clarification]...[/clarification]` prefix string.

4. **Integration in `TaskPaneControl.RunAssistant`** -- before calling `HostState.PrepareAssistantRun(request)`, the method reads the clarification panel state, builds the prefix, and prepends it to the request text.

5. **Population logic in `TaskPaneControl`** -- on status refresh, read `SessionContext` and repopulate the clarification panel controls if the document identity has changed.

**What Phase 1 does NOT include:**
- No changes to `IBrokerOrchestrator` or `KeywordBrokerOrchestrator`.
- No changes to `ContextPromptComposer` beyond receiving the prefixed request string.
- No model-generated clarifying questions.
- No mid-execution clarification.
- No persistence of clarification preferences between sessions.

**Estimated effort:** 2-3 working sessions. The UI construction follows the same pattern as the existing `TaskPaneControl` constructor. The prefix builder is a small static method. The population logic reuses `SessionContextBuilder.Build` data that `HostState` already calls.

**Test plan:**
- Unit tests in `Adze.Tests` for `ClarificationPrefixBuilder` (round-trip serialization, empty payload, full payload).
- Unit tests confirming that a prefixed request string still produces correct intent inference in `KeywordBrokerOrchestrator` (the prefix terms should boost, not confuse, the keyword scanner).
- Manual validation: expand the clarification panel with a part open, confirm features and dimensions appear, select items, run the assistant, confirm the prefix appears in the Plan tab output.

### Phase 2: Broker-Aware Clarification and Model-Generated Questions

**Scope:** The broker directly consumes the clarification payload. The model returns clarifying questions as pre-run suggestions.

**Deliverables:**

1. **`IBrokerOrchestrator.CreateGroundingPlan` overload** accepting `ClarificationPayload`.
2. **`ContextPromptComposer` changes** to render the payload as a dedicated prompt section rather than relying on the prefix hack.
3. **`BrokerResponse.NextQuestions` rendered as clickable buttons** in the Task Pane. Clicking a question populates it into the request box and optionally sets clarification state.
4. **Scope filtering in `GroundingExecutionService`** -- if the user selected specific features, pass `AnchorName` to `GetFeatureTreeSliceParameters`. If specific dimensions were selected, pass a name filter to `GetDimensionsParameters` (requires a new parameter field).
5. **Pre-run question pass** -- a lightweight broker call with the request text but no tool execution, returning only `NextQuestions` and a suggested intent. The Task Pane shows these as chip-style buttons before the user clicks "Run assistant".

**Estimated effort:** 3-4 working sessions. The broker changes are straightforward. The pre-run question pass adds a round-trip latency concern (mitigated by the deterministic fallback path, which is instant).

### Phase 3: Mid-Execution Clarification During Agent Loop

**Scope:** The broker can pause mid-execution to ask the user a clarifying question, then resume.

This phase requires:

1. **Async execution model** -- the current `ThreadPool.QueueUserWorkItem` pattern becomes an async state machine that can yield back to the UI thread.
2. **Mid-run UI state** -- the Task Pane shows "The assistant needs your input" with a question and response buttons, while keeping the partial results visible.
3. **Stateful broker turn** -- the broker emits a `turn_status: "needs_clarification"` with a question payload. The host pauses tool execution, shows the question, collects the answer, and resumes the broker with the answer injected.
4. **Timeout fallback** -- if the user does not respond within a configurable timeout, the broker proceeds with a default assumption and notes the assumption in the answer.

**Estimated effort:** 5-7 working sessions. This is the most complex phase and touches the execution model, UI state machine, and broker contract.

---

## 5. Implementation Notes

### 5.1 ComposerPanel Height Adjustment

The current `composerPanel` is in row 1 of the root `TableLayoutPanel` with a fixed 156px height. When the clarification panel expands, this row height must grow. Two approaches:

**Approach A (recommended):** Change row 1 from `SizeType.Absolute` to `SizeType.AutoSize` and set `composerPanel.AutoSize = true`, `AutoSizeMode = GrowAndShrink`. This lets the panel grow naturally when the clarification section becomes visible. The risk is that WinForms AutoSize can be unpredictable with nested controls; test thoroughly.

**Approach B:** Keep the row absolute and programmatically recalculate its height when the clarification panel toggles. Set `root.RowStyles[1].Height = 156 + clarificationPanel.Height` when expanded, `156` when collapsed. This is more predictable but requires manual measurement.

### 5.2 Population Performance

`SessionContextBuilder.Build` traverses COM objects (features, dimensions, mates) on every call. The clarification panel population should NOT trigger a new `Build` call. Instead, it should read from the most recent `SessionContext` that `HostState` already caches for the status refresh. If `HostState` does not currently cache the context (it rebuilds on every refresh), a simple `_lastContext` field in `HostState` would avoid redundant COM traversals.

### 5.3 Dock Order in ComposerPanel

WinForms `Dock = DockStyle.Top` stacks controls in reverse addition order. The current `composerPanel` adds controls in this order:

```csharp
composerPanel.Controls.Add(runRow);        // Top, but added first -> bottom
composerPanel.Controls.Add(requestBox);    // Top, added second -> middle
composerPanel.Controls.Add(requestLabel);  // Top, added third -> top
```

The clarification panel must be added between `runRow` and `requestBox`:

```csharp
composerPanel.Controls.Add(runRow);
composerPanel.Controls.Add(clarificationPanel);  // NEW
composerPanel.Controls.Add(requestBox);
composerPanel.Controls.Add(requestLabel);
```

This places the clarification panel visually between the request box (above) and the run button (below), which is the desired layout.

### 5.4 Focus Preservation

When the clarification panel re-populates (e.g., document changed), the code must:

1. Record whether the request text box currently has focus.
2. Record the checked items in the scope list by name (not index).
3. Clear and repopulate the controls.
4. Re-check any items whose names still exist in the new list.
5. Restore focus to the request text box if it was focused before.

### 5.5 Clarification Prefix Parsing (Future-Proofing)

Although Phase 1 treats the prefix as opaque text that the keyword scanner happens to handle, Phase 2 will need to parse it back out. The `[clarification]...[/clarification]` format is chosen because:

- It is unlikely to appear in natural user text.
- It is trivially parseable with `IndexOf` + `Substring`.
- The broker can strip it before passing the raw user text to the model, or include it as structured context.

### 5.6 Contract Location

`ClarificationPayload` belongs in `Adze.Contracts.Models` because it is a boundary type shared between `Adze.Host` (UI) and `Adze.Broker` (formatting/orchestration). `ClarificationPrefixBuilder` belongs in `Adze.Broker.Formatting` because it is a prompt-formatting concern.

---

## 6. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Clarification panel makes the composer area too tall on small monitors | Users cannot see the answer panel | Collapsible by default; auto-collapse when panel height exceeds 50% of Task Pane height |
| Scope list with 150 dimensions overwhelms the user | Decision paralysis, slow rendering | Cap at 20 items, show truncation footer, sort by relevance (selection-related first, then by feature tree order) |
| Structured prefix confuses the model-backed broker | Model treats `[clarification]` block as literal user text | Acceptable in Phase 1 (models handle structured prefixes well); Phase 2 moves to dedicated prompt section |
| COM traversal for population adds latency | UI feels sluggish when expanding the panel | Reuse cached `SessionContext` from last status refresh; never trigger a new COM traversal from the UI thread |
| Users ignore the clarification panel entirely | Feature goes unused | Track usage in trace events (was clarification expanded? how many items checked?). If adoption is low, consider Phase 2 model-generated questions as the primary entry point |

---

## 7. Open Questions

1. **Should the clarification panel auto-expand on document open?** Pro: discoverability. Con: noise for experienced users. Current recommendation: expand on first document open per session, then respect user preference.

2. **Should scope selections persist across runs?** Pro: iterative refinement. Con: stale selections after document changes. Current recommendation: clear selections when the document identity (path) changes, preserve them across runs on the same document.

3. **Should the "Compare" intent only appear with multiple configurations, or also when multiple documents are open?** Current recommendation: configurations only in Phase 1. Multi-document comparison is a Phase 3+ feature.

4. **Should the output mode affect tool execution or only synthesis?** Current recommendation: only synthesis. "Brief" vs "Detailed" changes how the answer is written, not which tools run. "Tabular" may require the synthesis prompt to request structured output, which is a Phase 2 concern.

5. **Should there be a keyboard shortcut to toggle the clarification panel?** Current recommendation: yes, `Ctrl+Shift+R` (Refine). Implementation is trivial via `KeyDown` handler on the `TaskPaneControl`.
