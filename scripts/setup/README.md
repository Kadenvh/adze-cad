# Setup Scripts

This directory contains the active developer scripts for build, registration, launch, validation, and fixture discovery.

## Contents

| File | Purpose |
|------|---------|
| `validate-json-schemas.ps1` | Validate all JSON schemas under `schemas/` |
| `check-solidworks-host-readiness.ps1` | One-shot preflight for interop and host prerequisites |
| `wait-for-solidworks-host-readiness.ps1` | Poll until the host prerequisites are visible |
| `build-host.ps1` | Build the host project only |
| `build-all.ps1` | Build the full solution, optionally after stopping SOLIDWORKS |
| `stop-solidworks-processes.ps1` | Stop the known host-side processes used during reload and validation |
| `register-host-addin.ps1` | Write per-user COM and SOLIDWORKS add-in registration |
| `unregister-host-addin.ps1` | Remove the per-user registration written by the register script |
| `launch-and-check-host.ps1` | Launch the desktop path and report whether host load was blocked |
| `open-sample-document.ps1` | Open a target sample once a live `SldWorks.Application` server is available |
| `reload-host.ps1` | Stop, build, register, launch, and optionally open a sample document |
| `validate-host-spike.ps1` | End-to-end validation of the current host/tool baseline |
| `run-grounding-benchmarks.ps1` | Execute the curated grounding benchmark suite |
| `run-broker-evals.ps1` | Evaluate broker tool recommendation against the curated tasks |
| `run-tests.ps1` | Restore NuGet packages, build, and run the NUnit 3 compiled unit test suite |
| `collect-support-bundle.ps1` | Collect logs, traces, snapshots, latest reports, and preflight output into a zipped support bundle |
| `RegressionReportHelpers.ps1` | Shared helper for writing JSON regression reports |
| `find-custom-property-fixtures.ps1` | Discover parts with usable custom properties |
| `find-dimension-fixtures.ps1` | Discover parts with usable display dimensions |
| `find-reference-graph-fixtures.ps1` | Discover assemblies with reference-graph value |
| `inspect-annotation-types.ps1` | Inspect live annotation and dimension behavior during fixture debugging |

## Conventions

- Run these scripts with `pwsh -NoProfile -File` unless the script explicitly depends on Windows PowerShell.
- Development registration is per-user by default.
- Treat launcher state as part of validation. If `launch-and-check-host.ps1` reports a login or update window, clear it before assuming the host failed.
- Benchmark and broker-eval runs write JSON reports under `benchmarks/reports`.
- Support bundles default to `%LOCALAPPDATA%\Adze\SupportBundles` and are intended for beta/support triage, not for source control.

## Adding New Items

- Put reusable developer workflows here, not one-off notes.
- Keep scripts safe to rerun and log any unattended or installer-related work.
- Update this README when a new workflow script becomes part of the normal path.
