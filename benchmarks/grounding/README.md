# Grounding Benchmarks

This directory contains the curated metadata for the current grounding benchmark suite.

## Contents

| File | Purpose |
|------|---------|
| `starter-corpus.manifest.json` | Lists the approved local and installed fixtures used by the benchmark suite |
| `starter-grounding-tasks.json` | Benchmark prompts, expected tools, and fixture bindings |
| `task-definition.schema.json` | Contract for grounding benchmark task definitions |

## Conventions

- Keep only metadata, task definitions, and expected outputs here. Source CAD files stay under `C:\SOLIDWORKS` or the installed sample tree.
- Treat task ids and fixture ids as stable once they are used by validation scripts.
- Expand the suite with real fixtures that exist on this machine, not aspirational placeholders.

## Adding New Items

- Add a fixture entry to `starter-corpus.manifest.json`.
- Add one or more grounded tasks to `starter-grounding-tasks.json`.
- Update the schema only when the task contract truly changes.
