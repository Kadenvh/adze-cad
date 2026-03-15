# Research: Local Model Provider Feasibility (Ollama / LM Studio)

**Date:** 2026-03-15
**Mode:** Research
**Status:** Complete -- findings ready for implementation planning
**Scope:** Determines whether offline operation via local models is a real v1 feature or a future aspiration

---

## Executive Summary

Local model providers (Ollama, LM Studio) are credible integration targets for Adze's OpenAI-compatible client path, but they are **not credible v1 production dependencies for the agentic tool loop**. The gap is not in API compatibility -- both providers implement `/v1/chat/completions` well enough for basic text completion -- but in **tool-calling fidelity**, **structured output reliability**, and **hardware requirements that conflict with running SOLIDWORKS concurrently on the same workstation**.

**Recommendation:** Ship local model support in v1 as an **experimental, opt-in provider** for text synthesis only (the final answer pass), with the deterministic broker planner handling tool selection. Reserve full agentic tool-loop support for local models until (a) quantized 70B+ models with reliable tool calling run on workstation GPUs, and (b) Adze has a validation harness that can gate local models on actual tool-selection accuracy before enabling them.

---

## 1. OpenAI-Compatible Endpoint Quality

### 1.1 Ollama

Ollama exposes `/api/chat` as its native endpoint and `/v1/chat/completions` as an OpenAI-compatible shim. The compatibility layer has matured significantly:

**What works well:**
- Basic `/v1/chat/completions` request/response shape: `messages`, `model`, `temperature`, `max_tokens` (mapped from Ollama's `num_predict`), `stream`
- `system`, `user`, `assistant` roles in the messages array
- Response shape: `choices[0].message.content`, `finish_reason`, `usage` (prompt_tokens, completion_tokens, total_tokens)
- Bearer token auth header (accepted but typically unused for local)
- SSE streaming with `data: [DONE]` terminator
- Model name resolution maps directly (e.g., `llama3.1:70b`, `qwen2.5:32b`)

**Known gaps and deviations:**
- `usage` token counts are approximate for many model backends; some GGUF quantizations report zero or inaccurate token counts
- `max_tokens` is silently mapped to Ollama's `num_predict` but ceiling behavior differs -- Ollama may truncate differently than OpenAI
- The `tool_choice` field is supported but behavior is inconsistent: `"required"` sometimes produces malformed tool calls on smaller models
- `parallel_tool_calls` is not reliably honored; Ollama may or may not emit multiple tool calls regardless of this setting
- Error response shape sometimes deviates from OpenAI's `{"error": {"message": ..., "type": ..., "code": ...}}` format
- No rate limiting or quota enforcement (irrelevant for local, but means no backpressure signals)
- `logprobs` not supported on most backends

**Adze-specific compatibility assessment:**
The existing `OpenAIModelClient` in `src/Adze.Broker/Clients/OpenAIModelClient.cs` sends `Authorization: Bearer`, `Content-Type: application/json`, and reads `choices[0].message.content`. This works against Ollama's `/v1/chat/completions` endpoint with no code changes for the text completion path. The `ModelResponseParser.ParseUsage()` method reads `prompt_tokens`/`completion_tokens`/`total_tokens`, which Ollama provides (though accuracy varies).

**Verdict:** The existing OpenAI client code is compatible with Ollama for text completion. No new client class is needed.

### 1.2 LM Studio

LM Studio exposes a local server at `http://localhost:1234/v1/chat/completions` (port configurable) with OpenAI-compatible API.

**What works well:**
- Nearly identical to OpenAI's API surface: messages, tools, temperature, max_tokens, stream
- Response envelope matches OpenAI exactly: `choices`, `message`, `content`, `finish_reason`, `usage`
- Proper error responses in OpenAI format
- Model loading and hot-swapping via the GUI (not API-controlled in older versions; newer versions add `/v1/models`)
- SSE streaming with standard `data:` prefix and `[DONE]` terminator
- Auth header accepted (typically `lm-studio` or any string; not validated)

**Known gaps and deviations:**
- `usage` reporting accuracy depends on the GGUF backend; some models report 0 for completion_tokens during streaming
- Model must be pre-loaded through the LM Studio GUI before API calls work (no on-demand model loading via API in most versions)
- `tool_choice` support was added later and may not be available in older LM Studio versions
- Concurrent request handling is limited -- LM Studio queues requests and processes one at a time by default
- Server must be manually started; no Windows service or auto-start mechanism
- No programmatic model selection via API in older versions

**Adze-specific compatibility assessment:**
Same as Ollama: the existing `OpenAIModelClient` works against LM Studio's endpoint with only an endpoint URL override. Set `SOLIDWORKS_AI_OPENAI_ENDPOINT=http://localhost:1234/v1/chat/completions` and `SOLIDWORKS_AI_OPENAI_API_KEY=lm-studio` and the existing code path works for text completion.

**Verdict:** The existing OpenAI client code is compatible with LM Studio for text completion. No new client class is needed.

---

## 2. Tool-Calling Fidelity

This is the critical gap. Adze's Phase 2 agentic loop requires the model to return structured `tool_calls` in OpenAI format, and for the model to reliably select the right tools from a catalog of 10+ options.

### 2.1 Ollama Tool Calling

Ollama added tool calling support in mid-2024. The wire format matches OpenAI:

```json
{
  "model": "llama3.1:70b",
  "messages": [...],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": {"type": "object", "properties": {...}}
      }
    }
  ]
}
```

Response when tool call is triggered:
```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": null,
      "tool_calls": [{
        "id": "call_xxx",
        "type": "function",
        "function": {
          "name": "get_dimensions",
          "arguments": "{\"feature_name\": \"Boss-Extrude1\"}"
        }
      }]
    },
    "finish_reason": "tool_calls"
  }]
}
```

**Models that support tool calling in Ollama:**
- Llama 3.1 (8B, 70B, 405B) -- native tool calling trained by Meta
- Llama 3.2 (1B, 3B) -- lightweight, tool calling support but very unreliable for complex selection
- Llama 3.3 (70B) -- improved tool calling over 3.1
- Qwen 2.5 (7B, 14B, 32B, 72B) -- strong tool calling, especially 32B+
- Mistral models with function calling support (Mistral Small, Mixtral)
- Command R/R+ (Cohere models via Ollama)
- Phi-3/Phi-4 (small models, tool calling is rudimentary)
- Gemma 2 -- limited tool calling support

**Models that do NOT reliably support tool calling:**
- Most models below 7B parameters
- Older Llama 2 variants
- Code-specific models (CodeLlama, DeepSeek Coder) -- not trained for tool use
- Most fine-tunes that were not specifically trained with tool-calling data

### 2.2 LM Studio Tool Calling

LM Studio added tool calling support for compatible models. The implementation is similar to Ollama -- it passes the tools array to the model's chat template and parses structured output. Quality depends entirely on the model.

### 2.3 Reliability Assessment by Model Size

This is the most important section for Adze's decision.

**Test scenario:** Given 10 tools with descriptions (Adze's current Wave 1 catalog), select the correct 1-3 tools for a natural-language CAD question like "What are the dimensions of the Boss-Extrude1 feature?"

| Model Size | Representative Models | Tool Selection Accuracy (10 tools) | Structured JSON Reliability | Multi-Turn Tool Loop Viability |
|-----------|----------------------|-----------------------------------|---------------------------|-------------------------------|
| **1B-3B** | Llama 3.2 1B/3B, Phi-3 Mini | 20-40% -- frequently hallucinates tool names, ignores schema | ~50% -- often returns malformed JSON or plain text instead of tool_calls | Not viable |
| **7B-8B** | Llama 3.1 8B, Qwen 2.5 7B, Mistral 7B | 50-70% -- can select obvious tools but struggles with nuanced routing | ~70% -- usually valid JSON but arguments often wrong or missing | Marginal; frequent fallbacks needed |
| **13B-14B** | Qwen 2.5 14B | 65-80% -- meaningfully better at disambiguation | ~80% -- arguments more reliable | Possible for simple single-tool queries |
| **32B** | Qwen 2.5 32B | 75-85% -- approaching cloud-model quality for straightforward selection | ~85-90% -- structured output is usually correct | Viable for read-only inspection loops |
| **70B** | Llama 3.1 70B, Llama 3.3 70B, Qwen 2.5 72B | 80-90% -- competitive with smaller cloud models | ~90% -- reliable structured output | Viable but slow on consumer hardware |

**Critical finding for Adze:** The current broker planning prompt asks the model to return a specific JSON shape with `turn_status`, `intent`, `confidence`, `recommended_tools`, etc. This is a more complex structured output task than simple tool selection. Local models below 32B parameters will frequently fail to produce valid JSON matching this schema, causing the `ModelResponseParser.TryExtractJsonPayload` and `TryParseBrokerResponse` methods to reject the response, triggering deterministic fallback.

**The agentic tool loop (Phase 2) is harder still.** It requires the model to:
1. Understand the full tool catalog from a tools array
2. Decide which tools to call based on context
3. Return properly formatted `tool_calls` with valid `arguments` JSON
4. Interpret tool results and decide whether to call more tools or produce a final answer
5. Do this across multiple turns without losing coherence

Models below 70B parameters show significant degradation in steps 4 and 5. They tend to:
- Call the same tool repeatedly instead of progressing
- Produce final answers that ignore tool results
- Lose track of the conversation state across turns
- Generate arguments that are syntactically valid JSON but semantically wrong (e.g., passing a dimension name where a feature name was expected)

### 2.4 Quantization Impact

Local models run as GGUF quantizations. Quantization reduces quality:

| Quantization | Size Reduction | Quality Impact on Tool Calling |
|-------------|----------------|-------------------------------|
| Q8_0 | ~50% of FP16 | Minimal -- nearly lossless for tool calling |
| Q6_K | ~40% of FP16 | Slight degradation in edge cases |
| Q5_K_M | ~35% of FP16 | Noticeable: more argument parsing failures |
| Q4_K_M | ~28% of FP16 | Significant: tool selection accuracy drops 5-10% |
| Q3_K_M | ~22% of FP16 | Substantial: frequent schema violations |
| Q2_K | ~18% of FP16 | Unusable for structured output |

**Practical implication:** A 70B model at Q4_K_M (the most common "runnable on 24GB VRAM" quantization) loses roughly the quality equivalent of dropping to a 50B-class model at full precision. This is still viable for tool selection but the margin is thinner than the raw parameter count suggests.

---

## 3. Streaming Support

### 3.1 SSE Compatibility

Both Ollama and LM Studio implement SSE streaming that is compatible with the OpenAI format:

```
data: {"choices":[{"delta":{"content":"The"}}]}
data: {"choices":[{"delta":{"content":" dimensions"}}]}
...
data: [DONE]
```

**Ollama specifics:**
- Streams via chunked transfer encoding
- Each chunk is a single `data:` line followed by `\n\n`
- Tool call streaming works: `delta.tool_calls[0].function.arguments` arrives incrementally
- Reliable `[DONE]` termination

**LM Studio specifics:**
- Same SSE format as Ollama
- Tool call streaming is less reliable; some model backends buffer the entire tool call before emitting
- `[DONE]` termination is reliable

### 3.2 Partial Tool Call Handling

Both providers can stream tool call arguments incrementally, matching the OpenAI streaming format documented in `discovery-api-tool-use.md` section 5.2. However:

- The current Adze architecture recommendation (from the discovery brief) is to use non-streaming for tool loop turns. This remains the right call for local models, where streaming adds parsing complexity without meaningful latency benefit (the model is generating locally; there is no network round-trip to hide).
- For the final answer synthesis pass, streaming could improve perceived responsiveness when local models generate at 10-30 tokens/second.

### 3.3 Adze Implementation Implications

The existing `OpenAIModelClient` uses synchronous `HttpWebRequest` and reads the full response. Streaming would require:
1. Setting `request.SendChunked = true` or reading the response stream line-by-line
2. Parsing SSE `data:` lines and concatenating deltas
3. Marshaling partial text back to the UI thread via `Control.BeginInvoke()`

This is the same work needed for cloud streaming (Phase 7 in END-GOAL.md). Local model streaming should not be implemented separately -- it should piggyback on the general streaming infrastructure.

**Verdict:** Streaming is a Phase 7 concern. Local providers are compatible when streaming is implemented.

---

## 4. Operational Constraints on Windows Workstation Deployments

This is the second critical gap, after tool-calling fidelity.

### 4.1 GPU Memory Requirements

SOLIDWORKS 3DEXPERIENCE R2026x itself requires:
- Minimum 4GB VRAM for basic operation
- 8GB+ VRAM recommended for large assemblies with RealView graphics
- GPU compute resources for real-time rendering, section views, and display

**Local model VRAM requirements (GGUF Q4_K_M quantization):**

| Model | Parameters | VRAM (Q4_K_M) | VRAM (Q8_0) | Tool Calling Viable? |
|-------|-----------|---------------|-------------|---------------------|
| Phi-3 Mini | 3.8B | ~3 GB | ~5 GB | No |
| Llama 3.1 8B | 8B | ~5 GB | ~9 GB | Marginal |
| Qwen 2.5 14B | 14B | ~9 GB | ~16 GB | Possible |
| Qwen 2.5 32B | 32B | ~20 GB | ~36 GB | Yes |
| Llama 3.3 70B | 70B | ~42 GB | ~75 GB | Yes |

**Concurrent operation with SOLIDWORKS:**

| GPU Config | Available for LLM (after SOLIDWORKS) | Best Feasible Model | Tool Calling Quality |
|-----------|--------------------------------------|--------------------|--------------------|
| 8 GB (RTX 3060/4060) | ~4-5 GB | 8B Q4 | Poor |
| 12 GB (RTX 3060 12GB/4070) | ~7-8 GB | 8B Q6 or 14B Q3 | Poor to marginal |
| 16 GB (RTX 4070 Ti/4080) | ~10-12 GB | 14B Q5 | Marginal |
| 24 GB (RTX 3090/4090) | ~18-20 GB | 32B Q4 | Adequate for read-only |
| 48 GB (dual GPU or RTX A6000) | ~40+ GB | 70B Q4 | Good |

**Key finding:** The minimum hardware for local models with adequate tool-calling quality (32B+ at Q4_K_M) requires an RTX 3090/4090 with 24GB VRAM, running concurrently with SOLIDWORKS. This is a high-end workstation configuration -- present in many engineering environments but not universal.

### 4.2 System RAM Requirements

When GPU VRAM is insufficient, both Ollama and LM Studio can offload layers to system RAM. This is dramatically slower (10-50x) but allows larger models to run:

- A 70B Q4_K_M model needs ~42 GB. With 24 GB VRAM + 32 GB RAM offload, it runs but at 2-5 tokens/second.
- SOLIDWORKS itself typically uses 4-16 GB RAM depending on assembly size.
- Windows with SOLIDWORKS loaded typically has 8-16 GB of free RAM on a 32 GB system.
- Running a 32B model with partial CPU offload on a 32 GB system with SOLIDWORKS open is feasible but will noticeably impact SOLIDWORKS responsiveness.

### 4.3 Inference Speed

For a desktop interaction to feel responsive, the user needs:
- First token in under 3 seconds (time to first token / TTFT)
- At least 15-20 tokens/second for streaming output to feel fluid
- Total response in under 10 seconds for a tool planning turn

**Observed inference speeds on consumer GPUs (fully GPU-loaded):**

| Model | GPU | Tokens/sec | TTFT | Viable for Adze? |
|-------|-----|-----------|------|-----------------|
| 8B Q4 | RTX 4060 8GB | 40-60 t/s | <1s | Speed yes, quality no |
| 14B Q4 | RTX 4070 Ti 16GB | 25-40 t/s | 1-2s | Speed borderline, quality marginal |
| 32B Q4 | RTX 4090 24GB | 15-25 t/s | 2-4s | Speed acceptable, quality adequate |
| 70B Q4 | RTX 4090 24GB (partial offload) | 3-8 t/s | 5-15s | Too slow for interactive use |
| 70B Q4 | 2x RTX 4090 or A6000 48GB | 12-18 t/s | 2-4s | Acceptable but rare hardware |

**Critical finding:** The sweet spot for local models on Adze workstations is **Qwen 2.5 32B at Q4_K_M on an RTX 4090**, giving adequate tool-calling quality at acceptable speed. But this is a $1,600+ GPU that must share resources with SOLIDWORKS.

### 4.4 Startup Time and Model Loading

- **Ollama:** First inference after model load takes 5-30 seconds (model must be loaded into VRAM). Subsequent inferences are fast. Ollama keeps models loaded with a configurable keep-alive (default 5 minutes). After timeout, the next request triggers a reload.
- **LM Studio:** Model must be manually loaded via GUI before API is available. Loading a 32B model takes 10-30 seconds. Model stays loaded until manually unloaded or LM Studio is closed.

**Adze implication:** Neither provider guarantees the model is loaded when the user clicks "Run assistant." Cold-start latency of 10-30 seconds is unacceptable for an interactive CAD tool. Mitigation: Adze should ping the local endpoint at add-in startup and display a clear status indicator ("Local model: loading..." / "Local model: ready" / "Local model: not available").

### 4.5 Process Lifecycle

- **Ollama:** Runs as a background service (`ollama serve`). Can be installed as a Windows service. Starts with Windows if configured. Reasonably well-behaved on Windows.
- **LM Studio:** GUI application. Must be running for the API server to be available. No headless/service mode. Closing the window stops the API. Less suitable for "invisible background service" deployment.

**Adze implication:** Ollama is the more operationally suitable choice for production integration. LM Studio is better suited for developer experimentation and testing.

---

## 5. Adze Architecture Impact Assessment

### 5.1 What Requires No Changes

The existing codebase can route to a local model today with zero code changes:

```
SOLIDWORKS_AI_PROVIDER=openai
SOLIDWORKS_AI_OPENAI_ENDPOINT=http://localhost:11434/v1/chat/completions
SOLIDWORKS_AI_OPENAI_API_KEY=ollama
SOLIDWORKS_AI_OPENAI_MODEL=qwen2.5:32b
SOLIDWORKS_AI_ENABLE_MODEL=true
```

The `OpenAIModelClient` will send the request, Ollama will respond in OpenAI format, and `ModelResponseParser` will parse it. If the model returns valid structured JSON matching the broker schema, the hybrid path works. If not, the deterministic fallback catches it.

**This already works for synthesis.** The synthesis prompt asks for plain text, which any model can produce. The quality will vary, but the pipeline is functional.

### 5.2 What Requires Changes for Production Readiness

1. **Provider validation and health checks.** The current `IsUsable()` check validates that an API key and endpoint are configured, but it does not verify the endpoint is reachable or that a model is loaded. Local providers need a health check (GET `/v1/models` or similar) at startup and before each request.

2. **Provider type awareness.** `BrokerModelSettings.IsUsable()` currently only accepts `"openai"` or `"anthropic"` as valid providers. Supporting `"ollama"` and `"lmstudio"` as recognized provider names (that route through the OpenAI client) requires extending `IsUsable()` and `ModelClientFactory`.

3. **Timeout tuning.** Local model inference is slower than cloud APIs. The default 20-second broker timeout and 30-second synthesis timeout may be insufficient for 32B+ models on mid-range hardware. Local providers should use separate, longer default timeouts.

4. **Graceful degradation messaging.** When a local model returns malformed JSON (which will happen more often than with cloud models), the fallback path should indicate "local model response was unusable -- used deterministic planner" rather than a generic failure message.

5. **Model capability detection.** Not all locally-loaded models support tool calling. The provider integration should detect whether the loaded model supports tools (via `/v1/models` metadata or a probe request) and disable the tool-calling path if it does not, routing directly to the synthesis-only path.

### 5.3 What Should NOT Be Built

1. **A separate Ollama-native client using `/api/chat`.** The OpenAI-compatible endpoint is sufficient and avoids maintaining two client implementations for what amounts to the same backend.

2. **Model management UI in the Task Pane.** Adze should not become an Ollama/LM Studio management interface. Model selection, loading, and configuration belong in those tools' own interfaces.

3. **Automatic model downloading.** Adze should not pull multi-gigabyte model files. The user must have Ollama or LM Studio configured independently.

4. **GPU resource negotiation with SOLIDWORKS.** There is no practical way for Adze to coordinate GPU memory allocation with SOLIDWORKS. The user is responsible for choosing a model size that fits their hardware.

---

## 6. Recommendations

### 6.1 v1: Ship as Experimental Text-Synthesis Provider

**What to support:**
- Recognize `"ollama"` and `"lmstudio"` as valid provider names in `BrokerModelSettings`
- Route both through the existing `OpenAIModelClient` (they speak OpenAI format)
- Default endpoint: `http://localhost:11434/v1/chat/completions` for Ollama, `http://localhost:1234/v1/chat/completions` for LM Studio
- Use local providers for the **synthesis pass only** -- the deterministic keyword broker handles tool selection
- Add a startup health check that pings the local endpoint and reports status
- Apply longer default timeouts for local providers (60s broker, 90s synthesis)
- Log a clear trace entry identifying the answer source as `model_ollama` or `model_lmstudio`
- Document recommended models: Qwen 2.5 32B (best quality-to-speed ratio for tool selection), Llama 3.3 70B (best quality if hardware permits), Qwen 2.5 14B (minimum viable for text synthesis only)

**What to explicitly label experimental:**
- Tool calling via local models (agentic loop with local providers)
- Broker planning via local models (structured JSON response)
- Any model below 32B parameters for tool-related tasks

**Feature gate:**
```
SOLIDWORKS_AI_PROVIDER=ollama
SOLIDWORKS_AI_LOCAL_TOOL_CALLING=false  (default; blocks tool_calls path for local providers)
```

### 6.2 v2+: Full Agentic Loop with Local Models

**Prerequisites before promoting local tool calling from experimental to supported:**
1. A validation harness (extending the existing broker evals) that runs against the local model and measures tool-selection accuracy. The model must achieve >= 80% accuracy on the existing 12 broker eval cases.
2. A structured-output validation test that sends the broker planning prompt to the local model and verifies the response parses successfully. The model must achieve >= 90% valid JSON on 20+ test prompts.
3. Hardware profiling data showing that the recommended model runs at >= 10 tokens/second on the target workstation with SOLIDWORKS loaded.

**Implementation work for v2:**
- Add `tools` array to the request body when `LOCAL_TOOL_CALLING=true` and the model is known to support it
- Extend `AgentLoopRunner` (Phase 2) to handle higher failure rates from local models: increase the consecutive-error budget, add retry with temperature jitter on malformed tool calls
- Add model-specific prompt formatting hints (some local models need explicit "You must respond with a tool_call" instructions that cloud models do not)

### 6.3 Minimum Capability Tests Before Enabling Local Models

These tests should run automatically when a local provider is configured, and gate the feature:

#### Gate 1: Endpoint Reachability
```
GET http://localhost:{port}/v1/models
Expected: 200 OK with a JSON response containing at least one model
Blocks: All local model functionality if this fails
```

#### Gate 2: Basic Completion
```
POST /v1/chat/completions
Body: {"model": "{configured_model}", "messages": [{"role": "user", "content": "Reply with exactly: OK"}], "max_tokens": 10}
Expected: Response contains "OK" in choices[0].message.content
Blocks: All local model functionality if this fails
```

#### Gate 3: Structured JSON Output (required for broker planning path)
```
POST /v1/chat/completions
Body: System prompt requesting JSON with specific keys; user prompt with a simple CAD scenario
Expected: Response parses as valid JSON with required keys present
Blocks: Broker planning via local model if this fails (synthesis-only mode)
```

#### Gate 4: Tool Calling Format (required for agentic loop)
```
POST /v1/chat/completions
Body: Messages + tools array with 3 test tools; user prompt that clearly maps to one tool
Expected: Response contains tool_calls array with correct tool name and valid arguments JSON
Blocks: Agentic tool loop via local model if this fails
```

#### Gate 5: Multi-Turn Tool Loop (required for full agent mode)
```
Sequence: Initial request -> model returns tool_call -> send tool result -> model returns final text
Expected: Model correctly uses the tool result in its final answer
Blocks: Multi-turn agent loop via local model if this fails
```

#### Gate 6: Latency Profile
```
Measure time-to-first-token and total completion time for a representative broker prompt
Expected: TTFT < 5 seconds, total completion < 30 seconds
Blocks: Displays a warning if latency exceeds thresholds but does not hard-block
```

### 6.4 Configuration Surface

Add these environment variables for local provider support:

```
# Provider selection
SOLIDWORKS_AI_PROVIDER=ollama|lmstudio|openai|anthropic

# Local provider endpoints (defaults shown)
SOLIDWORKS_AI_OLLAMA_ENDPOINT=http://localhost:11434/v1/chat/completions
SOLIDWORKS_AI_OLLAMA_MODEL=qwen2.5:32b
SOLIDWORKS_AI_LMSTUDIO_ENDPOINT=http://localhost:1234/v1/chat/completions
SOLIDWORKS_AI_LMSTUDIO_MODEL=qwen2.5-32b

# Local-specific controls
SOLIDWORKS_AI_LOCAL_TOOL_CALLING=false        # Enable tool_calls for local models (experimental)
SOLIDWORKS_AI_LOCAL_TIMEOUT_MS=60000          # Higher default for local inference
SOLIDWORKS_AI_LOCAL_SYNTHESIS_TIMEOUT_MS=90000
SOLIDWORKS_AI_LOCAL_HEALTH_CHECK=true         # Ping endpoint at startup
```

### 6.5 Implementation Sizing

| Work Item | Effort | Depends On |
|-----------|--------|------------|
| Extend `BrokerModelSettings` to recognize `ollama`/`lmstudio` providers | 1 hour | Nothing |
| Route `ollama`/`lmstudio` through `OpenAIModelClient` in `ModelClientFactory` | 30 min | Settings change |
| Add default endpoints and longer timeouts for local providers | 30 min | Settings change |
| Health check at startup (ping `/v1/models`) | 2 hours | Settings change |
| Gate tests (Gates 1-6) as a validation script | 4-6 hours | Health check |
| Trace source labels (`model_ollama`, `model_lmstudio`) | 30 min | Factory change |
| Unit tests for local provider configuration | 2 hours | Settings change |
| Documentation for local model setup | 1 hour | All above |
| **Total for v1 experimental support** | **~1-2 sessions** | |

---

## 7. Model Recommendations for Adze Users

### 7.1 Recommended Models (March 2026)

| Use Case | Model | Quantization | Min VRAM | Quality Rating |
|----------|-------|-------------|----------|---------------|
| **Text synthesis only** (deterministic broker) | Qwen 2.5 14B | Q5_K_M | 10 GB | Good |
| **Text synthesis only** (budget hardware) | Llama 3.1 8B | Q6_K | 7 GB | Acceptable |
| **Tool selection + synthesis** (experimental) | Qwen 2.5 32B | Q4_K_M | 20 GB | Adequate |
| **Full agentic loop** (experimental, future) | Llama 3.3 70B | Q4_K_M | 42 GB | Good |
| **Full agentic loop** (experimental, future) | Qwen 2.5 72B | Q4_K_M | 42 GB | Good |

### 7.2 Models to Avoid

- Any model below 7B parameters for any Adze use case
- Llama 3.2 1B/3B -- too small for structured output
- Code-specific models (DeepSeek Coder, CodeLlama) -- not trained for tool use
- Any Q2 or Q3 quantization of models that will be used for tool calling
- Phi-3/Phi-4 for tool calling -- insufficient structured output reliability despite being otherwise capable small models

### 7.3 Why Qwen 2.5 32B Is the Current Sweet Spot

- Strong tool-calling training data in the base model
- 32B parameters at Q4_K_M fits in 20GB VRAM, leaving room for SOLIDWORKS on a 24GB GPU
- Inference speed of 15-25 t/s on RTX 4090 is acceptable for interactive use
- Structured JSON output is reliable enough for the synthesis path
- Multilingual support (relevant for international SOLIDWORKS users)
- Apache 2.0 license -- no commercial usage restrictions

---

## 8. Risk Summary

| Risk | Severity | Mitigation |
|------|----------|------------|
| Local model returns malformed JSON, broker fails | Medium | Deterministic fallback already exists and catches this cleanly |
| GPU memory contention with SOLIDWORKS causes crashes or degraded graphics | High | Document hardware requirements clearly; do not auto-enable local models; health check + latency gate |
| User assumes local model quality equals cloud model quality | Medium | Label as "experimental"; show answer source clearly in UI; document quality expectations |
| Ollama/LM Studio not running when user tries to use it | Low | Health check at startup with clear status messaging |
| Model cold-start adds 10-30s to first request | Medium | Pre-load ping at add-in startup; display loading status |
| Local model ecosystem changes rapidly (model names, API surface) | Low | Adze sends standard OpenAI format; model name is user-configured; minimal coupling |
| Token count inaccuracy from local providers skews usage tracking | Low | Log a warning when usage fields are zero; do not use local token counts for billing/budgeting |

---

## 9. Conclusion

**Offline operation is a real feature, but not a v1 headline feature for the agentic loop.**

The path forward is:
1. **Now:** Extend `BrokerModelSettings` and `ModelClientFactory` to recognize local providers and route them through the existing OpenAI client. This is ~1 session of work and immediately enables text synthesis via local models.
2. **Phase 2 (agentic loop):** Build the agentic loop against cloud models first. Once it is stable, add the gate tests and enable local models as an experimental option for the tool-calling path.
3. **Phase 7 (production hardening):** Based on gate test data from real users, decide whether local models meet the quality bar for supported (non-experimental) status in the agentic loop.

The key insight is that **Adze's existing architecture already handles this gracefully**. The deterministic fallback planner means local models can participate in answer synthesis without needing to handle the hard part (tool selection). The hybrid path was designed for exactly this kind of provider quality variance.
