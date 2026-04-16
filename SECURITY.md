# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in Adze, please report it responsibly:

**Email:** kaden@vhtech.me
**Subject prefix:** `[SECURITY]`

Please include:
- Description of the vulnerability
- Steps to reproduce
- Affected version(s)
- Potential impact

Do not open a public GitHub issue for security vulnerabilities. Acknowledgment within 72 hours, initial triage within 7 days.

## Scope

Adze runs as an in-process COM add-in inside SOLIDWORKS. Security-relevant areas:

- **API key handling** — keys are read from environment variables (`SOLIDWORKS_AI_*_API_KEY`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`) or a repo-root `.env` file. Keys are never logged, cached to disk, or transmitted anywhere except the provider endpoint they authenticate against. `.env` is gitignored.
- **Data transmission** — when using cloud AI providers, SOLIDWORKS session data (feature trees, dimensions, mates, properties, tool results) is sent to the user's chosen provider endpoint. Users who want zero egress can configure Ollama or LM Studio for fully local inference. See `PRIVACY.md` for the full data flow.
- **Write operations** — all write tools follow an 8-step safety lifecycle: plan, preview, approve, apply, verify, trace, undo-label, history. User approval is required before any modification is applied. `AgentPolicyEngine` enforces trust-tier gating on advanced write tools.
- **Local storage** — traces, logs, snapshots, and progression data are stored under `%LOCALAPPDATA%\Adze`. These files include document paths, machine name, selection/reference metadata, custom properties, and tool-result payloads. `host.log` is intended for operational metadata rather than full answer or plan transcripts.
- **COM boundary** — all SOLIDWORKS COM interop is contained within `Adze.Host`. The broker, tools, and trace projects do not call COM directly. External processes cannot invoke SOLIDWORKS operations through Adze.
- **Open source** — every line that handles data is publicly auditable at https://github.com/Kadenvh/adze-cad.

## Secure development practices

- Secret scanning enabled on GitHub (push protection active)
- Dependabot enabled for NuGet + GitHub Actions updates (weekly)
- CodeQL analysis on every push and pull request to `main`
- Branch protection on `main` (required status checks, no direct pushes)
- All AI-provider HTTP clients verify TLS certificates (default .NET behavior)
- Rate-limit handling with exponential backoff to avoid triggering provider abuse detection

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |

Security fixes will be backported to the latest 0.1.x release. Older versions are not supported once a newer minor release is available.
