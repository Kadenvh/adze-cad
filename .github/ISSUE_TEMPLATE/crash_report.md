---
name: Crash Report
about: SOLIDWORKS crashed when using, enabling, or installing Adze
title: 'Crash: '
labels: bug, crash
assignees: ''
---

**What crashed**
- [ ] SOLIDWORKS itself (hard close / no error dialog)
- [ ] SOLIDWORKS froze (had to force-kill)
- [ ] An error dialog appeared (paste the message below)
- [ ] Adze Task Pane (SW stayed up, just Adze UI broke)
- [ ] Installer / uninstaller

**When did it crash?**
Pick the closest match:
- [ ] On SOLIDWORKS launch with Adze installed
- [ ] When enabling Adze via Tools > Add-Ins
- [ ] When clicking a button in the Task Pane
- [ ] When typing a prompt / running the assistant
- [ ] When opening a specific document
- [ ] During install / uninstall
- [ ] Other — describe in "To reproduce" below

**To reproduce**
1. Open '...'
2. Click '...'
3. See crash

**Environment**
- **SOLIDWORKS build number:** [e.g. 34.1.0.0140 — find in Help > About SOLIDWORKS]
- **SOLIDWORKS edition:** [e.g. 3DEXPERIENCE R2026x, 2025 SP2, 2026 SP0]
- **License type:** [e.g. Professional, Premium, Makers]
- **Adze version:** [e.g. v0.1.1 — see Task Pane footer or release zip name]
- **AI provider:** [e.g. OpenAI, Anthropic, Ollama, none / offline broker]
- **Windows version:** [e.g. Windows 11 23H2]
- **Recent SW / 3DEXPERIENCE update?** [Yes — applied YYYY-MM-DD / No]

**Crash artifacts**

If SOLIDWORKS generated a crash dump, please attach or quote the path to:
- `%APPDATA%\SolidWorks\YYYYMMDDHHMMSS_<build>\CXPD\*.dmp` (SW's own crash session)
- `%LOCALAPPDATA%\CrashDumps\sldworks.exe.*.dmp` (Windows Error Reporting, if enabled)

And the Adze log:
- `%LOCALAPPDATA%\Adze\logs\host.log` — at least the last 100 lines around the crash timestamp

**Feature gates active at crash**
If you've set any `SOLIDWORKS_AI_*` environment variables, list them here. Or, open `%LOCALAPPDATA%\Adze\config.json` (if it exists) and paste contents.

```
SOLIDWORKS_AI_RIBBON=...
SOLIDWORKS_AI_CONTEXT_MENU=...
(etc.)
```

**Additional context**
Anything else we should know — recent system changes, concurrent installs, add-ins interacting with Adze, etc.
