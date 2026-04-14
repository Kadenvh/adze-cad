# Discovery Brief: Adze for SOLIDWORKS

**Date:** 2026-03-11
**Mode:** Hybrid
**Status:** Ready for Development

---

## Problem Statement
SOLIDWORKS users need an assistant that lives inside the desktop product, understands the active CAD session, and can safely execute real work. The gap is not "chat about CAD"; the gap is grounded interaction, safe automation, reusable learning, and a product experience that feels native instead of bolted on.

## Proposed Solution
Build a native SOLIDWORKS add-in that owns the in-CAD experience and the CAD execution boundary. The add-in should provide a persistent Task Pane assistant, guided PropertyManager pages, live model/context grounding, safe tool execution, trace capture, a governed learning system, and optional progression mechanics such as achievements, exploration percentage, and tool unlocks. Reasoning should sit outside the SOLIDWORKS COM boundary and call into a strict tool layer rather than directly improvising against the UI.

## Project Locations
- **Repository / build workspace:** `C:\SW_plugin`
- **SOLIDWORKS user data and configs:** `C:\SOLIDWORKS`
- **SOLIDWORKS settings export:** `C:\SOLIDWORKS\settings\swSettings.sldreg`
- **SOLIDWORKS shared data:** `C:\SOLIDWORKS\SOLIDWORKS Data`
- **Installed application root:** `C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x`
- **Primary host folder:** `C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS`
- **Installed interop assemblies confirmed locally:** `SolidWorks.Interop.sldworks.dll`, `SolidWorks.Interop.swconst.dll`, `SolidWorks.Interop.swdocumentmgr.dll`

Environment rules for implementation:

- Treat `C:\Program Files\Dassault Systemes\...` as a read-only dependency and integration target, not a working directory.
- Treat `C:\SOLIDWORKS\settings\swSettings.sldreg` as a backup/reference artifact; do not mutate it automatically.
- Treat `C:\SOLIDWORKS\SOLIDWORKS Data` as read-only during initial indexing and retrieval work.
- Keep all source, generated code, docs, traces, schemas, and build artifacts under `C:\SW_plugin`.

## Success Criteria
- The assistant launches as a native SOLIDWORKS add-in inside the local R2026x install.
- It can correctly explain the active document, selection, feature context, and relevant properties before any write tooling is enabled.
- It can complete a curated set of safe actions with explicit preview/approval and full trace logs.
- It can learn from approved traces by turning them into reusable recipes and governed automations.
- It exposes progression mechanics that improve trust and onboarding without weakening safety.
- PowerShell, registration, and installer permission behavior are validated early so script/installer failures do not derail host setup.
- It is ready to derive the permanent project docs and implementation plan from this discovery brief.

## Scope
### In Scope
- Desktop-first SOLIDWORKS assistant targeting the local `SOLIDWORKS 3DEXPERIENCE R2026x` environment first.
- Native add-in UX using Task Pane and PropertyManager pages.
- Read-only grounding on active part, assembly, and drawing sessions.
- Safe write tools for a narrow v1 action set.
- Full trace and approval logging.
- A governed learning system built from traces, corrections, and reviewed workflow recipes.
- User-facing progression features: achievements, exploration percentage, and staged tool unlocks.
- Read-only indexing of local SOLIDWORKS data and later optional prior-design retrieval.
- A deliberate experimental lane for breakthrough features after the core product is stable.

### Out of Scope
- Arbitrary desktop clicking as the default execution model.
- Production use of computer-use or generic UI automation as the main control path.
- Unsupervised self-modifying behavior where the assistant changes its own permissions, tools, or model routing.
- Broad generative CAD or B-Rep ML in the initial implementation wave.
- Automatic writes into PDM, vaults, or shared enterprise stores in v1.
- Direct edits under `C:\Program Files\Dassault Systemes\...`.

## Technical Approach
### Product Architecture
- **Native add-in host:** C# add-in running in-process with SOLIDWORKS.
- **Native UX surfaces:** Task Pane for persistent chat, status, plans, progress, and achievements; PropertyManager pages for guided selection, preview, and confirmation.
- **Tool executor:** Typed, allowlisted operations that own all SOLIDWORKS COM calls.
- **Broker/service layer:** AI orchestration, retrieval, policy, trace storage, evaluation, and recipe management.
- **Knowledge/indexing layer:** Document Manager-backed and file-backed read-only indexing for prior designs, references, standards, and reusable context.
- **Learning/progression layer:** Trace review, recipe synthesis, unlock engine, achievement engine, and exploration metrics.

### Agent Roles
These are logical agents/workers inside the product and also the best parallel implementation workstreams:

- **Conductor Agent:** Builds the plan, chooses tools, requests approvals, and explains intent.
- **CAD Executor Agent:** Calls only typed SOLIDWORKS tools from inside the add-in.
- **Retrieval Agent:** Gathers local docs, prior designs, standards, and prior recipes.
- **Learning Agent:** Converts approved traces into recipe candidates, recommendations, and progression updates.
- **Safety Agent:** Validates tool inputs, approval level, rollback hints, and policy compliance.
- **Validation Agent:** Runs eval suites, replay tests, and regression scenarios.

### Data and Config Boundaries
- Live CAD state comes from the active SOLIDWORKS session.
- Shared data and standards come from `C:\SOLIDWORKS\SOLIDWORKS Data` and later approved sources.
- Settings and machine-specific preferences can be read from `C:\SOLIDWORKS\settings\swSettings.sldreg` for diagnostics or compatibility support, but should not be rewritten by the assistant automatically.
- Install-time integration depends on the locally installed interop assemblies under the R2026x SOLIDWORKS folder.

## End-to-End Implementation Process
### Pre-Phase: Environment and Permissions Validation
**Goal:** Eliminate script-launch and registration surprises before the add-in host is built.

Current local findings:

- PowerShell execution policy is `RemoteSigned` at `LocalMachine`.
- `MachinePolicy`, `UserPolicy`, `Process`, and `CurrentUser` are currently undefined.
- The current shell is running at medium integrity, not elevated.
- The current user is associated with the Administrators group, but that group is marked `deny only` in the present session, which means elevation is not active right now.

Interpretation:

- Local `.ps1` files should generally run under the current policy.
- A script window that opens and closes immediately is more likely to be an invocation/hosting problem than a base execution-policy block.
- Machine-wide registration, installer writes, or other protected operations may still require elevation because the current session is not elevated.

Implementation procedure:

1. Create a repeatable PowerShell diagnostics checklist for the machine.
2. Validate all script launch modes that the project may rely on:
   - direct terminal invocation
   - `pwsh -File`
   - `powershell.exe -File`
   - installer-triggered execution
   - any double-click or detached-window launch patterns, if they are part of the workflow
3. Test script behavior with explicit logging, pause handling, and exit-code capture so "opens then closes" can be distinguished from permission failure.
4. Standardize the preferred invocation pattern for project scripts:
   - `pwsh -NoProfile -File <script.ps1>`
   - capture stdout/stderr to a log when running unattended
   - reserve detached-window launches for scripts that explicitly manage their own logging and wait behavior
5. Decide which actions are user-scope versus machine-scope:
   - add-in registration
   - installer writes
   - local broker installation
   - trace/log directories
6. Prefer user-scope registration and execution during development unless a machine-scope requirement is unavoidable.
7. Define the escalation path for machine-scope tasks and document when elevation is actually required.

Deliverables:

- Permissions and execution-policy checklist.
- Script-launch troubleshooting checklist.
- User-scope versus machine-scope registration/install decision.
- Early installer and registration test matrix.

Exit criteria:

- The team can explain why a script succeeded or failed instead of treating PowerShell behavior as opaque.
- The host-spike phase has a documented launch path that does not depend on guesswork.
- Machine-scope tasks are clearly separated from standard development-time commands.

### Phase 0: Environment Freeze and Architecture Lock
**Goal:** Remove ambiguity before any code is written.

Implementation procedure:

1. Freeze the initial support target to the local `SOLIDWORKS 3DEXPERIENCE R2026x` desktop environment.
2. Decide the add-in host baseline:
   - preferred: native C# add-in with the simplest stable registration path for this machine.
   - optional accelerator: xCAD, if the initial spike proves it reduces setup cost without complicating debugging.
3. Define the repository layout under `C:\SW_plugin` for host, broker, schemas, docs, traces, samples, and installers.
4. Define read-only versus writable boundaries for `C:\SOLIDWORKS` and the installed application tree.
5. Capture the initial risk register and acceptance gates.
6. Finalize the permissions model for development, local testing, and installer execution.

Exit criteria:

- One supported SOLIDWORKS target.
- One approved host approach.
- One repo layout.
- One explicit safety model.
- One explicit permission/elevation model.
- One initial evaluation dataset list.

### Phase 1: Developer Foundation and Host Spike
**Goal:** Prove that the add-in can load reliably in the local install.

Implementation procedure:

1. Create the add-in solution and registration path.
2. Reference the installed interop assemblies from the local SOLIDWORKS install.
3. Implement add-in connect/disconnect lifecycle and structured logging.
4. Add one command and one Task Pane surface.
5. Verify load, unload, and reload behavior across SOLIDWORKS restarts.
6. Capture packaging notes and all local prerequisites.

Deliverables:

- Loadable add-in shell.
- Registration/install notes.
- First structured logs.
- First environment verification checklist.

Exit criteria:

- Add-in loads consistently on this machine.
- Task Pane renders.
- Basic diagnostics are visible.

### Phase 2: Grounding Layer
**Goal:** Make the assistant trustworthy before it can act.

Implementation procedure:

1. Build live context collectors for active document, document type, units, configuration, dirty state, and selection.
2. Add feature-tree slice, custom property, dimension, mate, and warning collectors.
3. Create a compact context schema for model prompts and tool planning.
4. Build replayable grounding benchmarks using local files and agreed sample models.
5. Surface the context in the Task Pane for transparent inspection.

Deliverables:

- Grounding schema.
- Read-only inspection tools.
- Benchmark harness.
- First "why I think this" view in the UI.

Exit criteria:

- The assistant explains the current session accurately enough to pass the initial benchmark.
- No write tools are enabled until this gate is met.

### Phase 3: Safe Action Layer
**Goal:** Introduce a narrow set of useful, reversible actions.

Implementation procedure:

1. Implement typed tool contracts and validation.
2. Add plan, preview, apply, verify, and log states.
3. Start with the first action set:
   - highlight/select entities
   - set dimension value
   - suppress/unsuppress feature
   - rename feature or component
   - set custom property
   - export approved outputs
   - run approved macro wrappers
4. Add rollback guidance for each action where possible.
5. Record every action as a structured trace.

Deliverables:

- First write-capable tools.
- Approval UI.
- Action logs and trace records.
- Regression task suite for safe actions.

Exit criteria:

- No unapproved writes.
- High task-completion rate on the safe-action benchmark.
- Clear human-readable audit trail for every action.

### Phase 4: Fully Interactive Native UX
**Goal:** Make the assistant feel built into SOLIDWORKS.

Implementation procedure:

1. Upgrade the Task Pane into the primary assistant workspace:
   - chat
   - plan/status
   - tool previews
   - history
   - achievements/progress
2. Use PropertyManager pages for all selection-heavy or high-risk workflows.
3. Add interactive affordances:
   - entity highlighting
   - explicit ambiguity resolution
   - step playback
   - explain/undo guidance
4. Add context-aware suggestions tied to the active model and workflow.

Deliverables:

- Persistent assistant panel.
- Guided confirmation flows.
- Interactive preview patterns.
- Live explanation of current plan and confidence.

Exit criteria:

- Users can stay inside SOLIDWORKS for the full interaction loop.
- The assistant no longer feels like a detached chat overlay.

### Phase 5: LearningAgent, Recipes, and Progression
**Goal:** Turn usage into capability without compromising safety.

Implementation procedure:

1. Capture structured traces:
   - user intent
   - live context
   - tool sequence
   - approvals
   - corrections
   - result state
2. Build the `LearningAgent` pipeline:
   - trace ingestion
   - candidate pattern detection
   - recipe proposal generation
   - human review queue
   - recipe publication
3. Add progression mechanics:
   - achievements
   - exploration percentage
   - tool unlock tiers
   - streaks or milestones only if tied to quality, not volume
4. Personalize recommendations from validated history, not raw model memory.

Deliverables:

- Trace schema.
- Recipe system.
- Unlock engine.
- Achievement catalog.
- Exploration metric definitions.

Exit criteria:

- Successful traces can be promoted into reusable workflows.
- Unlocks are policy-driven and reviewable.
- Progression increases trust and onboarding quality instead of encouraging unsafe behavior.

### Phase 6: Headless Knowledge and Prior-Design Retrieval
**Goal:** Extend beyond the active file without losing grounding.

Implementation procedure:

1. Add read-only indexing for approved local data sources.
2. Start with `C:\SOLIDWORKS\SOLIDWORKS Data` and approved sample/design folders.
3. Add metadata extraction, reference graphing, and similar-design retrieval.
4. Keep this lane read-only until governance is mature.
5. Connect retrieval results back into the assistant as cited context, not hidden model state.

Deliverables:

- Indexer.
- Search and retrieval layer.
- Reference graph view.
- Related-design suggestions.

Exit criteria:

- The assistant can answer cross-document questions with explicit provenance.
- Retrieval does not weaken live-session grounding.

### Phase 7: Guided Authoring and Automation
**Goal:** Move from isolated tools to structured workflows.

Implementation procedure:

1. Create guided multi-step flows for drawing generation, exports, template-driven operations, and approved macro-backed workflows.
2. Add recipe playback with checkpoints and mid-run confirmation.
3. Allow users to save or share approved recipes once they pass validation.
4. Build team-safe automation packs later, after single-user flows are stable.

Deliverables:

- Guided workflows.
- Recipe playback UI.
- Team-sharable automation format.

Exit criteria:

- Multi-step automation works without turning into black-box autonomy.

### Phase 8: Experimental and Groundbreaking Development Lanes
**Goal:** Reserve ambitious ideas for a separate, controlled track.

Planned R&D lanes:

- Semantic selection understanding and richer model-neighborhood reasoning.
- Advanced recipe generation from repeated expert behavior.
- Geometry-aware embeddings and design-memory retrieval.
- Intelligent exploration maps of the SOLIDWORKS capability surface.
- Experimental overlay or viewport augmentation if it materially improves interaction.
- Limited lab-only UI automation or computer-use fallback for unsupported dialogs.

Rules for this phase:

- These lanes do not block the core product.
- Each lane needs its own success metric.
- Nothing graduates into production without explicit validation and safety review.

### Phase 9: Hardening, Packaging, and Beta Readiness
**Goal:** Make the system installable, supportable, and testable.

Implementation procedure:

1. Package the add-in and dependencies.
2. Add upgrade, rollback, and diagnostic collection procedures.
3. Finalize configuration management, logs, traces, and privacy defaults.
4. Create regression suites for grounding, actions, recipes, and retrieval.
5. Define beta feedback loops and failure triage.

Deliverables:

- Installer/update path.
- Diagnostics guide.
- Beta checklist.
- Release acceptance suite.

Exit criteria:

- Clean install and upgrade path.
- Stable error reporting.
- Repeatable evaluation before each release.

## Parallel Workstreams
To move quickly without losing quality, implementation should be split into parallel tracks:

- **Track A: Host/UI**
  Build add-in shell, Task Pane, PropertyManager pages, logging, and install/load reliability.
- **Track B: Tooling/Schemas**
  Define typed tool contracts, entity resolution rules, approvals, and rollback metadata.
- **Track C: Broker/AI**
  Build orchestration, prompt/context packaging, tool loop, retries, and policy handling.
- **Track D: Learning/Progression**
  Build trace capture, recipe synthesis, achievements, exploration percentage, and unlock engine.
- **Track E: Retrieval/Evals**
  Build indexing, search, benchmark datasets, regression replay, and acceptance scoring.
- **Track F: Packaging/Ops**
  Build installer, diagnostics, config management, and local deployment procedure.

## Tool Roadmap
### Wave 1: Grounding Tools
- get_active_document
- get_document_summary
- get_selection_context
- get_feature_tree_slice
- get_dimensions
- get_custom_properties
- get_configurations
- get_mates
- get_rebuild_diagnostics
- get_reference_graph

### Wave 2: Safe Action Tools
- select_or_highlight_entities
- set_dimension_value
- suppress_feature
- unsuppress_feature
- rename_object
- set_custom_property
- export_document
- run_approved_macro

### Wave 3: Guided Workflow Tools
- create_drawing_from_template
- insert_drawing_views
- run_recipe
- save_recipe_candidate
- compare_to_prior_design

## Learning, Achievements, and Exploration System
### LearningAgent Principles
- Learn from structured traces, not silent model drift.
- Promote only reviewed, high-confidence behavior.
- Keep all unlocks visible, explicit, and reversible.
- Separate user progression from model capability maturity.

### Achievement Categories
- **Orientation:** open first document, explain selection, identify feature context.
- **Builder:** complete first approved edit, first export, first validated macro wrapper.
- **Designer:** complete a guided drawing or modeling workflow with no rollback.
- **Automator:** publish first approved recipe, replay it successfully, share it safely.
- **Trust:** maintain streaks of verified safe actions, pass evaluation milestones, resolve ambiguity correctly.

### Exploration Percentage
Exploration should measure meaningful product depth, not activity padding. Recommended composition:

- **30% API/tool exploration:** how much of the supported tool surface has been discovered and exercised successfully.
- **25% workflow exploration:** how many supported end-to-end workflows have been completed.
- **20% context exploration:** document types, configurations, selections, and edge cases encountered.
- **15% knowledge exploration:** retrieval sources, recipes, and prior designs surfaced and used.
- **10% reliability exploration:** benchmark and validation milestones achieved.

### Tool Unlock Tiers
- **Tier 0:** inspect only.
- **Tier 1:** reversible single-document edits.
- **Tier 2:** guided multi-step workflows.
- **Tier 3:** recipe creation and replay.
- **Tier 4:** cross-document retrieval and team-shared automations.
- **Tier 5:** experimental lab features only.

Unlock inputs:

- user role
- admin policy
- proven reliability
- completion of prerequisite achievements
- passing eval thresholds

## Key Decisions Made
These are the recommended defaults that the initial project bootstrap should encode:

- **Primary target:** start with the locally installed `SOLIDWORKS 3DEXPERIENCE R2026x` environment.
- **Product shell:** native C# in-process add-in.
- **Execution rule:** all SOLIDWORKS COM calls stay inside the add-in tool executor.
- **AI control model:** reasoning outside the COM boundary through strict typed tools.
- **UX rule:** Task Pane plus PropertyManager pages, not detached overlay-first control.
- **Safety rule:** `plan -> preview -> apply -> verify -> log` for every write action.
- **Learning rule:** traces become reviewed recipes; the assistant does not self-expand permissions.
- **Progression rule:** achievements and unlocks are tied to trust, onboarding, and validated capability.
- **Delivery rule:** grounding first, safe actions second, recipes third, retrieval fourth, experimental work last.

## Open Questions
- Which add-in host baseline is best on this machine: minimal raw add-in template or xCAD-accelerated scaffolding?
- Should the broker run as a local desktop companion, Windows service, or in-process helper for v1?
- Which local project folders under `C:\SOLIDWORKS` should be approved as initial benchmark and retrieval corpora?
- How visible should achievements be: quiet progress system, explicit gamification, or team dashboard?
- Should exploration percentage be user-specific, machine-specific, workspace-specific, or some combination?
- What exact privacy, retention, and redaction rules should apply to traces and retrieved design context?
- Should add-in registration and developer tooling stay user-scope in v1, with machine-scope installation reserved for packaging/beta?

## Risks & Unknowns
- COM/threading errors can destabilize the host if boundaries are sloppy.
- A modern AI stack can create latency unless the broker is isolated from the SOLIDWORKS UI thread.
- Recipe promotion can become unsafe if evaluation and review are weak.
- Gamification can become noise if it rewards volume rather than verified outcomes.
- Retrieval can dilute trust if provenance is not surfaced clearly.
- Experimental interactive features can consume time unless kept in a separate lane with hard gates.
- Permission confusion can waste time unless script launch behavior, execution policy, and elevation requirements are made explicit up front.

## Recommended Next Step
Use this document as the authoritative input for the permanent project handbook and implementation plans, with the early implementation focused on:

1. repo structure and ADRs
2. add-in host spike
3. grounding schema
4. first safe tool catalog
5. trace/recipe schema
6. evaluation harness
7. learning/progression system definitions

## Bootstrap Notes
What the initial project bootstrap needs to preserve:

- The local machine paths and environment assumptions.
- The phase order and exit criteria.
- The distinction between live CAD execution and external reasoning.
- The LearningAgent / achievements / exploration / unlock system.
- The split between core product work and experimental groundbreaking lanes.
- The rule that speed comes from parallel workstreams and strict gates, not from skipping validation.
