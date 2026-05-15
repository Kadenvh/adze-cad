# Adze.Manager

Standalone WinForms control panel (`Adze.Manager.exe`). Post-install GUI for managing the Adze SOLIDWORKS add-in — log viewing, settings, install state, SW launch helper.

**Type:** WinExe (.NET Framework 4.8). AssemblyVersion 1.1.0.0. Ships in the release zip.

## Why it exists

`install-adze.bat` registers the add-in. After that, end users (and the dev) need a way to:

- See what's happening in `%LOCALAPPDATA%\Adze\logs\host.log` without tailing a file in PowerShell
- Toggle feature gates without `setx SOLIDWORKS_AI_* true`
- Set provider + API key without manual env vars or registry edits
- See install state at a glance (DLL version, register state, last-verified SW build, compatibility probe outcome)
- Launch SOLIDWORKS with a single button (and detect the 3DEXPERIENCE updater state)

The Manager is that GUI. It uses `Adze.Broker`'s config / gate / settings APIs — it does NOT talk to SW COM directly.

## Layout

```
src/Adze.Manager/
├── Program.cs            # WinForms entry, DPI awareness, MainForm bootstrap
├── MainForm.cs           # 4-tab TabControl + action row (Install / Uninstall / Eject / Launch SW)
├── LogsTab.cs            # host.log live tail, chat JSONL browser, install transcripts, telemetry summary
├── SettingsTab.cs        # Provider dropdown, masked API key, feature-gate toggles, theme
├── AgentProfileTab.cs    # Placeholder for v1.1+ agent-knob UI
├── StatusTab.cs          # Install state, SW process, last-verified build, probe result
└── Properties/AssemblyInfo.cs
```

## What's in each tab

### Logs
- Live tail of `%LOCALAPPDATA%\Adze\logs\host.log` with `FileSystemWatcher` follow mode
- Prefix-colored output (ERROR / WARN / INFO)
- Chat JSONL session browser (per-session conversation history)
- Script log inspector (most-recent `install-*.log` / `uninstall-*.log` transcripts)
- Export button (copies relevant logs to a zip for triage)

### Settings
- Provider dropdown (OpenAI / Anthropic / OpenRouter / Ollama / LM Studio)
- Masked API key input + Save / Clear (encrypted via DPAPI in `%LOCALAPPDATA%\Adze\keys.dat`)
- Model / endpoint / tuning slider overrides
- Feature gate toggles (Ribbon, Context Menu, Toast, PMP writes, Native Sidebar, Mesh Import, etc.)
- Light / Dark / System appearance picker

### Agent Profile
- Placeholder. v1.1+ scope: verbosity slider, system instructions textarea, recipe promotion rules.

### Status
- Install state (registered Y/N, version, install path)
- SW process state (running Y/N, PID)
- 3DEXPERIENCE updater state (running → orange "ejection" prompt)
- Last-verified SW build (from `SwBuildStateService`)
- Most recent compatibility probe outcome (Compatible / GracefulRefusal / HardIncompatible)
- API key presence (without revealing the key)
- "Verify Setup" button — runs an 8-point install/COM/env-var checklist

## Dependencies

- `Adze.Broker` — for `FeatureGateRegistry`, `FeatureGateConfigService`, `ApiKeyStore`, `BrokerModelSettings`, `SwBuildStateService`, `PreUpdateEjectService`

**What it does NOT depend on:** `Adze.Host` (no COM coupling), `SolidWorks.Interop.*` (no SW API). The Manager runs even when SW isn't installed.

## Logging discipline

The Manager itself does NOT write to `host.log` — that's the SW-add-in's log. Manager-side actions (Install button clicks, scripts invoked from the GUI) are logged to `%LOCALAPPDATA%\Adze\logs\manager.log` with timestamps and the SHA256 + last-write-time of any script the Manager runs (forensic logging added session 6).

## Launch options

Double-click `Adze.Manager.exe`. No command-line arguments needed. Idempotent across launches.

## Cross-refs

- `plans/project-index.md` §1 — project inventory
- `plans/ui-rebuild-v1.1.md` Chunk 1 — Manager 4-tab refactor history
- `install/CLAUDE.md` — install procedure (Manager is the post-install tool)
- brain.db Decision #21 — Native WinForms as v1.1+ UI authority (Manager is part of that)
- brain.db Decision #23 — `AddInsStartup\{GUID}` invariant (Manager exposes the Status check)
