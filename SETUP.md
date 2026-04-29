# Adze Setup

## Simplest path to first run

Not a developer? This is the fast lane.

1. Download the `.zip` from [Releases](https://github.com/Kadenvh/adze-cad/releases).
2. Extract it anywhere.
3. Double-click **`install-adze.bat`**. The **Adze Manager** opens — a small Windows Forms window with Install / Uninstall / Eject for Update / Refresh buttons. Click **Install / Reinstall**; the log panel streams progress and completes in a few seconds.
4. Launch SOLIDWORKS.
5. The **Adze** panel appears in the right sidebar. Type any question about your active document and click **Run assistant**.

That's it. Adze runs locally with a built-in deterministic broker that reads the document via its 11 read tools. AI provider keys (OpenAI, Anthropic, Ollama, LM Studio) are optional — configure them from the in-app Settings panel when you're ready.

To uninstall: double-click **`uninstall-adze.bat`** (opens the Manager pre-armed to uninstall), or use the Manager's **Uninstall** button directly.

**Before a SOLIDWORKS or 3DEXPERIENCE update:** open the Manager and click **Eject for Update**. Adze unregisters itself and clears its persisted compatibility state so the updater runs cleanly and Adze's next launch re-verifies against the updated SW binaries.

---

The rest of this guide is for someone who has the Adze source code and wants to build, register, run, validate, and troubleshoot the add-in locally.

## Prerequisites

- SOLIDWORKS 2025 or 2026 desktop edition. Do not use a cloud-only 3DEXPERIENCE client without the desktop SOLIDWORKS host.
- Windows 10 or Windows 11.
- .NET Framework 4.8. Windows 11 includes it; Windows 10 may need the .NET Framework 4.8 Developer Pack.
- Visual Studio 2022 or newer, or MSBuild from Visual Studio Build Tools.
- PowerShell 7 or newer (`pwsh`) for build and schema-validation scripts.
- Windows PowerShell 5.1 (`powershell.exe`) for host validation, reload, and benchmark scripts.

## Beta Install (No Source Code Required)

If you received a packaged release zip (`adze-v*.zip`):

1. Extract the zip to any folder.
2. Double-click `install-adze.bat` → the **Adze Manager** opens → click **Install / Reinstall**.

   Or, for headless install (CI, scripting, no GUI):

   ```powershell
   powershell.exe -NoProfile -File install-adze.ps1
   ```

   Both paths copy binaries to `%LOCALAPPDATA%\Adze\bin\`, register the add-in per user (no admin required), and run pre-flight checks.

3. Launch SOLIDWORKS. The `Adze for SOLIDWORKS` tab should appear in the right sidebar.

To update, extract the new zip over the old one and either click **Install / Reinstall** in the Manager, or rerun `install-adze.ps1`.

To uninstall:

- GUI: double-click `uninstall-adze.bat` → Manager opens → click **Uninstall**.
- Headless: `powershell.exe -NoProfile -File uninstall-adze.ps1` (add `-RemoveUserData` to also remove traces, logs, and progression state).

To prepare for a SOLIDWORKS or 3DEXPERIENCE update, use the Manager's **Eject for Update** button — it runs the uninstaller and clears Adze's persisted compatibility state so the next launch re-verifies against whatever binaries the updater leaves behind.

## Developer Build and Install

Build the full solution from source:

```powershell
pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks
```

Register the add-in per user (no admin rights required). The installer auto-detects the repo and registers from the Debug build output:

```powershell
powershell.exe -NoProfile -File install\install-adze.ps1
```

Alternatively, for a lighter dev-only registration that skips the DLL copy step:

```powershell
powershell.exe -NoProfile -File scripts\setup\register-host-addin.ps1
```

Launch SOLIDWORKS. The `Adze for SOLIDWORKS` tab should appear in the right sidebar.

To package a release zip for distribution:

```powershell
pwsh -NoProfile -File install\package-release.ps1
```

If a `Login | 3DEXPERIENCE ID | Dassault Systèmes` window or a `3DEXPERIENCE Update` window appears, complete it first. Those launcher-managed windows can block SOLIDWORKS from loading far enough to start the add-in.

## AI Model Configuration

The model path is optional.

Without model configuration, Adze still works. It uses the local deterministic broker and the host-side grounded answer builder.

With model configuration, Adze can use either OpenAI Chat Completions or Anthropic Messages for tool planning and answer synthesis.

### Provider Selection

- `SOLIDWORKS_AI_ENABLE_MODEL=true` enables the model path.
- `SOLIDWORKS_AI_PROVIDER=openai` forces OpenAI.
- `SOLIDWORKS_AI_PROVIDER=anthropic` forces Anthropic.
- If `SOLIDWORKS_AI_PROVIDER` is not set:
  Adze defaults to `openai` when only an OpenAI key is present.
  Adze defaults to `anthropic` when an Anthropic key is present.
  With no usable key, Adze falls back to the deterministic broker.

### Provider-Specific Variables

| Variable | Required | Purpose | Example |
|---|---|---|---|
| `SOLIDWORKS_AI_OPENAI_API_KEY` or `OPENAI_API_KEY` | Yes for OpenAI | OpenAI API key | `sk-proj-...` |
| `SOLIDWORKS_AI_OPENAI_MODEL` | No | OpenAI model ID | `gpt-4o` |
| `SOLIDWORKS_AI_OPENAI_ENDPOINT` | No | OpenAI or compatible Chat Completions endpoint | `https://api.openai.com/v1/chat/completions` |
| `SOLIDWORKS_AI_ANTHROPIC_API_KEY` or `ANTHROPIC_API_KEY` | Yes for Anthropic | Anthropic API key | `sk-ant-api03-...` |
| `SOLIDWORKS_AI_ANTHROPIC_MODEL` | No | Anthropic model ID | `claude-sonnet-4-20250514` |
| `SOLIDWORKS_AI_ANTHROPIC_ENDPOINT` | No | Anthropic Messages endpoint | `https://api.anthropic.com/v1/messages` |
| `SOLIDWORKS_AI_ANTHROPIC_VERSION` | No | Anthropic API version header | `2023-06-01` |

### Shared Broker and Synthesis Variables

| Variable | Required | Purpose | Example |
|---|---|---|---|
| `SOLIDWORKS_AI_MAX_TOKENS` | No | Broker max tokens | `700` |
| `SOLIDWORKS_AI_SYNTHESIS_MAX_TOKENS` | No | Synthesis max tokens | `1100` |
| `SOLIDWORKS_AI_TIMEOUT_MS` | No | Broker planning timeout | `20000` |
| `SOLIDWORKS_AI_SYNTHESIS_TIMEOUT_MS` | No | Answer synthesis timeout | `30000` |
| `SOLIDWORKS_AI_TEMPERATURE` | No | Sampling temperature | `0.1` |

Backward compatibility:
- The older `SOLIDWORKS_AI_ANTHROPIC_MAX_TOKENS`
- `SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_MAX_TOKENS`
- `SOLIDWORKS_AI_ANTHROPIC_TIMEOUT_MS`
- `SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_TIMEOUT_MS`
- `SOLIDWORKS_AI_ANTHROPIC_TEMPERATURE`

Those older names still work as fallback aliases.

Note: Adze uses provider API billing. Consumer chat subscriptions do not provide usage for this application.

## Feature Gates

All gates are environment variables. The value `true`, `1`, `yes`, or `on` enables the feature; any other value (or absence) leaves it disabled. Default for every gate is off.

| Variable | Enables |
|---|---|
| `SOLIDWORKS_AI_ENABLE_MODEL` | AI-powered answers (required for anything beyond the deterministic fallback) |
| `SOLIDWORKS_AI_AGENT_LOOP` | Iterative agentic tool-calling loop |
| `SOLIDWORKS_AI_FIRST_WAVE_WRITES` | Write tool definitions in the agent loop (requires `AGENT_LOOP`) |
| `SOLIDWORKS_AI_RETRIEVAL` | Closed-file search via `search_project_files` tool |
| `SOLIDWORKS_AI_LOCAL_MODELS` | Ollama / LM Studio providers |
| `SOLIDWORKS_AI_STREAM_FINAL_TEXT` | SSE streaming for the final answer synthesis pass |
| `SOLIDWORKS_AI_RIBBON` | "Adze" CommandManager ribbon tab with six quick-action buttons |
| `SOLIDWORKS_AI_CONTEXT_MENU` | Right-click context menu items on features, components, and empty canvas |
| `SOLIDWORKS_AI_TOAST` | Tray balloon notifications on run completion (suppressed when SOLIDWORKS is foreground) |
| `SOLIDWORKS_AI_PMP_WRITES` | PropertyManager Page confirmations for write operations (v0.1.2: `set_dimension_value` only) |
| `SOLIDWORKS_AI_NATIVE_SIDEBAR` | v1.1 native WinForms sidebar in place of the legacy WebBrowser-based Task Pane (see below) |

Gates fail safely: turning a gate on never breaks the add-in if the feature cannot initialize (the Task Pane stays fully functional either way).

Resolution order for any gate: **environment variable** → **`config.json`** at `%LOCALAPPDATA%\Adze\config.json` → **baked-in default**. The Adze Manager Settings tab writes to `config.json` so you never need to edit it by hand.

## Switching to the v1.1 native sidebar

Adze v1.1 introduces a native WinForms sidebar that replaces the legacy IE11-based WebBrowser Task Pane. The new sidebar is OFF by default during the v1.1 rollout — both surfaces are registered alongside each other, and the legacy path is bit-for-bit unchanged when the gate is off.

**Recommended path — via Adze Manager (no terminal):**

1. Open `Adze.Manager.exe`
2. Click **Verify Setup** — runs an 8-point install + COM + env-var checklist. If the gate is off, the dialog offers a one-click enable.
3. Click **Yes** when prompted to enable the gate. Manager writes `SOLIDWORKS_AI_NATIVE_SIDEBAR=true` to your User-scope environment.
4. **Fully close SOLIDWORKS** AND the **3DEXPERIENCE Launcher** in Task Manager. Environment variables do not propagate to already-running processes — the new gate value will only take effect when SW launches from a fresh process tree.
5. Relaunch SOLIDWORKS from 3DX. The new sidebar appears in place of the legacy one.

**Alternative — via PowerShell:**

```powershell
setx SOLIDWORKS_AI_NATIVE_SIDEBAR true
# Fully close SLDWORKS.exe and the 3DEXPERIENCE Launcher in Task Manager.
# Re-open SW from 3DX.
```

**Verifying which sidebar loaded:**

Tail `%LOCALAPPDATA%\Adze\logs\host.log`. Look for the most recent entry of the form:

```
Task Pane mounting sidebar=v1.1-native ...
```

If you see `sidebar=legacy`, the gate is still effectively off — most commonly because SW or the 3DX Launcher were already running when you set the env var. Kill them entirely and relaunch.

**Reverting:** unset the variable (`setx SOLIDWORKS_AI_NATIVE_SIDEBAR ""`) or click **Verify Setup** in Manager and decline the prompt next time. The legacy sidebar resumes on next SW launch with no other changes required.

## Data and Privacy

- All SOLIDWORKS data is read locally through in-process COM.
- Nothing leaves the machine unless the model path is enabled.
- When the model path is enabled, session context and tool results are sent to the configured provider API for planning and answer synthesis.
- Traces, logs, snapshots, support bundles, and progression state are stored locally under `%LOCALAPPDATA%\Adze\`.
- Snapshot and trace files can include document paths, machine name, selection metadata, reference paths, custom properties, and tool-result payloads captured from the active SOLIDWORKS session.
- `logs\host.log` stores operational metadata for local troubleshooting. It does not store full answer text or plan text, but local trace/snapshot files can still contain detailed session context.
- The repo does not include telemetry, cloud storage, or external data collection.

## Validation Commands

Validate JSON schemas:

```powershell
pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1
```

Run end-to-end host validation:

```powershell
powershell.exe -NoProfile -File scripts\setup\validate-host-spike.ps1
```

Run grounding benchmarks:

```powershell
powershell.exe -NoProfile -File scripts\setup\run-grounding-benchmarks.ps1
```

Run broker tool-selection evals:

```powershell
powershell.exe -NoProfile -File scripts\setup\run-broker-evals.ps1
```

Collect a support bundle:

```powershell
pwsh -NoProfile -File scripts\setup\collect-support-bundle.ps1
```

## Uninstall

Unregister the add-in:

```powershell
powershell.exe -NoProfile -File scripts\setup\unregister-host-addin.ps1
```

## Troubleshooting

- Black Task Pane: rebuild, re-register, and relaunch SOLIDWORKS. Check `%LOCALAPPDATA%\Adze\logs\host.log` for Task Pane creation or control-initialization errors.
- SOLIDWORKS does not load the add-in: clear any login or `3DEXPERIENCE Update` windows first, then relaunch.
- Wrong model provider is being used: set `SOLIDWORKS_AI_PROVIDER` explicitly to `openai` or `anthropic` and relaunch SOLIDWORKS.
- PowerShell scripts open and close immediately: run them from an existing `pwsh` or `powershell.exe` terminal instead of double-clicking the script.
- `No active document` or similar grounded-session errors: open a part, assembly, or drawing first, then click `Run assistant`.
