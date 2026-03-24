# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in Adze, please report it responsibly:

**Email:** kaden@vhtech.me

Please include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact

Do not open a public GitHub issue for security vulnerabilities.

## Scope

Adze runs as an in-process COM add-in inside SOLIDWORKS. Security-relevant areas include:

- **API key handling** — keys are read from environment variables, never stored by Adze
- **Data transmission** — when using cloud AI providers, SOLIDWORKS session data is sent to the user's chosen provider. Users can use local models (Ollama/LM Studio) for complete privacy.
- **Write operations** — all write tools follow an 8-step safety lifecycle with preview, approval, and verification
- **Local storage** — traces, logs, and progression data are stored under `%LOCALAPPDATA%\Adze`

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.1.x   | Yes       |
