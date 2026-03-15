# Adze - First Usable Build

**Version:** 0.1.0  
**Last Updated:** 2026-03-15  
**Purpose:** Define the shortest path from the current grounded alpha to the first build that feels like the intended assistant product

## Definition

The first usable build is not a production release. It is the first build where a normal user can:

- open SOLIDWORKS through the supported path
- ask a real engineering question in the Task Pane
- receive a grounded answer that feels deliberate instead of debug-oriented
- understand blocked/no-document states without reading logs
- collect support artifacts without manual spelunking

## What Already Exists

- native SOLIDWORKS add-in host
- answer-first Task Pane UI
- 10 live read-only grounding tools
- hybrid broker with deterministic fallback
- provider-routed model-backed final answer synthesis with deterministic fallback
- traces, recipes, achievements, exploration, and unlock tiers
- green scripted validation on the curated corpus
- 130 compiled NUnit unit tests covering broker, tools, and trace layers
- assistant-first Task Pane workspace with `Plan`, `Status`, and `Tools` tabs
- one-command support bundle collection

## What Still Prevents "First Usable Build"

- answer-quality eval coverage is still thin compared to tool-selection coverage
- launcher/login/update interruption handling still leaks operational complexity to the user
- install/update packaging is still a developer workflow
- the Task Pane still needs a bit more assistant-product polish

## Track 1 - Grounded Answer Quality

**Status:** Partially complete

### Already Done

- provider-routed model-backed planning exists
- provider-routed model-backed final answer synthesis exists
- deterministic fallback exists for both planning and final answer rendering
- provider-selection checks already prove the OpenAI/Anthropic routing branch works as designed

### Remaining

- add answer-quality eval tasks for synthesis output
- add explicit synthesis timeout/failure coverage
- run a live external-provider smoke test with a real API key
- decide whether answers should expose evidence snippets or citations from tool results
- tighten any prompt behavior that feels too generic or too verbose in live use

### Exit Criteria

- grounded answer evals exist and are stable
- fallback behavior is proven under failure, timeout, and disabled-model conditions
- answer phrasing stays grounded in the executed tool results

## Track 2 - Assistant Workspace UX

**Status:** Partially complete

### Already Done

- deliberate `Run assistant` flow
- answer-first layout
- `Plan`, `Status`, and `Tools` separated from the primary answer surface
- blocked/no-document messaging improved
- explicit run-state messaging added
- active-tab status refresh with scroll preservation added
- provider network work moved off the UI thread after host-thread context capture

### Remaining

- improve empty-state copy further
- decide how much evidence to show inline vs in the plan tab
- reserve cleaner space for later write confirmations and recipe suggestions
- continue smoothing the interaction so it feels less like a developer panel
- capture a direct desktop acceptance pass for rendering, resize behavior, and status-tab scroll preservation

### Exit Criteria

- the assistant answer is clearly the primary interaction surface
- a new user can understand what to do next in blocked/no-document/error states
- normal use feels like `ask -> run -> answer`, not `open logs -> infer state`

## Track 3 - Session, Install, And Beta Path

**Status:** Partially complete

### Already Done

- user-scope registration works
- current machine no longer shows the previously tracked HKLM residue for the active add-in GUID
- launcher preflight exists
- support bundle generation exists

### Remaining

- harden launcher interruption handling
- create install/update assets under `install/`
- define the clean beta setup path
- expand support-bundle coverage only as real beta cases demand it

### Exit Criteria

- beta users can install/update/uninstall without ad hoc registry surgery
- launcher blockers are detected and explained before the user hits a dead end
- support artifacts are simple to collect and useful to inspect

## Explicitly Out Of Scope Before First Usable Build

- production-grade safe write tools
- broad retrieval/indexing workflows
- experimental UI automation or computer-use paths
- full production release process and support burden

## Release Gate

The first usable build requires all of the following to be good enough at the same time:

- the grounded answer loop feels real and trustworthy
- the Task Pane feels like an assistant workspace
- launcher/install/support workflows are manageable without developer intervention

Minimum validation gate:

1. `validate-json-schemas.ps1`
2. `build-all.ps1 -StopSolidWorks`
3. `run-tests.ps1` (130 compiled unit tests)
4. `validate-host-spike.ps1`
5. `run-grounding-benchmarks.ps1`
6. `run-broker-evals.ps1`
7. synthesis-specific evals once added
8. support-bundle smoke test
9. direct desktop Task Pane acceptance pass

## Recommended Execution Order From Here

1. Add answer-quality and failure-mode evals for the synthesis path.
2. Harden launcher/login/update interruption handling.
3. Add install/update assets under `install/`.
4. Continue Task Pane polish around evidence display and future confirmations.
