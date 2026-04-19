# Privacy Policy

**Last updated:** 2026-04-16
**Applies to:** Adze (adze-cad) — native AI assistant for SOLIDWORKS

---

## Summary

Adze is a local add-in. It does not collect, store, or transmit your data to VH Tech LLC or any third party. When you configure a cloud AI provider, requests to that provider are governed by your own agreement with that provider. Adze is not an intermediary, data broker, or telemetry collector.

If you want zero data to leave your machine, configure a local model provider (Ollama or LM Studio) and Adze operates fully offline.

---

## What Adze does with your SOLIDWORKS data

Adze reads your active SOLIDWORKS document at runtime through the SOLIDWORKS COM API. This includes feature trees, dimensions, mates, custom properties, configurations, rebuild diagnostics, and reference graphs. Adze also indexes closed SOLIDWORKS files in folders you explicitly enable retrieval for.

Nothing from your SOLIDWORKS session is persisted beyond local trace files stored on your own machine at `%LOCALAPPDATA%\Adze`. These trace files are for your own debugging and history. They never leave your computer.

---

## What happens when you use a cloud AI provider

When you configure Adze with an OpenAI, Anthropic, or OpenRouter API key, Adze sends the following to that provider as part of each request:

- The prompt you typed in the Task Pane
- Session context slices (feature tree, dimensions, diagnostics, etc.) required to answer your question
- Tool call results during the agentic loop

Data sent to a cloud provider is governed by **that provider's** terms and privacy policy, not Adze's. VH Tech LLC has no access to these requests, no relationship with the provider, and does not receive any copy of the data.

If a cloud provider logs or trains on your inputs, that is a property of your account with them. Consult their privacy policy directly.

---

## What happens when you use a local model

When you configure Adze with Ollama or LM Studio, all inference happens on your own machine. No network request is made to any external service. No data leaves your computer.

This is the recommended configuration for confidential designs or enterprise environments with data-egress concerns.

---

## What Adze does not do

- Adze does not transmit usage telemetry to VH Tech LLC or any third party
- Adze does not send crash reports to any remote service
- Adze does not phone home with version checks
- Adze does not read files outside the SOLIDWORKS document, trace directory, and explicitly opted-in retrieval folders
- Adze does not integrate with any analytics service (no Google Analytics, no Segment, no Amplitude)

Adze does maintain a session-local telemetry dashboard (runs, token usage, success rates, tool call rankings) visible in the Task Pane Status section. This data stays in memory during the session and is never transmitted. It exists so you can see your own usage, not so we can see it.

---

## Local trace files

Adze writes trace files to `%LOCALAPPDATA%\Adze` on your machine. These include:

- Session traces (what tools ran, what results they returned)
- Write snapshots (before/after state for every write operation)
- Recipe candidates (captured workflows for potential reuse)
- Progression state (trust tier, achievements)
- Launcher preflight diagnostics
- Cost and usage accounting

These files are yours. They never leave your machine. You can delete them at any time:

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Adze"
```

---

## API keys and credentials

Adze reads API keys in priority order:

1. Environment variables (`SOLIDWORKS_AI_OPENAI_API_KEY`, `SOLIDWORKS_AI_ANTHROPIC_API_KEY`, or their non-prefixed variants).
2. A `.env` file at the repository root (developer setups only; not shipped in the release zip).
3. The DPAPI-encrypted key store at `%LOCALAPPDATA%\Adze\keys.dat`, populated when you enter a key in the Task Pane Settings panel.

The DPAPI store uses Windows' user-scope data protection — the encrypted bytes can only be decrypted by the same Windows user on the same machine. Keys stored there are not retrievable from a backup restored to a different machine or account. Keys are not logged, cached, or transmitted anywhere except the provider endpoint they authenticate against.

Never commit `.env` files to a public repository. The Adze repository's `.gitignore` excludes `.env` by default.

---

## Update lifecycle and compatibility state

Adze runs a read-only compatibility probe at SOLIDWORKS launch that asks the live SW COM API for its revision number, gets its command manager, and creates + immediately removes a throwaway command group. The probe detects when a 3DEXPERIENCE / SOLIDWORKS update has broken interop so Adze can refuse to register damaging UI hooks instead of crashing.

- The probe output (revision + pass/fail + failed step) is written to `%LOCALAPPDATA%\Adze\logs\host.log` on your machine. It never leaves your computer.
- The last-verified SW build string is persisted to `%LOCALAPPDATA%\Adze\state\sw-build.txt` so Adze can detect build changes on next launch. It never leaves your computer.
- During uninstall or when the 3DX updater (`swxdesktopupdate.exe`) is detected running, Adze clears the persisted build so the next launch re-verifies from scratch.
- The Adze Manager utility detects whether `sldworks.exe` and `swxdesktopupdate.exe` are currently running (local process enumeration only) to help you pick a safe time to install, uninstall, or eject before an update. No process list or update status ever leaves your machine.

---

## Open source and transparency

Adze is MIT-licensed and fully open source at https://github.com/Kadenvh/adze-cad. Every line of code that handles your data is publicly auditable. If you suspect a privacy issue, open an issue or pull request on GitHub.

---

## Contact

Privacy questions: kaden@vhtech.me

Security issues: see `SECURITY.md` for responsible disclosure.
