# Diagnostics

This directory contains planning-time and environment diagnostics used to validate the local Windows, launcher, and SOLIDWORKS setup.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `powershell-launch-smoke.ps1` | Confirms PowerShell host, execution policy, elevation state, and logging behavior |
| `scripts/` | One-off cleanup and recovery scripts used during install/reset work |
| `logs/` | Captured installer logs, cleanup logs, smoke-test output, and investigation artifacts |
| `downloads/` | Cached installer or prerequisite payloads referenced by diagnostics work |

## Conventions

- Keep diagnostics narrowly scoped and safe to rerun.
- Log every unattended or installer-related action.
- Treat this folder as historical setup evidence, not application runtime code.

## Adding New Items

- Add a script here only when it helps isolate or recover from a machine-specific setup issue.
- Write output to `logs/` with task-specific filenames.
- Move durable app workflows into `scripts/setup/` instead of leaving them here.
