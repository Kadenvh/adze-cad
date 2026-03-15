# Research: Agent Loop Threading Architecture for SOLIDWORKS Add-In

**Date:** 2026-03-15
**Status:** De-risking research, implementation-ready
**Scope:** Thread safety, COM apartment rules, UI marshaling, cancellation, and streaming patterns for running a multi-step AI agent loop inside the Adze SOLIDWORKS add-in
**Grounded in:** `src/Adze.Host/UI/TaskPaneControl.cs`, `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Host/Services/SessionContextBuilder.cs`, `src/Adze.Host/AddIn/AdzeAddIn.cs`, `documentation/plans/discovery-agent-loop-architecture.md`

---

## 1. Platform Constraints: The Non-Negotiable Facts

### 1.1 SOLIDWORKS is an STA COM Server

SOLIDWORKS runs on the main UI thread inside a Single-Threaded Apartment (STA). Every COM interface pointer obtained from SOLIDWORKS (`ISldWorks`, `ModelDoc2`, `Feature`, `SelectionMgr`, `Dimension`, `Mate2`, `Component2`, etc.) is bound to this STA thread. These are the platform-level facts:

- **All SOLIDWORKS COM calls must execute on the thread that created the COM object**, which is the SOLIDWORKS main UI thread (the same thread that hosts Windows Forms controls in the Task Pane).
- The .NET Runtime Callable Wrapper (RCW) that wraps each COM interface does not automatically marshal cross-apartment calls for in-process add-ins the way it would for out-of-process COM. SOLIDWORKS add-ins are loaded in-process via `ISwAddin.ConnectToSW()`, meaning the add-in DLL shares the SOLIDWORKS process and its main STA thread.
- Calling a SOLIDWORKS COM method from a background thread (which lives in the MTA by default) will either:
  1. Silently produce wrong results or corrupt internal state.
  2. Throw a `System.Runtime.InteropServices.COMException` with `RPC_E_WRONG_THREAD` (0x8001010E).
  3. Deadlock if the STA thread is blocked waiting for the background thread while the background thread tries to marshal a COM call back to the STA.
  4. Crash SOLIDWORKS outright.
- **There is no safe way to call SOLIDWORKS COM from a non-STA thread**, period. Not with locks, not with `lock (application)`, not with `Monitor`, not with `Mutex`. The apartment model is enforced at the COM infrastructure level, below the .NET runtime.

### 1.2 The Task Pane Control Lives on the STA Thread

The `TaskPaneControl` is a `System.Windows.Forms.UserControl` created by SOLIDWORKS via COM activation (`TaskpaneView.AddControl`). It runs on the SOLIDWORKS main UI thread. This thread pumps both Win32 messages and COM calls. This is the same thread that:

- Receives SOLIDWORKS event callbacks (`ActiveModelDocChangeNotify`, `NewSelectionNotify`, etc.).
- Processes Windows Forms message loop events (button clicks, timer ticks, paint).
- Executes COM API calls to SOLIDWORKS.

Blocking this thread for any significant duration (more than ~50ms) causes:

- The SOLIDWORKS UI to freeze (no model rotation, no menu interaction, no button response).
- COM callback delivery to stall (event notifications queue up).
- Windows to report the application as "Not Responding" after ~5 seconds of blocked message pump.

### 1.3 .NET Framework 4.8 Constraints

The project targets .NET Framework 4.8 (confirmed in `Adze.Host.csproj`: `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>`). This means:

- `System.Threading.CancellationTokenSource` and `CancellationToken` are available (since .NET 4.0).
- `System.Threading.Tasks.Task` and `Task<T>` are available (since .NET 4.0).
- `async`/`await` are available at the language level (C# latest is enabled via `<LangVersion>latest</LangVersion>`).
- **However**, `System.Net.Http.HttpClient` is not referenced. The existing HTTP calls use `System.Net.HttpWebRequest` with synchronous `.GetResponse()`. Adding `HttpClient` would require a NuGet package or framework reference change.
- `Task.Run()` is available and is the modern replacement for `ThreadPool.QueueUserWorkItem`.
- `SynchronizationContext` is available. Windows Forms installs a `WindowsFormsSynchronizationContext` on the UI thread, which `Control.BeginInvoke` / `Control.Invoke` rely on internally.

### 1.4 Current Threading Pattern (Observed in Code)

From `TaskPaneControl.RunAssistant()` (lines 475-518):

```
[UI thread]  Click handler fires
[UI thread]  HostState.PrepareAssistantRun()     -- calls COM via SessionContextBuilder.Build()
[UI thread]  ThreadPool.QueueUserWorkItem()       -- hands off to background
[BG thread]  HostState.CompleteAssistantRun()     -- API calls, tool execution, synthesis
[BG thread]  PostToUi(() => ApplySnapshot())      -- marshals result back
[UI thread]  ApplyAssistantRunSnapshot()          -- updates TextBox controls
```

`PostToUi()` (lines 709-727) uses `BeginInvoke` (asynchronous, fire-and-forget). This is correct for the final result delivery and progress updates. The existing pattern is sound but needs extension for the multi-turn loop.

---

## 2. Recommended Runtime Design

### 2.1 Thread Responsibility Matrix

| Responsibility | Thread | Why |
|---|---|---|
| All SOLIDWORKS COM calls (`ISldWorks`, `ModelDoc2`, `Feature`, etc.) | UI thread (STA) | COM apartment rules. No exceptions. |
| `SessionContextBuilder.Build()` | UI thread | Traverses COM objects (features, dimensions, mates, references). |
| Windows Forms control reads/writes (`TextBox.Text`, `Label.Text`, `Button.Enabled`) | UI thread | Windows Forms affinity. |
| HTTP API calls to OpenAI/Anthropic | Background thread | Blocking I/O, 2-30 seconds. Must not block UI. |
| JSON serialization/deserialization of API payloads | Background thread | CPU work, can be heavy for large payloads. |
| Tool execution against `SessionContext` | Background thread | Tools are pure functions over serialized data. No COM. |
| Conversation state management | Background thread | In-memory list manipulation, no thread safety concerns within the single agent loop thread. |
| Trace recording, file I/O | Background thread | Disk I/O should not block UI. |
| Progress updates to UI | Marshaled from BG to UI via `BeginInvoke` | Fire-and-forget is correct for status text. |
| Mid-loop COM recapture (if needed) | Marshaled from BG to UI via `Invoke` | Must block BG thread until fresh data is captured. |
| Cancellation signal | UI thread sets the token; BG thread checks it | `CancellationTokenSource` is thread-safe by design. |

### 2.2 Core Execution Flow

```
[UI Thread - STA]                              [Background Thread - MTA/ThreadPool]
=================                              ====================================

User clicks "Run assistant"
  |
  |-- _isRunning = true
  |-- Disable run button, enable cancel button
  |-- _agentCancellation = new CancellationTokenSource()
  |-- context = HostState.PrepareAssistantRun()    [COM capture happens here]
  |-- ThreadPool.QueueUserWorkItem() --->          |
  |                                                |
  |   (UI thread returns to message pump)          |-- Build initial conversation messages
  |   (SOLIDWORKS remains responsive)              |
  |                                                |-- LOOP:
  |                                                |   |
  |                                                |   |-- token.ThrowIfCancellationRequested()
  |                                                |   |
  |                                                |   |-- API call (HttpWebRequest, blocking)
  |                                                |   |   (2-30 seconds)
  |                                                |   |
  |                                                |   |-- Parse response
  |                                                |   |
  |   <-- BeginInvoke(progress update) ------------|   |-- PostToUi("Thinking... turn 2")
  |   _runStateLabel.Text = "Thinking..."          |   |
  |                                                |   |-- if tool_use:
  |                                                |   |     |
  |                                                |   |     |-- [Optional: needs fresh COM data?]
  |   <-- Invoke(COM recapture) -------------------|   |     |   Control.Invoke(() => {
  |   context = SessionContextBuilder.Build(app)   |   |     |     return BuildContextUnsafe(app);
  |   return context;  --------------------------->|   |     |   })  // BG thread blocks here
  |                                                |   |     |
  |   <-- BeginInvoke(progress) -------------------|   |     |-- PostToUi("Running get_dims...")
  |   _runStateLabel.Text = "Running..."           |   |     |
  |                                                |   |     |-- result = tool.Execute(context)
  |                                                |   |     |     [pure function, no COM]
  |                                                |   |     |
  |                                                |   |     |-- Append tool_result to messages
  |                                                |   |     |-- continue LOOP
  |                                                |   |
  |                                                |   |-- if text response:
  |                                                |   |     break LOOP
  |                                                |   |
  |                                                |   |-- if max iterations:
  |                                                |   |     break LOOP
  |                                                |
  |   <-- BeginInvoke(final result) ---------------|-- PostToUi(() => ApplyResult(result))
  |   Apply answer, plan, tools to UI              |
  |   Re-enable run button                         |
  |   _isRunning = false                           |
  |                                                |
  |   (message pump continues)                     (thread returns to pool)
```

### 2.3 Why This Shape Is Correct

1. **COM calls never leave the STA thread.** `PrepareAssistantRun()` runs on the UI thread before the background work starts. Mid-loop COM refresh uses `Control.Invoke`, which marshals execution back to the UI thread and blocks the background thread until it completes.

2. **The UI thread never blocks for more than milliseconds.** COM context capture (`SessionContextBuilder.Build`) takes single-digit milliseconds because it reads in-memory COM properties. The `Invoke` call from the background thread briefly borrows the UI thread for this capture, then returns.

3. **API calls never touch the UI thread.** All HTTP I/O runs on the background thread. The UI message pump continues to process SOLIDWORKS interactions during the 2-30 second API calls.

4. **Progress updates are non-blocking.** `BeginInvoke` (asynchronous) is used for progress text because the background thread does not need to wait for the UI to repaint.

5. **A single background thread runs the loop.** No concurrent tool execution, no parallel API calls. This eliminates an entire class of thread-safety bugs with zero measurable performance cost (tools execute in microseconds against in-memory data).

---

## 3. Detailed Pattern Specifications

### 3.1 Control.Invoke vs. Control.BeginInvoke

These are the two mechanisms for marshaling work from a background thread to the UI thread. They have different semantics and using the wrong one causes deadlocks or race conditions.

**`Control.Invoke(Delegate)`** -- Synchronous marshal.

- Posts a message to the UI thread's message queue and **blocks the calling thread** until the delegate executes on the UI thread and returns.
- The calling thread does not proceed until the UI thread has completed the work.
- Returns the delegate's return value (if any).
- **Use for:** Operations where the background thread needs the result before continuing. The only case in the agent loop is mid-loop COM recapture.
- **Deadlock risk:** If the UI thread is blocked waiting for the background thread (e.g., `Thread.Join()`, `Task.Wait()`, `ManualResetEvent.WaitOne()`), and the background thread calls `Invoke`, both threads are stuck. **This is the #1 threading bug in SOLIDWORKS add-ins.** The prevention is simple: the UI thread must never synchronously wait on the background thread.

```csharp
// CORRECT: Background thread calls Invoke to get fresh COM data.
// UI thread is free (pumping messages), so Invoke completes promptly.
SessionContext freshContext = (SessionContext)_control.Invoke(
    new Func<SessionContext>(() => HostState.CaptureContext()));
```

```csharp
// DEADLOCK: UI thread waits for BG thread, BG thread calls Invoke.
// UI thread:
_backgroundTask.Wait();  // BLOCKED -- waiting for BG thread
// BG thread:
_control.Invoke(...);    // BLOCKED -- waiting for UI thread to pump
// Both threads are stuck forever.
```

**`Control.BeginInvoke(Delegate)`** -- Asynchronous marshal.

- Posts a message to the UI thread's message queue and **returns immediately** to the calling thread.
- The delegate will execute on the UI thread at some future point when the message pump processes it.
- Returns an `IAsyncResult` that can be used with `EndInvoke`, but this is rarely needed.
- **Use for:** Fire-and-forget operations. Progress updates, final result delivery, UI state changes.
- **No deadlock risk** because the calling thread never waits.

```csharp
// CORRECT: Background thread fires progress update and continues immediately.
_control.BeginInvoke(new Action(() =>
{
    _runStateLabel.Text = "Running get_dimensions... (turn 2 of 10)";
}));
```

**Decision matrix for the agent loop:**

| Operation | Method | Why |
|---|---|---|
| Progress text update | `BeginInvoke` | BG thread does not need to wait for repaint. |
| Intermediate tool result append | `BeginInvoke` | Fire-and-forget UI update. |
| Final result delivery | `BeginInvoke` | Fire-and-forget; `FinishAssistantRunUi` runs after. |
| Mid-loop COM context recapture | `Invoke` | BG thread needs the `SessionContext` before executing the tool. |
| Cancel button state change | Direct (UI thread) | Cancel click handler runs on UI thread already. |

### 3.2 PostToUi Pattern (Existing, Extend)

The existing `PostToUi` method in `TaskPaneControl` (lines 709-727) is well-written:

```csharp
private void PostToUi(Action action)
{
    if (action == null || IsDisposed) return;
    try
    {
        if (!IsHandleCreated) return;
        BeginInvoke(action);
    }
    catch (InvalidOperationException)
    {
        // Control was disposed between the check and the call.
        // Swallow -- the UI is gone, nothing to update.
    }
}
```

This pattern is correct and should be reused for all fire-and-forget UI updates from the agent loop. The `InvalidOperationException` catch handles the race condition where the control is disposed between the `IsHandleCreated` check and the `BeginInvoke` call.

For the synchronous COM recapture case, add a parallel method:

```csharp
/// <summary>
/// Marshals a function to the UI thread and blocks until it returns.
/// Used exclusively for mid-loop COM recapture where the background
/// thread needs the result before proceeding.
/// </summary>
private T InvokeOnUi<T>(Func<T> func)
{
    if (func == null) throw new ArgumentNullException(nameof(func));
    if (IsDisposed || !IsHandleCreated)
        throw new ObjectDisposedException(nameof(TaskPaneControl));

    try
    {
        return (T)Invoke(func);
    }
    catch (InvalidOperationException)
    {
        // Control disposed during Invoke -- treat as cancellation.
        throw new OperationCanceledException(
            "Task Pane was disposed during agent loop execution.");
    }
}
```

### 3.3 ThreadPool.QueueUserWorkItem vs. Task.Run

Both are available on .NET 4.8. The existing code uses `ThreadPool.QueueUserWorkItem`. For the agent loop, either works, but `Task.Run` has a minor advantage:

| Feature | `ThreadPool.QueueUserWorkItem` | `Task.Run` |
|---|---|---|
| Returns a handle to the work | No | Yes (`Task`) |
| Exception propagation | Must catch in callback | Can observe via `Task.Exception` (but we catch internally anyway) |
| `CancellationToken` support | Not built in (pass manually) | `Task.Run(action, token)` checks token before scheduling |
| Available on .NET 4.8 | Yes | Yes |

**Recommendation:** Use `Task.Run` for the agent loop. It integrates cleanly with `CancellationToken` and the returned `Task` handle can be stored for diagnostic purposes (though we should never `.Wait()` on it from the UI thread).

```csharp
private CancellationTokenSource? _agentCancellation;
private Task? _agentTask;  // For diagnostics only. NEVER .Wait() on UI thread.

private void RunAssistant()
{
    if (_isRunning) return;

    _isRunning = true;
    _agentCancellation = new CancellationTokenSource();
    CancellationToken token = _agentCancellation.Token;

    // --- UI thread: capture COM state ---
    AssistantRunPreparation preparation;
    try
    {
        preparation = HostState.PrepareAssistantRun(GetRequestText());
    }
    catch (Exception ex)
    {
        ShowRunFailure(ex);
        FinishAssistantRunUi();
        return;
    }

    // --- Transition to cancel-capable UI state ---
    SwitchToCancelMode();

    // --- Launch background agent loop ---
    _agentTask = Task.Run(() =>
    {
        try
        {
            AgentLoopResult result = RunAgentLoopOnBackground(preparation, token);
            PostToUi(() => ApplyAgentResult(result));
        }
        catch (OperationCanceledException)
        {
            PostToUi(() => ShowCancelled());
        }
        catch (Exception ex)
        {
            PostToUi(() => ShowRunFailure(ex));
        }
        finally
        {
            PostToUi(FinishAssistantRunUi);
        }
    }, token);
}
```

### 3.4 CancellationTokenSource Lifecycle

`CancellationTokenSource` is thread-safe: `.Cancel()` can be called from the UI thread while the background thread checks `.Token.IsCancellationRequested` or calls `.Token.ThrowIfCancellationRequested()`.

**Lifecycle:**

```
UI Thread                              Background Thread
========                              =================
new CancellationTokenSource()
  |
  |-- Pass token to Task.Run() ------> token received
  |                                     |
  |                                     |-- token.ThrowIfCancellationRequested()
  |                                     |   (passes, not cancelled yet)
  |                                     |
  |                                     |-- API call (blocking, 10 seconds)
  |                                     |
User clicks "Cancel"                    |
  |                                     |
  |-- _agentCancellation.Cancel()       |   (background thread is blocked in HTTP)
  |   (sets token.IsCancelled = true)   |
  |                                     |
  |                                     |-- API call returns (or times out)
  |                                     |-- token.ThrowIfCancellationRequested()
  |                                     |   THROWS OperationCanceledException
  |                                     |
  |   <-- PostToUi(ShowCancelled) ------|-- caught in Task.Run catch block
```

**Cancellation points in the agent loop** (ordered by where they appear in the loop):

1. **Top of each loop iteration** -- before the API call starts.
2. **After each API call returns** -- the HTTP call itself is not cancellable with `HttpWebRequest` (see section 3.5), but the token is checked immediately after.
3. **Before each tool execution** -- between processing individual `tool_use` blocks.
4. **Before mid-loop COM recapture** -- no point marshaling to the UI thread if cancelled.

```csharp
while (state.IterationCount < state.MaxIterations)
{
    // Cancellation point 1: before API call
    cancellationToken.ThrowIfCancellationRequested();

    state.IterationCount++;
    PostProgress("Thinking...", state.IterationCount);

    AgentModelResponse response = modelClient.CompleteWithTools(/*...*/);

    // Cancellation point 2: after API call
    cancellationToken.ThrowIfCancellationRequested();

    if (response.StopReason == "tool_use")
    {
        foreach (ToolUseRequest toolUse in response.ToolUses)
        {
            // Cancellation point 3: before each tool
            cancellationToken.ThrowIfCancellationRequested();

            PostProgress($"Running {toolUse.Name}...", state.IterationCount);
            ToolResult result = ExecuteTool(context, toolUse);
            state.AddToolResult(toolUse.Id, result);
        }
        continue;
    }

    // Text response -- final answer
    break;
}
```

**Disposal:** Dispose the `CancellationTokenSource` in `FinishAssistantRunUi()`:

```csharp
private void FinishAssistantRunUi()
{
    _agentCancellation?.Dispose();
    _agentCancellation = null;
    _agentTask = null;
    _isRunning = false;
    SwitchToRunMode();
    // ... existing refresh logic ...
}
```

### 3.5 HTTP Request Cancellation with HttpWebRequest

`HttpWebRequest.GetResponse()` is a blocking call with no native `CancellationToken` support. There are two approaches:

**Approach A: Timeout-based (recommended for initial implementation).**

The existing code already sets `request.Timeout` and `request.ReadWriteTimeout`. When the user cancels, the background thread is blocked in `GetResponse()` until either:
- The response arrives (then the cancellation token is checked after).
- The timeout expires (throws `WebException` with `Status == WebExceptionStatus.Timeout`).

This means cancellation has latency up to the timeout value. With a 30-second timeout, the worst case is the user waits 30 seconds after clicking Cancel. This is acceptable for Phase 1.

**Approach B: Request abort (optional improvement).**

Store a reference to the active `HttpWebRequest` and call `.Abort()` from the cancel handler:

```csharp
// In the model client:
private volatile HttpWebRequest? _activeRequest;

// In the API call method:
_activeRequest = (HttpWebRequest)WebRequest.Create(endpoint);
try
{
    // ... configure and send request ...
    using var response = (HttpWebResponse)_activeRequest.GetResponse();
    // ... read response ...
}
finally
{
    _activeRequest = null;
}

// Called from cancel handler (UI thread):
public void AbortActiveRequest()
{
    _activeRequest?.Abort();
    // .Abort() causes GetResponse() to throw WebException
    // with Status == WebExceptionStatus.RequestCanceled
}
```

The `volatile` keyword ensures the UI thread sees the most recent assignment. `HttpWebRequest.Abort()` is documented as thread-safe.

**Recommendation:** Start with Approach A. Add Approach B only if user-perceived cancellation latency is a problem in practice. The complexity cost of Approach B is low but the code path is tricky to test.

### 3.6 The Deadlock Trap: Never Wait on Background Thread from UI Thread

This is the single most dangerous pattern in the codebase. It must be documented as a hard rule.

**The rule: The UI thread must NEVER call `Task.Wait()`, `Task.Result`, `Thread.Join()`, `ManualResetEvent.WaitOne()`, or any other blocking wait that depends on the background agent loop thread completing.**

Why: The background thread may call `Control.Invoke()` for COM recapture. If the UI thread is blocked waiting on the background thread, and the background thread is blocked waiting on `Invoke` to complete on the UI thread, both threads deadlock permanently. SOLIDWORKS freezes. The user must kill the process.

```csharp
// FORBIDDEN -- will deadlock if BG thread ever calls Invoke:
private void OnFormClosing(object sender, FormClosingEventArgs e)
{
    _agentTask?.Wait();  // UI thread blocked here
    // Meanwhile, BG thread tries Invoke() for COM recapture
    // DEADLOCK: UI waits for BG, BG waits for UI
}

// CORRECT -- cancel and let the background thread finish asynchronously:
private void OnFormClosing(object sender, FormClosingEventArgs e)
{
    _agentCancellation?.Cancel();
    // Do NOT wait. The background thread will finish on its own.
    // PostToUi calls will be swallowed because the control is disposing.
}
```

---

## 4. COM Interaction Patterns

### 4.1 The SessionContext Snapshot Model

The existing architecture captures a complete `SessionContext` on the UI thread via `SessionContextBuilder.Build()` **before** any background work starts. This is the correct pattern. The `SessionContext` is a plain C# object graph with no COM references -- it is safe to read from any thread.

The snapshot model means:
- **Tools never hold COM interface pointers.** They operate on `SessionContext` properties (`context.FeatureTree.Features`, `context.Dimensions.Items`, etc.).
- **The background thread never needs to touch COM** for normal tool execution.
- **COM data can go stale** during a long agent loop (the user might change selection, open a different document, etc.), but this is acceptable for most queries.

### 4.2 Mid-Loop COM Refresh (When Needed)

Some agent loop iterations may benefit from fresh COM data:

| Scenario | Trigger | How |
|---|---|---|
| User changed selection during a long loop | Model calls `get_selection_context` | Marshal `SessionContextBuilder.Build()` to UI thread via `Invoke` |
| Model asks about a feature that was created since the loop started | Model calls `get_feature_tree_slice` | Same: full context refresh via `Invoke` |
| Document was switched | Model calls `get_active_document` | Same: full context refresh via `Invoke` |

**Implementation:**

The `AgentLoopRunner` receives a `Func<SessionContext>` delegate that, when called from the background thread, marshals to the UI thread, captures fresh COM data, and returns the new `SessionContext`:

```csharp
// Constructed in TaskPaneControl, passed to the agent loop:
Func<SessionContext> refreshContext = () =>
{
    return InvokeOnUi(() => HostState.CaptureContext());
};
```

**When to refresh:** Refreshing on every iteration is wasteful. Refresh only when:
1. The model explicitly requests a tool that would benefit from fresh data (e.g., `get_selection_context` after the initial context was captured many seconds ago).
2. A configurable "stale threshold" has elapsed (e.g., 30 seconds since last capture).

For Phase 1, **do not refresh mid-loop**. Use the initially captured context for all tool executions. This avoids the complexity of determining when a refresh is needed and eliminates the `Invoke` deadlock risk during the loop. Add mid-loop refresh in Phase 2 when there is evidence that stale data is causing bad agent behavior.

### 4.3 COM Object Lifetime: ReleaseComObject Discipline

`SessionContextBuilder.Build()` already follows correct COM lifetime practice (visible in lines 95-140, 199-233, etc.):

```csharp
Feature? feature = null;
try
{
    feature = model.IFirstFeature();
    while (feature != null) {
        Feature currentFeature = feature;
        feature = null;
        try {
            // ... use currentFeature ...
            feature = currentFeature.IGetNextFeature();
        }
        finally {
            ReleaseComObject(currentFeature);
        }
    }
}
finally
{
    ReleaseComObject(feature);
}
```

This pattern is critical: COM objects obtained from SOLIDWORKS have reference counts. Failing to release them causes memory leaks and can prevent SOLIDWORKS from shutting down cleanly. The `ReleaseComObject` helper (lines 931-946) is correctly implemented:

```csharp
private static void ReleaseComObject(object? value)
{
    if (value == null || !Marshal.IsComObject(value)) return;
    try { Marshal.ReleaseComObject(value); }
    catch (Exception ex) { FileLogger.Error("COM object release failed.", ex); }
}
```

**Rule for the agent loop:** No COM object references should ever leak into `SessionContext`, `ToolResult`, or any object that crosses from the UI thread to the background thread. The existing code already follows this rule. The agent loop does not introduce new COM interaction patterns -- it only adds more iterations of the same tool execution against the same serialized `SessionContext`.

### 4.4 RCW and Prevent Prevent Prevent: Never Store COM Pointers on Background Thread

A Runtime Callable Wrapper (RCW) is the .NET proxy for a COM interface pointer. RCWs are bound to the thread's apartment. If a background thread (MTA) accidentally obtains a reference to a SOLIDWORKS COM object (e.g., by capturing `ISldWorks` in a closure), calling methods on it will either fail or produce undefined behavior.

The existing `HostState._application` field stores the `ISldWorks` reference, but it is only accessed under `lock (Sync)` and the returned reference is only used on the UI thread (in `PrepareAssistantRun` and `BuildStatusText`, both called from UI thread context).

**Rule:** The `ISldWorks` reference (and any COM interface pointer) must never be passed to the background thread, stored in a closure that runs on the background thread, or accessed from the `AgentLoopRunner`. All COM access flows through the `SessionContext` snapshot or the `Invoke`-based refresh delegate.

---

## 5. Streaming Partial Responses into the Task Pane

### 5.1 Safe UI Update Pattern

The Task Pane uses Windows Forms `TextBox` controls for answer, plan, and tools display. Updating these from the background thread requires marshaling. The existing `ReplaceTextPreserveView` method (lines 675-706) is designed for this:

```csharp
private void ReplaceTextPreserveView(TextBox target, string text, bool preserveView)
{
    if (string.Equals(target.Text, text, StringComparison.Ordinal)) return;
    // ... save scroll position ...
    target.Text = text;
    // ... restore scroll position ...
}
```

For streaming partial responses during the agent loop, the pattern is:

```csharp
// On background thread, after each iteration or tool completion:
PostToUi(() =>
{
    // Append to tools display
    _toolsBox.Text += Environment.NewLine + "--- " + toolName + " ---" +
                      Environment.NewLine + resultSummary;
    _toolsBox.SelectionStart = _toolsBox.TextLength;
    _toolsBox.ScrollToCaret();
});
```

### 5.2 Throttling UI Updates

If the agent loop runs fast (e.g., multiple tool executions in quick succession), posting a `BeginInvoke` for each one can flood the message queue and cause the UI to flicker. Throttle updates:

```csharp
private DateTime _lastProgressUpdate = DateTime.MinValue;
private const int MinProgressIntervalMs = 100;  // 10 updates/sec max

private void PostThrottledProgress(string message)
{
    DateTime now = DateTime.UtcNow;
    if ((now - _lastProgressUpdate).TotalMilliseconds < MinProgressIntervalMs)
        return;  // Skip this update

    _lastProgressUpdate = now;
    PostToUi(() => _runStateLabel.Text = message);
}
```

For the tools panel, accumulate results and post in batches:

```csharp
// In AgentLoopRunner: accumulate a StringBuilder of tool results
// Post the full accumulated text after each tool_use iteration completes,
// not after each individual tool within the iteration.
```

### 5.3 TextBox.AppendText vs. TextBox.Text Assignment

`TextBox.AppendText(string)` is more efficient than `TextBox.Text += string` because it does not allocate a new string for the entire content. However, it does not allow scroll position preservation. For the agent loop:

- Use `TextBox.Text = fullText` (full replacement) for the answer panel, since the answer is replaced on each final response.
- Use accumulation + `TextBox.Text = accumulated` for the tools panel, since we want to show the full tool execution log.
- Use `Label.Text = message` for the status label (single-line, always replaced).

### 5.4 Streaming API Responses (Future Phase)

Both OpenAI and Anthropic support Server-Sent Events (SSE) streaming. On .NET 4.8 with `HttpWebRequest`, implementing SSE streaming requires:

```csharp
request.Headers["Accept"] = "text/event-stream";
using var response = (HttpWebResponse)request.GetResponse();
using var stream = response.GetResponseStream();
using var reader = new StreamReader(stream, Encoding.UTF8);

string? line;
while ((line = reader.ReadLine()) != null)
{
    if (cancellationToken.IsCancellationRequested) break;
    if (!line.StartsWith("data: ")) continue;
    string data = line.Substring(6);
    if (data == "[DONE]") break;

    // Parse incremental content delta
    // Post partial text to UI via BeginInvoke
    PostToUi(() => _answerBox.Text = accumulatedText);
}
```

**Risk:** `StreamReader.ReadLine()` is blocking. If the server stops sending events (network issue), the thread blocks until the read timeout. The `HttpWebRequest.ReadWriteTimeout` applies to individual read operations, so this is bounded. Cancellation between chunks is possible because `ReadLine()` returns after each line.

**Recommendation:** Defer streaming to Phase 3. The initial agent loop uses non-streaming requests. The model's tool-use responses are typically short (JSON with tool names and parameters), so the network transfer time is negligible. The wait is dominated by model inference time, which streaming cannot reduce.

---

## 6. Anti-Patterns to Avoid

### 6.1 FATAL: Calling COM from Background Thread

```csharp
// WRONG: Background thread directly accesses ISldWorks
ThreadPool.QueueUserWorkItem(_ =>
{
    ModelDoc2 model = _application.IActiveDoc2;  // COM VIOLATION
    string title = model.GetTitle();              // UNDEFINED BEHAVIOR
});
```

**Fix:** Always capture COM data into `SessionContext` on the UI thread before starting background work.

### 6.2 FATAL: Synchronous Wait on UI Thread

```csharp
// WRONG: UI thread blocks waiting for background task
private void RunAssistant()
{
    var task = Task.Run(() => RunAgentLoop());
    task.Wait();  // DEADLOCK if RunAgentLoop calls Invoke
    ApplyResult(task.Result);
}
```

**Fix:** Use fire-and-forget with `BeginInvoke` callback:
```csharp
Task.Run(() => {
    var result = RunAgentLoop();
    PostToUi(() => ApplyResult(result));
});
```

### 6.3 DANGEROUS: Storing COM References in Closures

```csharp
// WRONG: COM object captured in closure, used on background thread
ISldWorks app = _application;
Task.Run(() =>
{
    string version = app.RevisionNumber();  // COM VIOLATION
});
```

**Fix:** Extract the value on the UI thread, pass the value (not the COM reference) to the background thread.

### 6.4 DANGEROUS: Forgetting to Check IsDisposed/IsHandleCreated

```csharp
// WRONG: BeginInvoke on a disposed control throws
Task.Run(() =>
{
    BeginInvoke(new Action(() => _label.Text = "Done"));
    // Throws InvalidOperationException if control was disposed
});
```

**Fix:** Use the `PostToUi` guard pattern (already in the codebase).

### 6.5 SUBTLE: Re-entrancy in Invoke Callbacks

```csharp
// WRONG: Invoke callback triggers another background operation
// that also calls Invoke, creating nested marshaling
control.Invoke(new Action(() =>
{
    RefreshContext();  // This might trigger an event that starts another background task
    // that also calls Invoke... leading to re-entrant message pump processing
}));
```

**Fix:** Keep `Invoke` callbacks minimal -- only capture data, never trigger side effects that start new asynchronous work.

### 6.6 SUBTLE: Using lock() Around COM Calls

```csharp
// WRONG: lock does not help with COM apartment violations
lock (_sync)
{
    ModelDoc2 model = _application.IActiveDoc2;  // Still a COM violation on MTA thread
}
```

`lock` provides mutual exclusion between .NET threads. It does not change the COM apartment of the calling thread. COM apartment affinity is enforced at the COM infrastructure level, below the CLR.

### 6.7 WASTEFUL: Parallel Tool Execution

```csharp
// WRONG: Parallel tool execution adds complexity for microsecond gains
Parallel.ForEach(response.ToolUses, toolUse =>
{
    ToolResult result = ExecuteTool(context, toolUse);  // Thread-safe, but...
    lock (results) { results.Add(result); }
});
```

Tools execute against in-memory `SessionContext` data. Each tool takes microseconds to milliseconds. Parallelizing them saves negligible time while adding thread-safety complexity for the results list, ordering issues for the conversation history, and harder-to-debug failure modes.

**Fix:** Sequential tool execution within each iteration. The API call (seconds) dominates total loop time, not tool execution (microseconds).

### 6.8 SUBTLE: Timer Interaction with Agent Loop

The existing `System.Windows.Forms.Timer` (`_refreshTimer`) fires on the UI thread. The current code already stops the timer during assistant runs (`_refreshTimer.Stop()` in `RunAssistant`). This must be preserved in the agent loop path.

If the timer fires during an `Invoke` callback (possible because `Invoke` pumps messages), the timer handler could trigger a COM call that interferes with the context capture. The existing guard (`if (_isRunning) return;` in `RefreshStatus`) prevents this.

---

## 7. Timeout Architecture

### 7.1 Per-Call Timeouts

Each API call in the agent loop has its own timeout, set via `HttpWebRequest.Timeout`. This is already the pattern in both `OpenAIModelClient` and `AnthropicMessagesModelClient`.

For the agent loop, the timeout applies to each individual API call, not the entire loop. A 10-iteration loop with a 30-second timeout per call can run for up to 300 seconds total.

### 7.2 Per-Loop Timeout

Add an optional total loop timeout using `CancellationTokenSource` with a timeout:

```csharp
// In TaskPaneControl, when starting the agent loop:
int loopTimeoutMs = agentConfig.TotalLoopTimeoutMs;  // e.g., 120000 (2 minutes)

_agentCancellation = loopTimeoutMs > 0
    ? new CancellationTokenSource(loopTimeoutMs)
    : new CancellationTokenSource();
```

`CancellationTokenSource(int millisecondsDelay)` automatically sets the token to cancelled after the specified delay. The agent loop's cancellation checks will then throw `OperationCanceledException`, which is handled the same way as user-initiated cancellation but with a different message:

```csharp
catch (OperationCanceledException)
{
    bool wasUserCancelled = _agentCancellation?.IsCancellationRequested == true
        && !_agentCancellation.Token.WaitHandle.WaitOne(0);  // Not reliable; use a flag instead

    // Better: set a flag when user clicks Cancel
    PostToUi(() => ShowCancelled(wasTimedOut: !_userRequestedCancel));
}
```

**Simpler approach with a dedicated flag:**

```csharp
private bool _userRequestedCancel;

private void CancelAssistant()
{
    _userRequestedCancel = true;
    _agentCancellation?.Cancel();
}

// In FinishAssistantRunUi:
_userRequestedCancel = false;
```

### 7.3 Timeout Hierarchy

```
Total loop timeout (e.g., 120s)
  |
  |-- Iteration 1
  |     |-- API call timeout (e.g., 30s)
  |     |-- Tool execution (no explicit timeout; tools are sub-millisecond)
  |
  |-- Iteration 2
  |     |-- API call timeout (e.g., 30s)
  |     |-- Tool execution
  |     ...
  |
  |-- Whichever triggers first (loop timeout or max iterations) stops the loop
```

---

## 8. Error Recovery Patterns

### 8.1 Exception Types and Where They Occur

| Exception | Source | Thread | Handling |
|---|---|---|---|
| `WebException` (timeout) | `HttpWebRequest.GetResponse()` | Background | Retry once if iterations remain, then fall back to deterministic. |
| `WebException` (rate limit, 429) | `HttpWebRequest.GetResponse()` | Background | Wait 1-2 seconds, retry once. Then fall back. |
| `WebException` (auth, 401/403) | `HttpWebRequest.GetResponse()` | Background | Stop loop immediately. Return error. |
| `WebException` (server, 500+) | `HttpWebRequest.GetResponse()` | Background | Retry once. Then fall back. |
| `COMException` | `SessionContextBuilder.Build()` via `Invoke` | UI (marshaled) | Catch in `Invoke` callback. Use stale context. Log warning. |
| `InvalidOperationException` | `Control.Invoke()` / `BeginInvoke()` | Background | Control disposed. Treat as cancellation. Return partial results. |
| `OperationCanceledException` | `CancellationToken.ThrowIfCancellationRequested()` | Background | Caught in `Task.Run` wrapper. Show cancellation UI. |
| `Exception` from tool execution | `tool.Execute(context, params)` | Background | Caught per-tool. Create error `ToolResult`. Send as `tool_result` with `is_error: true`. Model self-corrects. |
| `OutOfMemoryException` | Anywhere (large API responses) | Either | Let it propagate. The CLR will handle it. Not recoverable in-process. |

### 8.2 Consecutive Error Budget

```csharp
int consecutiveApiErrors = 0;
const int maxConsecutiveApiErrors = 2;

while (state.IterationCount < state.MaxIterations)
{
    // ... cancellation check ...

    AgentModelResponse response = modelClient.CompleteWithTools(/*...*/);

    if (!response.Success)
    {
        consecutiveApiErrors++;
        if (consecutiveApiErrors >= maxConsecutiveApiErrors)
        {
            // Too many consecutive API failures. Fall back to deterministic.
            return CreateFallbackResult(state);
        }
        // Log the error, continue loop (model might recover)
        continue;
    }

    consecutiveApiErrors = 0;  // Reset on success
    // ... process response ...
}
```

Tool errors are NOT counted toward the consecutive API error budget. Tool errors are normal (e.g., asking `get_mates` on a part document returns "Not an assembly"). They are sent back to the model as `tool_result` with `is_error: true`, and the model typically self-corrects.

### 8.3 Deterministic Fallback Integration

When the agent loop fails entirely (API unreachable, auth error, max consecutive errors), fall back to the existing single-turn path:

```csharp
if (agentResult.HasError && agentResult.ExecutedTools.Count == 0)
{
    // Complete fallback: run the single-turn deterministic path
    return HostState.CompleteAssistantRun(preparation);
}

if (agentResult.HasError && agentResult.ExecutedTools.Count > 0)
{
    // Partial fallback: use accumulated tool results with deterministic synthesis
    return BuildDeterministicAnswer(agentResult.ExecutedTools);
}
```

---

## 9. Implementation Checklist

### Phase 1 (Minimum Viable Agent Loop)

- [ ] `AgentLoopRunner` runs on a background thread via `Task.Run`.
- [ ] COM capture happens once on UI thread before `Task.Run`.
- [ ] No mid-loop COM refresh (deferred to Phase 2).
- [ ] `CancellationTokenSource` created per run, disposed in `FinishAssistantRunUi`.
- [ ] Cancel button toggles from "Run assistant" to "Cancel" during agent loop.
- [ ] `_agentCancellation.Cancel()` on cancel click.
- [ ] Token checked at 4 points: top of loop, after API call, before each tool, (no COM refresh point yet).
- [ ] Progress via `PostToUi` / `BeginInvoke` (fire-and-forget) to `_runStateLabel`.
- [ ] Final result via `PostToUi` / `BeginInvoke` to answer/plan/tools panels.
- [ ] All existing `RefreshStatus` guards respected (`if (_isRunning) return;`).
- [ ] Timer stopped during agent loop.
- [ ] Feature-gated behind `SOLIDWORKS_AI_AGENT_LOOP=true`.
- [ ] Deterministic fallback when API fails or is unconfigured.
- [ ] No `Task.Wait()` or `Task.Result` anywhere on the UI thread.
- [ ] No COM references in closures passed to background thread.
- [ ] No parallel tool execution.

### Phase 2 (Hardening)

- [ ] Mid-loop COM refresh via `Invoke` when tool needs fresh data.
- [ ] `InvokeOnUi<T>` helper method added to `TaskPaneControl`.
- [ ] Per-loop total timeout via `CancellationTokenSource(timeout)`.
- [ ] HTTP request abort on cancellation (Approach B from section 3.5).
- [ ] Throttled progress updates (section 5.2).
- [ ] Intermediate tool results displayed in Tools tab during loop.
- [ ] Consecutive error budget (section 8.2).

### Phase 3 (Streaming and Polish)

- [ ] SSE streaming for final answer generation.
- [ ] Partial answer text displayed as tokens arrive.
- [ ] Cross-click conversation persistence.
- [ ] Token budget visualization in the UI.

---

## 10. Reference: Thread-Safe Type Checklist

| Type | Thread-Safe? | Notes |
|---|---|---|
| `CancellationTokenSource` | Yes | `.Cancel()` and `.Token` are thread-safe. |
| `CancellationToken` | Yes | Struct, copied by value. Check from any thread. |
| `Control.BeginInvoke` | Yes | Can be called from any thread. Posts to UI message queue. |
| `Control.Invoke` | Yes | Can be called from any thread. Blocks caller until UI thread executes. |
| `HttpWebRequest` | Partially | A single request should not be shared across threads. `.Abort()` is thread-safe. |
| `SessionContext` | Yes (read) | Plain C# object. Safe to read from any thread. No COM references. |
| `ToolResult` | Yes (read) | Plain C# object. Safe to read from any thread. |
| `AgentConversationState` | No | Only accessed from the single agent loop background thread. No sharing needed. |
| `ISldWorks` (COM RCW) | No | STA-bound. Only access from UI thread. |
| `ModelDoc2` (COM RCW) | No | STA-bound. Only access from UI thread. |
| `volatile` fields | Partially | Guarantees visibility, not atomicity. Suitable for `_activeRequest` reference. |
| `List<T>` | No | Only accessed from a single thread in the agent loop. No sharing needed. |

---

## 11. Summary of Key Decisions

1. **COM stays on the UI thread, always.** The snapshot model (`SessionContext`) is the boundary. No COM references cross to the background thread.

2. **Single background thread for the agent loop.** No parallelism within the loop. Sequential API calls, sequential tool execution. Complexity is the enemy; the API call dominates wall-clock time.

3. **`BeginInvoke` for progress, `Invoke` for COM recapture.** Async for fire-and-forget, sync only when the background thread needs the result.

4. **`CancellationTokenSource` for cancellation.** Thread-safe, checked at 4 points in the loop. `HttpWebRequest` timeout provides a bounded worst-case cancellation latency.

5. **No mid-loop COM refresh in Phase 1.** Use the initially captured context. Add `Invoke`-based refresh in Phase 2 with evidence of need.

6. **Never block the UI thread on the background thread.** No `Wait()`, no `Result`, no `Join()`. This is the cardinal rule that prevents deadlocks.

7. **Feature-gated.** The agent loop is behind `SOLIDWORKS_AI_AGENT_LOOP=true`. The existing single-turn path is preserved as default and as fallback.

8. **Deterministic fallback preserved.** When the agent loop fails, the system degrades to the existing single-turn deterministic path, not to silence.
