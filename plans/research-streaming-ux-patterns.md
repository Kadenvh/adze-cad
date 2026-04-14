# Research: Streaming UX Patterns for Agent Reasoning in a Task Pane Sidebar

**Date:** 2026-03-15
**Status:** Research findings
**Scope:** How to present an agent's intermediate reasoning, actions, and confirmations in Adze's 300-400px Task Pane without overwhelming users

---

## 1. Context

Adze is evolving from a single-turn assistant (user clicks "Run assistant", waits, sees answer) toward a multi-turn agentic loop where the model iteratively calls tools, observes results, and decides what to do next (see `discovery-agent-loop-architecture.md`). Future phases add write tools with preview/confirm gates (see `END-GOAL.md` Phase 3-4).

The current Task Pane (`src/Adze.Host/UI/TaskPaneControl.cs`) uses a Windows Forms `UserControl` inside a SOLIDWORKS sidebar. The layout is: request composer at top, answer panel in the middle, and Plan/Status/Tools tabs at the bottom in a `SplitContainer`. During a run, the UI shows "Running..." in a `Label` and blocks interaction until the full `AssistantRunSnapshot` arrives.

This research identifies concrete UI patterns for presenting intermediate agent state in that narrow sidebar, drawn from analysis of existing agentic UIs and adapted for the WinForms/Task Pane constraints.

---

## 2. Existing Agentic UI Patterns

### 2.1 Claude Code (Terminal)

Claude Code presents agent activity as a streaming vertical log in the terminal. Key patterns:

- **Tool calls are labeled blocks.** Each tool invocation appears as a distinct block with a header ("Read file.cs", "Search for X", "Edit file.cs") followed by a collapsed or expanded body showing the tool input/output.
- **Thinking is collapsed by default.** Extended reasoning appears as a separate collapsible section, not inline with tool output. Users can expand it if they want to see the chain of thought.
- **Streaming text for the final answer.** The assistant's text response streams token-by-token. Tool call blocks appear atomically (after the call completes), not streamed.
- **Permission gates are inline.** When Claude Code needs to run a command or edit a file, it shows the proposed action and waits for user approval with a simple yes/no prompt. The approval is a single line, not a modal dialog.
- **Progressive disclosure.** The log is append-only. Earlier steps scroll up naturally. The user sees the current activity at the bottom of the viewport.

**Applicable lesson:** The append-only vertical log maps well to a narrow sidebar. Tool activities are blocks, not inline text. Approval gates are minimal (one line, two buttons).

### 2.2 Cursor (IDE Agent Mode)

Cursor's agent mode operates in a side panel within VS Code. Key patterns:

- **Step pills.** Each agent step appears as a compact pill/chip showing the action type and target (e.g., "Reading file.cs", "Editing utils.ts"). Clicking a pill expands it to show details.
- **Diff view for edits.** When the agent proposes a code change, Cursor shows a standard unified diff view. The user sees red/green lines. Approval is Accept/Reject per file or per hunk.
- **Streaming answer alongside diffs.** The final answer text streams in a chat-like panel while diffs are shown in the editor. The two surfaces are separate.
- **Progress indicator.** A small animated spinner appears next to the current step pill while the API call or tool execution is in progress.
- **Inline errors.** If a tool fails, the error appears as a red-tinted step pill. The agent acknowledges the error in its next response and self-corrects.

**Applicable lesson:** Step pills are an excellent pattern for narrow sidebars. They are compact (one line per step), expandable for details, and visually scannable. The diff view pattern translates to Adze's write preview (before/after values rather than code lines).

### 2.3 GitHub Copilot Workspace (Plan View)

Copilot Workspace presents the full plan before execution. Key patterns:

- **Plan-first UI.** The agent generates a multi-step plan displayed as a checklist. Each step has a short description and a checkbox. The user reviews the plan before any execution begins.
- **Batch or individual approval.** The user can approve the entire plan ("Implement Plan") or toggle individual steps on/off before execution.
- **Live step status.** During execution, each step in the plan transitions through states: pending (gray), running (blue spinner), completed (green check), failed (red X).
- **Specification editing.** The user can edit the plan's natural-language specification before execution. The agent regenerates the plan based on the revised spec.
- **Parallel presentation of files.** Changes to different files are shown side-by-side in a multi-pane layout.

**Applicable lesson:** The plan-first pattern is directly relevant to Adze Phase 4 (multi-step write plans). The checklist-with-status-icons pattern works in a narrow sidebar -- it is just a vertical list of labeled rows with a status icon on each.

### 2.4 Devin (Timeline View)

Devin presents agent activity as a timeline. Key patterns:

- **Event-driven timeline.** Every action (file read, code change, terminal command, browser action) appears as a timestamped event in a vertical timeline. Events are grouped by logical phase.
- **Current activity spotlight.** The topmost or pinned section always shows "What Devin is doing right now" with a short sentence and a spinner.
- **Thought narration.** Devin periodically emits short narrative sentences explaining its reasoning ("I need to check the test file to understand the expected behavior"). These appear as lighter-styled events between tool events.
- **Long-running progress.** For multi-minute operations, Devin shows a progress message that updates in place ("Installing dependencies... 45s", then "Running tests... 12s").
- **Human-in-the-loop pause.** When Devin needs human input, the timeline shows a highlighted pause event with a text input and Send button.

**Applicable lesson:** The "current activity spotlight" is critical for Adze. Users should always be able to glance at the Task Pane and know what the agent is doing in one sentence. The thought narration pattern (short sentences, not full chain-of-thought) works in a sidebar without creating noise.

### 2.5 ChatGPT with Tools (Canvas / Tool Use)

ChatGPT's tool use in the standard chat interface. Key patterns:

- **Collapsed tool invocations.** When ChatGPT calls a tool (web search, code interpreter, DALL-E), a small card appears showing the tool name and a brief label. The card is collapsed by default. Clicking it shows the tool input and output.
- **Sequential reveal.** Tool cards appear in the order they execute. The final text answer streams below the last tool card.
- **No approval gates for read-only tools.** Read-only operations (search, code execution in sandbox) proceed without confirmation. Only actions with external side effects would need confirmation.
- **Thinking indicator.** A subtle animated indicator ("Searching...", "Analyzing...") appears while the tool is executing.

**Applicable lesson:** The collapsed-tool-card pattern is space-efficient and appropriate for read-only grounding tools. Adze should not prompt for confirmation on read-only tool execution.

---

## 3. Recommended Patterns for Adze Task Pane

### 3.1 Current Step Indicator

**Pattern: Single-line status label with verb + target.**

The existing `_runStateLabel` (a `Label` control at the bottom of the composer panel, currently showing "Running...") becomes the primary "what is happening right now" indicator.

**Display format:**

| Agent state | Label text | Example |
|-------------|------------|---------|
| Capturing context | `Capturing session context...` | |
| Calling broker/model | `Planning...` | |
| Executing tool | `Running {tool_name}...` | `Running get_dimensions...` |
| Executing tool N of M | `Running tool {N}/{M}: {tool_name}...` | `Running tool 2/4: get_dimensions...` |
| Synthesizing answer | `Synthesizing answer...` | |
| Waiting for confirmation | `Waiting for your approval...` | |
| Cancelling | `Cancelling...` | |

**WinForms implementation:**

- Use the existing `_runStateLabel` (`Label`, `Dock = DockStyle.Fill`, in the run button row).
- Update it via `PostToUi(() => _runStateLabel.Text = "Running get_dimensions...")` from the background thread at each stage transition.
- Add a subtle animated ellipsis effect. The simplest approach: cycle through ".", "..", "..." on a 500ms `Timer` while `_isRunning` is true. This avoids the need for any custom animation control. Set the label to a fixed-width portion for the ellipsis (e.g., pad the base text and cycle the trailing dots) to prevent layout jitter.

**Why not a progress bar?** The number of total steps is not always known upfront in an agentic loop (the model may decide to call more tools based on results). A progress bar that fills unpredictably is worse than no progress bar. The determinate "tool N of M" pattern works only when the plan is known; in iterative loops, use the indeterminate single-line status.

### 3.2 Activity Log (Step History)

**Pattern: Append-only mini-log in the Plan tab, styled as compact step pills.**

The current `_planBox` (a read-only `TextBox` in the Plan tab) is replaced with a richer control that shows the step history as it unfolds, not just the final plan dump.

**Two implementation options:**

**Option A: Styled TextBox (lowest effort, Phase 2 minimum).**

Keep the existing `TextBox` but append lines as steps complete:

```
[14:32:01] Planning...
[14:32:03] Plan: get_active_document, get_dimensions, get_feature_tree_slice
[14:32:03] Running get_active_document... done (23ms)
[14:32:04] Running get_dimensions... done (45ms, 12 dimensions)
[14:32:04] Running get_feature_tree_slice... done (31ms, 8 features)
[14:32:05] Synthesizing answer...
[14:32:08] Answer ready. Source: model_anthropic (claude-sonnet-4-20250514). 1,247 tokens.
```

Each line is appended in real time. The TextBox auto-scrolls to the bottom (set `SelectionStart = TextLength` then `ScrollToCaret()` after each append). Timestamps give the user a sense of pace.

Advantages: zero new controls, works with the existing `_planBox`, trivial to implement.
Disadvantages: no expand/collapse per step, no icons, monospace-only styling.

**Option B: Custom-drawn step list (Phase 3+, richer UX).**

Replace the Plan tab content with a `Panel` using owner-drawn step items. Each step is a small panel containing:
- A 16x16 status icon (gray circle = pending, blue spinner = running, green check = done, red X = failed)
- A one-line label ("get_dimensions -- 12 dimensions, 45ms")
- Optional expand/collapse to show full tool output

WinForms approach: Use a `FlowLayoutPanel` with `WrapContents = false` and `FlowDirection = TopDown`. Each step is a small `UserControl` or `Panel` with fixed height (24-28px collapsed). Expanding a step increases its height to show the detail text in a nested `TextBox`.

Advantages: compact, visually rich, expandable details without leaving the tab.
Disadvantages: more code, custom layout, potential flickering without careful double-buffering.

**Recommendation:** Start with Option A for Phase 2 (agentic loop). Migrate to Option B when write tool confirmations (Phase 3) demand richer per-step interaction.

### 3.3 Proposed Next Action Preview

**Pattern: Preview card between the activity log and the approval buttons.**

When the agent loop proposes a write operation, the agent loop pauses and the Task Pane shows a preview card. This is the "what will happen next" surface.

**Layout:**

```
+-------------------------------------------+
| Proposed Change                            |
|                                            |
|   Set D1@Sketch1                           |
|   Current: 50.0 mm                         |
|   New:     60.0 mm                         |
|                                            |
|   [Apply]  [Cancel]  [Edit value...]       |
+-------------------------------------------+
```

**WinForms implementation:**

Create a `WritePreviewPanel` -- a `Panel` that is normally invisible (`Visible = false`) and inserted into the answer panel area (or overlaid on top of the answer area). When a write preview arrives:

1. The panel becomes visible.
2. The answer panel dims or scrolls down to make room.
3. The preview panel shows the change description using `Label` controls for "Current" and "New" values.
4. Three `Button` controls: Apply, Cancel, Edit value.

Control details:

| Control | Type | Properties |
|---------|------|------------|
| Preview container | `Panel` | `Dock = DockStyle.Top`, `Height = 120`, `Visible = false`, `BackColor = Color.FromArgb(255, 252, 240)` (warm highlight), `BorderStyle = FixedSingle` |
| Title label | `Label` | `Font = Segoe UI 9.5pt Bold`, `Text = "Proposed Change"` |
| Property name | `Label` | `Font = Segoe UI 9pt`, `ForeColor = Color.FromArgb(86, 96, 108)` |
| Current value | `Label` | `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(180, 60, 60)` (muted red for "old") |
| New value | `Label` | `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(40, 120, 60)` (muted green for "new") |
| Apply button | `Button` | `FlatStyle = System`, `Width = 70`, `BackColor = default` |
| Cancel button | `Button` | `FlatStyle = System`, `Width = 70` |
| Edit button | `LinkLabel` | `Text = "Edit value..."`, opens an inline `TextBox` replacing the "New" value label |

**Color coding:** Use the red/green convention from diff views. The "current" value is in muted red (not bright -- this is information, not a warning). The "new" value is in muted green. This visual language is universally understood by engineers who use version control.

### 3.4 Waiting-for-Confirmation State

**Pattern: Modal-within-pane -- the Task Pane enters a confirmation mode where the run button changes to "Waiting..." and the preview panel is the only interactive element.**

When the agent proposes a write and waits for confirmation:

1. The `_runButton` text changes to "Waiting for approval..." and is disabled.
2. The `_runStateLabel` reads "Review the proposed change above."
3. The `_requestBox` is disabled (no new input while a confirmation is pending).
4. The `WritePreviewPanel` is the only area with active controls (Apply / Cancel / Edit).
5. A timeout label shows: "Auto-cancel in 2:00" counting down (configurable via `SOLIDWORKS_AI_WRITE_CONFIRM_TIMEOUT_MS`, default 120000ms). If the user does not respond, the agent receives a cancellation and proceeds without the write.

**After the user responds:**

- **Apply:** The preview panel disappears, the run state label updates to "Applying change...", the agent loop resumes.
- **Cancel:** The preview panel disappears, the run state label updates to "Change cancelled. Resuming...", the agent receives a cancellation result and adapts.
- **Edit:** The "New" value label becomes an editable `TextBox`. The user types a value and clicks Apply.

**Signaling mechanism:** The background agent loop thread waits on a `ManualResetEventSlim` (or `TaskCompletionSource` if using async). The UI thread sets the event when the user clicks Apply or Cancel, passing the decision back through a shared `WriteConfirmationResult` object.

```csharp
internal sealed class WriteConfirmationResult
{
    public bool Approved { get; set; }
    public string? ModifiedValue { get; set; }  // non-null if user edited the value
}
```

### 3.5 Partial Findings During Long Inspection Loops

**Pattern: Incremental answer assembly -- tool results appear in the Tools tab as they arrive, and the answer panel shows a growing summary.**

During a multi-turn agentic loop, the user should not stare at a blank answer panel for 10-30 seconds. Two complementary strategies:

**Strategy A: Live Tools tab updates.**

The Tools tab (`_toolsBox`) is updated after each tool completes, not just at the end of the run. Each tool result is appended with a separator:

```
--- get_active_document (23ms) ---
Document: Part1.SLDPRT (part)
Path: C:\SOLIDWORKS\samples\Part1.SLDPRT
Units: millimeters

--- get_dimensions (45ms) ---
12 dimensions found.
D1@Sketch1 = 50.0 mm
D2@Sketch1 = 30.0 mm
...
```

Implementation: After each tool executes in the background loop, call `PostToUi(() => AppendToolResult(toolName, result, elapsed))`. The `AppendToolResult` method appends to `_toolsBox.Text` and scrolls to the bottom.

**Strategy B: Interim answer panel message.**

While tools are still executing, the answer panel shows a placeholder that communicates progress:

```
Inspecting the document...

Found 12 dimensions and 8 features so far. Waiting for rebuild diagnostics before synthesizing the answer.
```

This is updated via `PostToUi` at key milestones (after each tool completes). It is replaced entirely when the final synthesized answer arrives.

Implementation: Add a method `UpdateInterimAnswer(string text)` that sets `_answerBox.Text` while `_isRunning` is true. The final `ApplyAssistantRunSnapshot` call overwrites it.

**Strategy C: Auto-switch to Tools tab during long runs.**

If the run exceeds a threshold (e.g., 5 seconds without completion), automatically switch `_detailsTabs.SelectedTab` to the Tools tab so the user can see results arriving. On completion, switch back to the Plan tab (or leave on Tools if the user manually navigated there).

Implementation: Start a `Timer` at run start. If it fires (5 seconds), check if the user has not manually changed tabs, then switch. Track `_userChangedTabDuringRun` via the `SelectedIndexChanged` handler.

**Recommendation:** Implement Strategy A (live Tools tab) and Strategy B (interim answer message) together for Phase 2. Strategy C (auto-switch) is a polish item.

### 3.6 Error and Recovery Presentation

**Pattern: Inline, calm, actionable -- errors appear as styled entries in the activity log, not modal dialogs or panic-red screens.**

Errors in an agentic CAD assistant fall into four categories:

| Category | Examples | Severity | User action needed? |
|----------|----------|----------|-------------------|
| Tool failure | COM exception reading features, feature tree empty | Low | Usually none -- agent self-corrects |
| API error | HTTP 429 rate limit, 500 server error, timeout | Medium | Possibly retry or check API key |
| COM/host error | SOLIDWORKS not connected, document closed mid-run | High | Reopen document, restart SOLIDWORKS |
| Agent loop error | Max iterations exceeded, model refuses to stop calling tools | Medium | Review partial results |

**Presentation rules:**

1. **Tool failures are not shown prominently in the answer panel.** They appear in the activity log (Plan tab) as a yellow-tinted line: `[14:32:04] get_rebuild_diagnostics failed: No active rebuild state. (Agent will work around this.)`. In the agentic loop, the error is sent back to the model as a `tool_result` with `is_error: true`, and the model adapts. The user does not need to act.

2. **API errors get a single line in the run state label.** Example: `API timeout. Retrying (1/2)...`. If all retries fail: `API unavailable. Falling back to local analysis.`. The answer panel shows the deterministic fallback answer, not an error message.

3. **COM/host errors stop the run and show a recovery message in the answer panel.** Example:

   ```
   The connection to SOLIDWORKS was lost during the inspection.

   This can happen if the document was closed or SOLIDWORKS was
   restarted while the assistant was running.

   To recover:
   - Make sure a document is open in SOLIDWORKS
   - Click "Run assistant" to try again
   ```

   The tone is calm and instructive, not panicked. No stack traces in the answer panel. Stack traces go to `FileLogger.Error` and appear in the Status tab for developers.

4. **Agent loop budget exhaustion shows partial results.** If the loop hits its iteration cap:

   ```
   The assistant gathered partial information but could not complete
   the full analysis within the step limit.

   Based on what was found:
   [partial answer from accumulated tool results]

   Run again with a more specific question to get a complete answer.
   ```

**WinForms specifics for error styling in the activity log (Option B step list):**

Use a `BackColor` tint on the step panel:
- Success: `Color.FromArgb(240, 248, 240)` (very faint green)
- Warning/retried: `Color.FromArgb(255, 248, 230)` (very faint amber)
- Failed (non-fatal): `Color.FromArgb(255, 243, 240)` (very faint red)
- Failed (fatal): `Color.FromArgb(255, 235, 235)` (slightly stronger red)

For the TextBox-based activity log (Option A), prefix lines with status markers:

```
[14:32:04] OK   get_dimensions (45ms, 12 dimensions)
[14:32:04] WARN get_rebuild_diagnostics failed: No active rebuild state
[14:32:05] RETRY API call timed out, retrying (1/2)...
[14:32:08] OK   Synthesis complete (1,247 tokens)
```

### 3.7 Multi-Step Plan Approval UX

**Pattern: Checklist with per-step and batch controls, presented before execution begins.**

This pattern is relevant starting in Phase 4 (advanced writes). When the agent proposes a multi-step write plan:

**Layout:**

```
+-------------------------------------------+
| Agent Plan (3 steps)                       |
|                                            |
| [x] 1. Set D1@Sketch1: 50mm -> 60mm       |
| [x] 2. Set Material to "Aluminum 6061"    |
| [ ] 3. Suppress Fillet1                    |
|                                            |
| [Apply checked (2)]  [Cancel all]          |
|                                            |
| Estimated undo: all steps in one group     |
+-------------------------------------------+
```

**WinForms implementation:**

Use a `CheckedListBox` with `CheckOnClick = true` for the step list. Each item is a one-line description. Below the list, two `Button` controls:

- "Apply checked (N)" -- dynamically updates its text to show the count of checked items.
- "Cancel all" -- cancels the entire plan and sends a cancellation back to the agent.

Below the buttons, a `Label` showing the undo behavior: "All applied steps will be grouped as a single undo operation." This sets expectations.

**Per-step detail expansion:** When the user clicks a step (not the checkbox), show a tooltip or expand a detail section below the list showing the before/after preview for that specific step. This reuses the `WritePreviewPanel` from section 3.3 but parameterized for the selected step.

**Step-at-a-time approval (alternative):** Instead of a batch checklist, present one step at a time:

```
+-------------------------------------------+
| Step 1 of 3                                |
|                                            |
|   Set D1@Sketch1                           |
|   Current: 50.0 mm -> New: 60.0 mm        |
|                                            |
|   [Apply & next]  [Skip]  [Cancel all]    |
+-------------------------------------------+
```

This is simpler and works better when steps have complex previews that need individual attention. The user sees one preview, decides, and moves to the next.

**Recommendation:** Use step-at-a-time for Phase 3 (first write tools, which are individual operations). Use the batch checklist for Phase 4 (when the agent proposes multi-step plans).

### 3.8 Balancing Transparency with Noise

**Pattern: Three-tier disclosure -- glance, scan, dig.**

The fundamental tension: engineers want to know what the agent is doing (trust requires transparency), but they do not want to read every API call (they have CAD work to do). The solution is progressive disclosure at three tiers:

**Tier 1 -- Glance (always visible, zero effort):**
- The `_runStateLabel` shows one sentence: what is happening right now.
- The answer panel shows the final answer or an interim summary.
- This is all a busy user needs.

**Tier 2 -- Scan (one click, the Plan tab):**
- The activity log shows each step with timestamps and one-line summaries.
- Steps are collapsed by default. The user can scan the list in 3-5 seconds and understand the flow.
- Errors and warnings are visually distinct (colored prefix or background tint).

**Tier 3 -- Dig (expand a step, the Tools tab):**
- Expanding a step in the activity log shows the full tool input and output.
- The Tools tab shows the complete raw tool results.
- The Status tab shows the session dashboard, token usage, and diagnostics.
- The file log (`%LOCALAPPDATA%\Adze\logs`) has full trace details.

**What to hide by default:**

| Information | Tier | Rationale |
|-------------|------|-----------|
| Current step name + target | 1 (Glance) | Users need to know the agent is working and what it is doing |
| Step count ("tool 2 of 4") | 1 (Glance) | Gives a sense of progress without detail |
| Each step's result summary ("12 dimensions found") | 2 (Scan) | Useful for trust-building but not essential during a run |
| Full tool output JSON | 3 (Dig) | Only needed for debugging or verification |
| API request/response bodies | 3 (Dig, log file) | Developer-only information |
| Token usage per call | 2 (Scan, in activity log) | Power users care about cost; it should not dominate the UI |
| Cumulative session tokens | 1 (Glance, in run state label after completion) | Users should know their spend at a glance |
| Model reasoning / chain-of-thought | 3 (Dig, expand in activity log) | Interesting but noisy; collapse by default |
| Error details and stack traces | 3 (Dig, Status tab and log file) | Never show stack traces in Tier 1 or 2 |

**What to always surface (never hide):**

- The fact that the agent is running (vs. idle).
- The fact that the agent is waiting for user input (confirmation gate).
- Fatal errors that require user action (document closed, SOLIDWORKS disconnected).
- The answer source (model vs. deterministic fallback) and token count.

---

## 4. Phased Implementation Recommendations

### Phase 2 Additions (Agentic Loop)

These changes coincide with the `discovery-agent-loop-architecture.md` implementation.

**4.1 Granular run state updates.**

Modify the agent loop runner to emit progress callbacks that the UI thread receives via `PostToUi`. Each callback carries a `RunProgressUpdate` value object:

```csharp
internal sealed class RunProgressUpdate
{
    public string StatusText { get; set; } = string.Empty;      // "Running get_dimensions..."
    public string? ToolName { get; set; }                        // "get_dimensions" or null
    public int? StepIndex { get; set; }                          // 1-based, or null if unknown
    public int? StepCount { get; set; }                          // total planned steps, or null
    public string? InterimAnswerText { get; set; }               // partial answer for the answer panel
    public string? ActivityLogLine { get; set; }                 // append to Plan tab log
    public string? ToolsTabAppend { get; set; }                  // append to Tools tab
}
```

The background thread calls `PostToUi(() => ApplyProgressUpdate(update))` at each transition. The `ApplyProgressUpdate` method updates `_runStateLabel`, appends to `_planBox`, appends to `_toolsBox`, and optionally updates `_answerBox`.

Effort: small -- it is plumbing from the loop runner through `HostState` to `TaskPaneControl`.

**4.2 Animated ellipsis on run state label.**

Add a `Timer` (250ms interval) that cycles the trailing dots on `_runStateLabel.Text` while `_isRunning`. Store the base text separately from the animated suffix.

```csharp
private string _runStateBaseText = "Ready.";
private int _ellipsisTick;

// In the ellipsis timer tick:
if (_isRunning)
{
    int dots = (_ellipsisTick++ % 3) + 1;
    _runStateLabel.Text = _runStateBaseText + new string('.', dots);
}
```

Effort: trivial.

**4.3 Live Tools tab updates.**

Change `_toolsBox` from write-once-at-end to append-as-tools-complete. Use a helper:

```csharp
private void AppendToolResult(string toolName, string resultSummary, long elapsedMs)
{
    string separator = _toolsBox.TextLength > 0
        ? Environment.NewLine + Environment.NewLine
        : string.Empty;
    string entry = separator +
        "--- " + toolName + " (" + elapsedMs + "ms) ---" +
        Environment.NewLine +
        resultSummary;
    _toolsBox.AppendText(entry);
}
```

`TextBox.AppendText` is efficient and auto-scrolls.

Effort: small.

**4.4 Cancellation button.**

When `_isRunning`, change `_runButton.Text` to "Cancel" and wire its click to set a `CancellationTokenSource`. On cancellation, the agent loop breaks, and the UI shows partial results.

Effort: small (the button toggle is trivial; the cancellation plumbing is part of the agent loop implementation).

### Phase 3 Additions (First Write Tools)

**4.5 Write preview panel.**

Build the `WritePreviewPanel` described in section 3.3. It is a `Panel` with `Visible = false` by default, inserted into `answerPanel` (above the answer text box, below the title label). When the agent loop sends a write preview, call `PostToUi(() => ShowWritePreview(preview))`.

Key properties:

| Control | WinForms Type | Key Properties |
|---------|---------------|----------------|
| Preview container | `Panel` | `Dock = DockStyle.Top`, `Height = 120`, `Visible = false`, `BackColor = Color.FromArgb(255, 252, 240)`, `Padding = new Padding(12, 10, 12, 10)` |
| Title | `Label` | `Dock = DockStyle.Top`, `Font = Segoe UI 9.5pt Bold`, `Height = 22` |
| Property name | `Label` | `Dock = DockStyle.Top`, `Font = Segoe UI 9pt`, `Height = 20` |
| Current value | `Label` | `Dock = DockStyle.Top`, `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(180, 60, 60)`, `Height = 20` |
| New value | `Label` | `Dock = DockStyle.Top`, `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(40, 120, 60)`, `Height = 20` |
| Button row | `FlowLayoutPanel` | `Dock = DockStyle.Top`, `Height = 32`, `FlowDirection = LeftToRight` |
| Apply button | `Button` | `Width = 70`, `FlatStyle = System` |
| Cancel button | `Button` | `Width = 70`, `FlatStyle = System` |
| Edit link | `LinkLabel` | `Text = "Edit value..."` |

Effort: moderate (new panel, signaling to background thread, timeout logic).

**4.6 Confirmation state machine.**

The Task Pane needs a state machine to manage the confirmation lifecycle:

```
Idle -> Running -> (WaitingForConfirmation -> Running) -> Completed
                                           \-> Running (cancelled)
```

States:
- `Idle`: all controls enabled, run button says "Run assistant".
- `Running`: request box disabled, run button says "Cancel", status label shows current step.
- `WaitingForConfirmation`: preview panel visible, run button says "Waiting...", only preview panel buttons are interactive.
- `Completed`: preview panel hidden, run button says "Run assistant", answer shows final result.

Implement as an enum field `_paneState` with a `TransitionTo(PaneState)` method that enables/disables the correct controls.

Effort: moderate.

### Phase 4 Additions (Multi-Step Plans)

**4.7 Plan review panel.**

Replace the write preview panel with a plan review panel when the agent proposes multiple steps. Use a `CheckedListBox` for the step list with Apply/Cancel buttons.

The `CheckedListBox` is already a proven control in the codebase (proposed in `discovery-clarification-ui.md` for the scope axis). The same control works here with different data.

**4.8 Step-at-a-time mode.**

For simpler initial implementation, present steps one at a time using the same `WritePreviewPanel` from Phase 3 with a "Step N of M" header and "Apply & Next" / "Skip" / "Cancel All" buttons.

---

## 5. Specific WinForms Control Recommendations Summary

| Need | Recommended Control | Alternative | Notes |
|------|-------------------|-------------|-------|
| Current step indicator | `Label` (existing `_runStateLabel`) | -- | Animated ellipsis via Timer |
| Activity log (Phase 2) | `TextBox` (existing `_planBox`, append mode) | `RichTextBox` for colored prefixes | RichTextBox is heavier but supports per-line coloring |
| Activity log (Phase 3+) | `FlowLayoutPanel` with child `Panel` items | `ListView` in Details view | FlowLayoutPanel is simpler; ListView supports icons natively |
| Write preview | `Panel` with child Labels + Buttons | -- | Owner-drawn for more visual polish |
| Multi-step checklist | `CheckedListBox` | `ListView` with checkboxes | CheckedListBox is simpler; ListView adds column flexibility |
| Timeout countdown | `Label` with `Timer` | -- | Update every 1s |
| Step status icons | `PictureBox` (16x16) in step panel | `ImageList` on `ListView` | Draw from embedded resources |
| Expand/collapse step | `LinkLabel` toggle + `Panel.Visible` | `TreeView` with node expand | TreeView is overkill for a flat list |
| Error tinting (TextBox log) | Prefix markers (`OK`, `WARN`, `ERR`) | `RichTextBox` with colored lines | RichTextBox allows `SelectionColor` per line |

### RichTextBox vs TextBox for the Activity Log

The standard `TextBox` cannot color individual lines. For Phase 2, this is acceptable -- prefix markers (`OK`, `WARN`) provide enough visual distinction in a monospace font.

For Phase 3+, consider migrating the Plan tab to a `RichTextBox`:
- `RichTextBox` supports `SelectionColor` and `SelectionBackColor` per line.
- Append a line, select it, set its color, then deselect. This is the standard pattern for colored logging in WinForms.
- `RichTextBox` is heavier than `TextBox` (it loads the Windows Rich Edit control). In a Task Pane with modest text volumes (dozens of lines, not thousands), this is not a concern.
- Set `DetectUrls = false` to avoid the RichTextBox's default URL-detection behavior.
- Set `ShortcutsEnabled = false` if you want to prevent the user from accidentally formatting the read-only content.

Example append-with-color helper:

```csharp
private void AppendColoredLine(RichTextBox target, string text, Color foreColor)
{
    target.SelectionStart = target.TextLength;
    target.SelectionLength = 0;
    target.SelectionColor = foreColor;
    target.AppendText(text + Environment.NewLine);
    target.SelectionColor = target.ForeColor;  // reset
    target.ScrollToCaret();
}
```

---

## 6. Anti-Patterns to Avoid

### 6.1 Modal Dialogs for Confirmations

Do not use `MessageBox.Show()` or custom `Form` dialogs for write confirmations. Modal dialogs steal focus from SOLIDWORKS and feel disruptive in a sidebar workflow. All confirmations should be inline within the Task Pane.

### 6.2 Full Chain-of-Thought Display

Do not stream the model's internal reasoning to the answer panel. Engineering users do not want to read "I should check the dimensions first because the user asked about sizing." That information belongs in the Dig tier (collapsed in the activity log, or in the log file). The answer panel shows answers and interim summaries, not reasoning.

### 6.3 Flashing or Pulsing Animations

WinForms does not have smooth animation support without custom painting. Avoid attempting CSS-style pulse effects on panels or labels. The animated ellipsis (section 4.2) is sufficient for indicating activity. Anything more complex will look janky in the Win32 rendering pipeline.

### 6.4 Auto-Expanding the Task Pane

Do not attempt to programmatically resize the SOLIDWORKS Task Pane panel width. The user controls the sidebar width. Design for the minimum expected width (300px) and let content reflow.

### 6.5 Sound or System Tray Notifications

Do not play sounds or show toast notifications when runs complete. The Task Pane is a passive sidebar. The user will check it when they are ready.

### 6.6 Progress Bars for Indeterminate Work

Do not show a `ProgressBar` in `Marquee` mode for API calls. It communicates "something is happening" but conveys no information the status label does not already provide. It also takes vertical space in a narrow layout. The animated ellipsis on the status label is more compact and equally informative.

---

## 7. Open Questions

1. **Should the Tools tab show raw JSON or formatted summaries?** Current implementation shows formatted text. For Tier 3 transparency, consider a toggle ("Raw / Formatted") at the top of the Tools tab.

2. **Should the activity log persist across runs?** The current Plan tab is cleared on each run. For iterative workflows, keeping a session-long log (with run separators) would let the user see the history. Risk: the log grows unbounded. Mitigation: cap at the last 5 runs or 500 lines and truncate older entries.

3. **Should the agent's thought narration (Devin-style) be included in the activity log?** If the model returns reasoning text between tool calls, it could be shown as a lighter-styled line in the log. This aids transparency but adds noise. Recommendation: include it, but style it distinctly (italic or gray text) so the user can skip it visually.

4. **Should the answer panel stream token-by-token during synthesis?** Streaming requires SSE or chunked HTTP reading, which adds complexity to the `.NET 4.8` `HttpWebRequest` client. The `discovery-agent-loop-architecture.md` and `END-GOAL.md` both defer streaming to Phase 7. For now, show the interim placeholder ("Synthesizing answer...") and then replace with the full answer.

5. **Should write confirmations have a keyboard shortcut?** Recommendation: yes. `Enter` for Apply, `Escape` for Cancel. These are standard Windows conventions and work without the user moving to the mouse.

---

## 8. Key Takeaways

1. **The status label is the most important surface.** One sentence, always visible, always current. Invest in getting this right first.

2. **Append-only activity logs beat progress bars.** They give a sense of pace, build trust, and provide an audit trail. Start with a simple TextBox; upgrade to RichTextBox or FlowLayoutPanel later.

3. **Write confirmations must be inline, not modal.** A preview panel in the Task Pane with Apply/Cancel buttons. Never steal focus from SOLIDWORKS with a dialog box.

4. **Three-tier disclosure (glance/scan/dig) resolves the transparency-vs-noise tension.** Most users will never leave Tier 1. Power users will scan Tier 2. Developers will dig into Tier 3. Design all three, but do not force Tier 2 or 3 on Tier 1 users.

5. **Errors should be calm and actionable.** Tool failures are handled by the agent. API failures trigger fallback. Only host disconnection requires user action, and the recovery guidance should read like a helpful note, not a crash report.

6. **Phase the investment.** Simple TextBox log + granular status label for Phase 2. Write preview panel for Phase 3. Rich step list and batch plan UI for Phase 4. Do not build the Phase 4 UI before Phase 2 is working.
