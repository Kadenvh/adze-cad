# Adze.Tests

NUnit 3 unit tests for the Adze broker, tools, and trace layers.

## Running

```powershell
pwsh -NoProfile -File scripts\setup\run-tests.ps1
```

The script downloads `nuget.exe` on first run, restores NUnit packages, builds, and runs via the NUnit 3 console runner.

## Structure

| Folder | Coverage |
|--------|----------|
| `Broker/` | KeywordBrokerOrchestrator, HybridBrokerOrchestrator, ModelResponseParser, BrokerModelSettings, ContextPromptComposer, ModelClientFactory |
| `Tools/` | All 10 grounding tools via mock SessionContext |
| `Trace/` | ModelJsonMapper round-trip serialization |
| `Helpers/` | SessionContextFactory — shared mock context builders |

## Conventions

- Test naming: `MethodName_Scenario_ExpectedResult`
- Arrange-Act-Assert pattern
- No file I/O or network calls — pure logic only
- `SessionContextFactory` provides reusable context shapes (part, assembly, selection, diagnostics, etc.)
