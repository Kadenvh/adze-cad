# Research: Tool-Calling Abstraction Across Providers

**Date:** 2026-03-15
**Status:** Research complete
**Scope:** Normalized comparison of tool-calling wire formats, streaming, lifecycle, and JSON schema handling across OpenAI, Anthropic, OpenRouter, Ollama, and LM Studio -- with a concrete C# abstraction proposal for the Adze .NET Framework 4.8 desktop host.

---

## 1. Provider-by-Provider Tool-Calling Analysis

### 1.1 OpenAI Chat Completions API

**Status:** Production-stable. Tool calling has been the canonical format since November 2023 (replacing the deprecated `functions` parameter).

#### Request Format

Tool definitions go in a top-level `tools` array. Each entry wraps the schema in `{"type": "function", "function": {...}}`:

```json
{
  "model": "gpt-4o",
  "messages": [
    {"role": "system", "content": "..."},
    {"role": "user", "content": "..."}
  ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "Returns dimensions.",
        "parameters": {
          "type": "object",
          "properties": {
            "scope": {"type": "string", "enum": ["selection", "document"]},
            "include_driven": {"type": "boolean"}
          },
          "required": []
        }
      }
    }
  ],
  "tool_choice": "auto"
}
```

**Schema key:** `parameters` (inside the `function` wrapper).
**System prompt:** In the `messages` array as `role: "system"`.

#### Response Format

When the model calls tools, `finish_reason` is `"tool_calls"` and the assistant message carries a `tool_calls` array:

```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": null,
      "tool_calls": [{
        "id": "call_abc123",
        "type": "function",
        "function": {
          "name": "get_dimensions",
          "arguments": "{\"scope\": \"document\"}"
        }
      }]
    },
    "finish_reason": "tool_calls"
  }]
}
```

**Critical detail:** `arguments` is a **JSON string**, not a parsed object. You must deserialize it yourself. This is the single most common source of bugs in OpenAI-format tool-call parsing. The string can contain malformed JSON if the model hallucinates, and it can be truncated if `max_tokens` is hit mid-generation.

**Content field:** Typically `null` when `tool_calls` is present. Some models (especially older GPT-4 Turbo snapshots) occasionally emit both `content` and `tool_calls`. Safe implementations must check for both.

#### Tool Result Format

Each tool result is a **separate message** with `role: "tool"`:

```json
{"role": "tool", "tool_call_id": "call_abc123", "content": "...result json string..."}
```

**Key facts:**
- Dedicated `role: "tool"` (not reusing `role: "user"`).
- One message per tool result. Multiple tools = multiple messages.
- ID field is `tool_call_id`.
- `content` is always a string. No `is_error` flag -- errors are communicated through the content text.
- The full assistant message (including `tool_calls`) must be appended to the conversation before the `tool` messages.

#### tool_choice

| Value | Behavior |
|-------|----------|
| `"auto"` | Model decides (default) |
| `"required"` | Must call at least one tool |
| `"none"` | Must not call tools |
| `{"type": "function", "function": {"name": "..."}}` | Must call this specific tool |

#### Parallel Tool Calls

Enabled by default. Multiple entries in `tool_calls` with different `id` values. Disable with `"parallel_tool_calls": false` at the request level. When parallel calls are returned, each gets its own `role: "tool"` result message.

#### Streaming

SSE format. Tool calls stream in `delta.tool_calls[]`:

```
data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_abc","type":"function","function":{"name":"get_dimensions","arguments":""}}]}}]}
data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"scope"}}]}}]}
data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\":\"doc\"}"}}]}}]}
data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
```

- First chunk has `id`, `name`, empty `arguments`.
- Subsequent chunks append to `arguments` for the tool call at `index`.
- For parallel calls, `index` differentiates them.
- Must buffer and concatenate `arguments` fragments, then parse the assembled JSON.
- `data: [DONE]` terminates the stream.

#### Usage Reporting

```json
{"usage": {"prompt_tokens": 345, "completion_tokens": 42, "total_tokens": 387}}
```

Present in non-streaming responses. In streaming, usage appears in the final chunk (since ~late 2024) if `stream_options: {"include_usage": true}` is set.

#### Quirks and Gotchas

1. **`arguments` is a string, not an object.** This is the most important difference from Anthropic. Forgetting to deserialize it separately is a common bug.
2. **`content` can be `null` or a string.** When `tool_calls` is present, `content` is usually `null`, but not guaranteed.
3. **`finish_reason` is `"tool_calls"` (plural),** not `"tool_call"`.
4. **Token counting includes tool definitions.** Tool schemas consume input tokens. 10 tools with descriptions can consume 500-1500 tokens.
5. **No built-in `is_error` flag on tool results.** If a tool fails, you describe the error in the `content` string. The model handles it fine in practice.
6. **`max_tokens` can truncate tool call arguments.** If `max_tokens` is too low, the model may emit a `tool_calls` array with incomplete `arguments` JSON. Always validate the JSON after assembling it.

---

### 1.2 Anthropic Messages API

**Status:** Production-stable. Tool use has been GA since the `2023-06-01` API version and has seen only additive changes.

#### Request Format

Tools go in a top-level `tools` array. No `function` wrapper -- each tool is a flat object:

```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 1024,
  "system": "You are a SOLIDWORKS assistant.",
  "tools": [
    {
      "name": "get_dimensions",
      "description": "Returns dimensions.",
      "input_schema": {
        "type": "object",
        "properties": {
          "scope": {"type": "string", "enum": ["selection", "document"]},
          "include_driven": {"type": "boolean"}
        },
        "required": []
      }
    }
  ],
  "messages": [
    {"role": "user", "content": "What are the dimensions?"}
  ]
}
```

**Schema key:** `input_schema` (not `parameters`).
**System prompt:** Top-level `system` field, not in `messages`.
**Required headers:** `x-api-key`, `anthropic-version: 2023-06-01`, `content-type: application/json`.

#### Response Format

When the model calls tools, `stop_reason` is `"tool_use"` and the `content` array contains `tool_use` blocks:

```json
{
  "id": "msg_01XFD...",
  "role": "assistant",
  "stop_reason": "tool_use",
  "content": [
    {"type": "text", "text": "Let me check the dimensions."},
    {
      "type": "tool_use",
      "id": "toolu_01A09...",
      "name": "get_dimensions",
      "input": {"scope": "document"}
    }
  ]
}
```

**Critical detail:** `input` is a **parsed JSON object**, not a string. This is the opposite of OpenAI. No extra deserialization step needed.

**Content array:** Can contain both `text` and `tool_use` blocks in the same response. The text block is the model's "thinking aloud" before calling tools. Both must be preserved when echoing the assistant message back.

#### Tool Result Format

Tool results go in a `user` message as `tool_result` content blocks:

```json
{
  "role": "user",
  "content": [
    {
      "type": "tool_result",
      "tool_use_id": "toolu_01A09...",
      "content": "...result json string..."
    }
  ]
}
```

**Key facts:**
- Results use `role: "user"` (not a dedicated role).
- Multiple tool results go as multiple `tool_result` blocks in a **single** `user` message.
- ID field is `tool_use_id` (not `tool_call_id`).
- `content` inside `tool_result` can be a string or an array of content blocks (text, image).
- Supports `"is_error": true` for explicit error signaling.
- The full assistant message (including all content blocks) must be appended before the `user` tool-result message.

#### tool_choice

```json
"tool_choice": {"type": "auto"}
```

| Value | Behavior |
|-------|----------|
| `{"type": "auto"}` | Model decides (default) |
| `{"type": "any"}` | Must use at least one tool |
| `{"type": "tool", "name": "..."}` | Must use this specific tool |

Note the structural difference: Anthropic uses `{"type": "any"}` where OpenAI uses `"required"`.

#### Parallel Tool Use

The model can return multiple `tool_use` blocks in one response. Return all `tool_result` blocks in one `user` message. Disable with:

```json
"tool_choice": {"type": "auto", "disable_parallel_tool_use": true}
```

#### Streaming

SSE format with named event types:

```
event: content_block_start
data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01A","name":"get_dimensions","input":{}}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"scope\""}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":":\"document\"}"}}

event: content_block_stop
data: {"type":"content_block_stop","index":1}

event: message_delta
data: {"type":"message_delta","delta":{"stop_reason":"tool_use"}}
```

**Key differences from OpenAI streaming:**
- Uses named `event:` lines (OpenAI omits the `event:` field and uses only `data:` lines).
- Tool input arrives as `input_json_delta` fragments (not embedded in a `delta.tool_calls` array).
- `content_block_start` announces the tool name and ID upfront; only `partial_json` needs buffering.
- `content_block_stop` signals that the full input JSON for one block is complete.
- `message_delta` carries the `stop_reason`.

#### Usage Reporting

```json
{"usage": {"input_tokens": 345, "output_tokens": 87}}
```

Field names differ from OpenAI: `input_tokens` / `output_tokens` (not `prompt_tokens` / `completion_tokens`). No `total_tokens` field -- compute it yourself.

#### Quirks and Gotchas

1. **`input` is an object, not a string.** Opposite of OpenAI. If you unify parsing, the OpenAI side needs an extra `JSON.parse()` step.
2. **`content` is always an array of blocks.** Even for plain text. Never assume it is a string.
3. **`stop_reason` is `"tool_use"` (singular),** not `"tool_calls"`.
4. **Tool results go in `role: "user"` messages.** This is semantically odd but architecturally intentional -- the API enforces strict user/assistant alternation.
5. **`system` is not a message.** It is a top-level field. If you build a unified message list, the system prompt needs special handling for Anthropic.
6. **`is_error` flag exists.** Unlike OpenAI, Anthropic has an explicit error-signaling mechanism on tool results.
7. **`max_tokens` is required.** OpenAI defaults it; Anthropic requires it explicitly.
8. **The `anthropic-version` header is mandatory.** Without it, the API returns a 400.

---

### 1.3 OpenRouter

**Status:** Production-stable aggregator. Uses the **OpenAI-compatible format** for all providers, translating transparently.

**Endpoint:** `https://openrouter.ai/api/v1/chat/completions`
**Auth:** `Authorization: Bearer <OPENROUTER_API_KEY>`

#### Format

Identical to OpenAI Chat Completions. Tool definitions use the `{"type": "function", "function": {...}}` wrapper, `parameters` for schemas, `tool_calls` in responses, `role: "tool"` for results.

```json
{
  "model": "anthropic/claude-sonnet-4-20250514",
  "messages": [...],
  "tools": [{"type": "function", "function": {"name": "...", "parameters": {...}}}]
}
```

#### Provider Coverage for Tool Calling

OpenRouter translates tool calling for models that natively support it:
- **Anthropic Claude** models (3.5 Sonnet, 3.5 Haiku, Opus, Sonnet 4, Haiku 4, etc.) -- reliable
- **OpenAI GPT-4o, GPT-4.1 series, GPT-4 Turbo** -- reliable (native pass-through)
- **Google Gemini 1.5, 2.0** models -- generally works; occasional schema strictness differences
- **Mistral Large, Medium** with function calling -- works but some models are less reliable
- **Meta Llama 3.1/3.2/3.3** -- works for instruction-tuned variants that support tool calling; quality varies
- **DeepSeek** models -- supported but tool-calling quality is inconsistent

#### Streaming

Same SSE format as OpenAI. OpenRouter transparently translates Anthropic's event-based SSE to OpenAI's delta-based format.

#### Quirks and Gotchas

1. **Latency overhead.** Extra hop through OpenRouter adds 50-200ms per request.
2. **Model naming.** Uses `provider/model-name` format: `anthropic/claude-sonnet-4-20250514`, `openai/gpt-4o`.
3. **`arguments` is always a JSON string,** even when the underlying model is Anthropic (OpenRouter translates Anthropic's `input` object into OpenAI's `arguments` string).
4. **Error format.** OpenRouter error responses generally follow the OpenAI format, but provider-specific errors may leak through with different structures.
5. **Rate limiting.** OpenRouter has its own rate limits layered on top of provider limits. 429 responses may come from OpenRouter or the underlying provider.
6. **Tool calling support is model-dependent.** Not all models available through OpenRouter support tool calling. If you send `tools` to a model that does not support them, you may get an error or the tools may be silently ignored.
7. **`HTTP-Referer` and `X-Title` headers.** OpenRouter encourages (but does not require) these for analytics and leaderboard tracking. Not relevant for tool calling but good practice.
8. **OpenRouter-specific response fields.** Responses may include `x-ratelimit-*` headers and an `id` field with an OpenRouter-specific prefix.
9. **Streaming `usage` availability.** Depends on the underlying provider. Not always present in streaming responses.

#### Implication for Adze

OpenRouter can be implemented as a thin configuration variant of the OpenAI client. The only differences are: the endpoint URL, the API key source, and the model naming convention. The wire format is identical.

---

### 1.4 Ollama

**Status:** Tool calling is supported in Ollama since v0.3.0 (mid-2024) using the OpenAI-compatible API endpoint. Quality depends heavily on the specific model.

**Endpoint:** `http://localhost:11434/v1/chat/completions` (OpenAI-compatible) or `http://localhost:11434/api/chat` (native Ollama format).

#### OpenAI-Compatible Endpoint (`/v1/chat/completions`)

Ollama implements the OpenAI Chat Completions API format for tool calling. The request format is identical to OpenAI:

```json
{
  "model": "llama3.1:8b",
  "messages": [
    {"role": "system", "content": "..."},
    {"role": "user", "content": "..."}
  ],
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

Response format follows OpenAI conventions:

```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": "",
      "tool_calls": [{
        "id": "call_...",
        "type": "function",
        "function": {
          "name": "get_dimensions",
          "arguments": "{\"scope\": \"document\"}"
        }
      }]
    },
    "finish_reason": "tool_calls"
  }]
}
```

Tool results use `role: "tool"` with `tool_call_id`, same as OpenAI.

#### Native Ollama Endpoint (`/api/chat`)

Ollama also has a native chat endpoint that supports tools in a slightly different format:

```json
{
  "model": "llama3.1:8b",
  "messages": [{"role": "user", "content": "..."}],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": {"type": "object", "properties": {...}}
      }
    }
  ],
  "stream": false
}
```

Response format is slightly different from the OpenAI-compatible endpoint:

```json
{
  "model": "llama3.1:8b",
  "message": {
    "role": "assistant",
    "content": "",
    "tool_calls": [{
      "function": {
        "name": "get_dimensions",
        "arguments": {"scope": "document"}
      }
    }]
  },
  "done": true,
  "done_reason": "stop"
}
```

**Key difference:** In the native endpoint, `arguments` can be a **parsed object** (not a string). Also, `tool_calls` entries may lack an `id` field in some Ollama versions, and the response is not wrapped in `choices[]`.

#### Streaming

The native `/api/chat` endpoint streams NDJSON (newline-delimited JSON), not SSE:

```
{"model":"llama3.1:8b","message":{"role":"assistant","content":""},"done":false}
{"model":"llama3.1:8b","message":{"role":"assistant","content":"","tool_calls":[...]},"done":true}
```

The `/v1/chat/completions` endpoint uses SSE format matching OpenAI conventions.

**Important:** Ollama disables streaming when tools are present in the request (for the native endpoint). The tool calls are returned in a single non-streaming response even when `"stream": true` is specified. The OpenAI-compatible endpoint may stream text but delivers tool calls atomically in some versions.

#### Model Support

Tool calling quality varies dramatically by model:

| Model | Tool Calling Quality | Notes |
|-------|---------------------|-------|
| `llama3.1:8b/70b` | Moderate | Sometimes calls wrong tool, argument schemas loosely followed |
| `llama3.2:3b` | Poor | Frequent hallucinated tool names, malformed arguments |
| `llama3.3:70b` | Good | Reliable tool selection, mostly correct arguments |
| `qwen2.5:7b/72b` | Good | Solid tool calling, respects schemas well |
| `mistral:7b` | Poor-Moderate | Inconsistent, sometimes embeds tool calls in text instead of structured output |
| `mixtral:8x7b` | Moderate | Works but occasionally produces invalid JSON in arguments |
| `command-r` (Cohere) | Good | Designed for tool calling, reliable |
| `deepseek-r1` | Poor | Does not reliably use tool-calling format |
| `phi3:medium` | Poor-Moderate | Sometimes works but often falls back to text descriptions |
| `gemma2:9b/27b` | Poor | Limited tool-calling capability |

#### Quirks and Gotchas

1. **No authentication by default.** Ollama runs locally with no API key. Auth depends on reverse proxy setup.
2. **`tool_calls[].id` may be missing or auto-generated.** Ollama did not always generate stable tool call IDs. Newer versions (0.5+) generate them for the OpenAI-compatible endpoint, but older versions or the native endpoint may omit them. You should generate a fallback ID if missing.
3. **`arguments` format is inconsistent.** The OpenAI-compatible endpoint returns `arguments` as a JSON string (matching OpenAI). The native endpoint may return it as a parsed object. Handle both.
4. **`finish_reason` vs. `done_reason`.** The OpenAI-compatible endpoint uses `finish_reason`. The native endpoint uses `done_reason` with value `"stop"` (not `"tool_calls"`). In the native endpoint, check for the presence of `tool_calls` in the message rather than relying on the done reason.
5. **Streaming is effectively disabled for tool calls.** Ollama buffers the entire response when tools are involved, making streaming a no-op for tool-calling turns.
6. **Schema validation is non-existent.** Ollama does not validate tool arguments against the provided JSON schema. The model may return arguments that violate `required`, `enum`, `type`, or `minimum`/`maximum` constraints. All argument validation must happen client-side.
7. **Parallel tool calls.** Supported in format but model-dependent. Most local models do not reliably produce multiple tool calls in a single response.
8. **`tool_choice` is not always respected.** Ollama accepts the parameter but whether the underlying model honors it depends on the model's fine-tuning.
9. **Token usage reporting is limited.** The native endpoint reports `prompt_eval_count` and `eval_count` (not OpenAI's field names). The OpenAI-compatible endpoint maps these to `prompt_tokens` / `completion_tokens`.
10. **Model hot-loading latency.** The first request to a model that is not loaded adds significant latency (seconds to tens of seconds for model loading). This is not tool-calling-specific but affects timeout configuration.

#### Recommendation for Adze

Use the **OpenAI-compatible endpoint** (`/v1/chat/completions`). This allows Ollama to share the same client implementation as OpenAI and OpenRouter. Add defensive handling for missing `tool_calls[].id` fields and malformed `arguments`. Set generous timeouts to account for model loading.

---

### 1.5 LM Studio

**Status:** Tool calling is supported in LM Studio since version 0.3.0 (late 2024) via its OpenAI-compatible API server. Quality depends on the loaded model.

**Endpoint:** `http://localhost:1234/v1/chat/completions`

#### Format

LM Studio implements the OpenAI Chat Completions API format. Tool definitions, tool calls, and tool results all match the OpenAI wire format:

```json
{
  "model": "loaded-model-identifier",
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

Responses follow the OpenAI `choices[].message.tool_calls` format with `arguments` as a JSON string.

#### Streaming

SSE format matching OpenAI conventions. Same buffering requirements as OpenAI for tool call arguments.

However, like Ollama, the actual streaming behavior during tool-calling turns is model-dependent. Some quantized models may not produce well-formed streaming tool-call deltas.

#### Model Support

LM Studio supports any GGUF model that the user loads. Tool-calling quality depends entirely on the model:

| Model Type | Tool Calling Quality | Notes |
|------------|---------------------|-------|
| Llama 3.1/3.3 instruct variants | Moderate-Good | See Ollama notes; same models, same behavior |
| Qwen 2.5 instruct variants | Good | Reliable tool calling |
| Mistral instruct | Moderate | Inconsistent |
| Smaller models (<7B) | Poor | Generally unreliable for structured tool calling |
| Non-instruct/base models | Not supported | No tool calling capability |

#### Quirks and Gotchas

1. **Model identifier is whatever the user loaded.** Unlike cloud providers with fixed model names, the `model` field is the local filename or a user-defined alias. Adze cannot assume a specific model name.
2. **No authentication by default.** Like Ollama, runs locally without API keys.
3. **`tool_calls[].id` generation.** LM Studio generates IDs in the OpenAI `call_*` format, but the reliability depends on the version. Older versions may produce non-unique or missing IDs.
4. **`tool_choice` support.** LM Studio accepts the parameter but enforcement depends on the loaded model. `"required"` mode may not reliably force tool use with all models.
5. **Schema strictness.** Like Ollama, schema validation is model-dependent, not server-enforced. Client-side validation is necessary.
6. **Context window limits.** Local models often have smaller context windows (4K-32K vs 128K+ for cloud models). Tool definitions and conversation history consume context. More aggressive truncation may be needed.
7. **Quantization effects.** Heavily quantized models (Q2, Q3) are significantly worse at structured output like tool calling than Q5/Q6/Q8/FP16 variants.
8. **Server startup latency.** LM Studio's API server takes time to load a model. First requests may time out.
9. **Concurrent request handling.** LM Studio typically handles one request at a time. If the agent loop sends a second request before the first completes (unlikely but possible with timeouts), it may queue or reject it.
10. **No usage reporting in some versions.** `usage` may be missing or incomplete in LM Studio responses. Handle absent `usage` gracefully.

#### Recommendation for Adze

Same as Ollama: use the OpenAI-compatible endpoint and share the OpenAI client implementation. Add defensive checks for missing fields and set model-loading-aware timeouts. Consider allowing the user to configure the model name and context window size since LM Studio does not expose these programmatically.

---

## 2. Normalized Comparison Matrix

| Aspect | OpenAI | Anthropic | OpenRouter | Ollama | LM Studio |
|--------|--------|-----------|------------|--------|-----------|
| **Endpoint format** | OpenAI Chat Completions | Anthropic Messages | OpenAI-compatible | OpenAI-compatible or native | OpenAI-compatible |
| **Auth mechanism** | `Authorization: Bearer` | `x-api-key` header | `Authorization: Bearer` | None (default) | None (default) |
| **Tool def wrapper** | `{"type":"function","function":{...}}` | Flat `{name, description, input_schema}` | Same as OpenAI | Same as OpenAI | Same as OpenAI |
| **Schema key name** | `parameters` | `input_schema` | `parameters` | `parameters` | `parameters` |
| **System prompt** | In `messages` as `role:"system"` | Top-level `system` field | In `messages` | In `messages` | In `messages` |
| **Tool call location** | `message.tool_calls[]` | `content[]` blocks `type:"tool_use"` | Same as OpenAI | Same as OpenAI | Same as OpenAI |
| **Arguments format** | JSON **string** | JSON **object** | JSON **string** | JSON **string** (compat) or object (native) | JSON **string** |
| **Stop signal (tools)** | `finish_reason:"tool_calls"` | `stop_reason:"tool_use"` | Same as OpenAI | Same as OpenAI (compat) | Same as OpenAI |
| **Stop signal (done)** | `finish_reason:"stop"` | `stop_reason:"end_turn"` | Same as OpenAI | Same as OpenAI (compat) | Same as OpenAI |
| **Tool result role** | `role:"tool"` | `role:"user"` + `tool_result` blocks | Same as OpenAI | Same as OpenAI | Same as OpenAI |
| **Result ID field** | `tool_call_id` | `tool_use_id` | `tool_call_id` | `tool_call_id` | `tool_call_id` |
| **Multiple results** | Separate `tool` messages | Array of blocks in one `user` message | Separate messages | Separate messages | Separate messages |
| **Error flag** | None (text in content) | `is_error: true` | None | None | None |
| **Parallel tool calls** | Default on; `parallel_tool_calls:false` | Default on; `disable_parallel_tool_use` | Same as OpenAI | Format-supported, model-dependent | Format-supported, model-dependent |
| **Streaming format** | SSE `data:` lines | SSE `event:` + `data:` lines | Same as OpenAI | SSE (compat) or NDJSON (native) | SSE |
| **Streaming tool calls** | Delta-based argument buffering | `input_json_delta` fragments | Same as OpenAI | Effectively non-streaming | Model-dependent |
| **Usage field names** | `prompt_tokens`, `completion_tokens` | `input_tokens`, `output_tokens` | Same as OpenAI | Same as OpenAI (compat) | Same as OpenAI or absent |
| **max_tokens** | Optional (has default) | **Required** | Optional | Optional | Optional |
| **Schema validation** | Server-side (partial) | Server-side (partial) | Provider-dependent | None (model-dependent) | None (model-dependent) |
| **Tool call IDs** | Always present (`call_*`) | Always present (`toolu_*`) | Always present | May be missing | May be missing |

---

## 3. JSON Schema Compatibility

All five providers accept JSON Schema for tool parameter definitions, but with different levels of support:

### 3.1 Common Supported Schema Features

These work reliably across all providers:

```json
{
  "type": "object",
  "properties": {
    "name": {"type": "string", "description": "..."},
    "count": {"type": "integer"},
    "enabled": {"type": "boolean"},
    "mode": {"type": "string", "enum": ["a", "b", "c"]}
  },
  "required": ["name"]
}
```

### 3.2 Schema Features with Inconsistent Support

| Feature | OpenAI | Anthropic | Local Models |
|---------|--------|-----------|--------------|
| `type: ["string", "null"]` (union types) | Supported | Supported | Often ignored by model |
| `default` values | Accepted, sometimes used | Accepted, sometimes used | Usually ignored |
| `minimum` / `maximum` on integers | Accepted, rarely enforced by model | Accepted, rarely enforced | Ignored |
| `additionalProperties: false` | Supported (strict mode) | Accepted but not enforced | Ignored |
| `$ref` / `$defs` | Not supported | Not supported | Not supported |
| Nested objects | Supported | Supported | Model-dependent; simpler is better |
| Arrays of objects | Supported | Supported | Fragile with local models |
| `oneOf` / `anyOf` | Partial support | Partial support | Unreliable |

### 3.3 OpenAI Strict Mode

OpenAI added a `"strict": true` option on tool definitions that enforces the schema on the model's output. When enabled:
- The model is guaranteed to produce JSON matching the schema
- `additionalProperties: false` must be set on all objects
- All properties must be listed in `required` (use `type: ["string", "null"]` for optional properties)
- No `$ref` or external references

This is the strongest schema enforcement available from any provider. Anthropic and local models do not offer an equivalent guarantee.

### 3.4 Safe Schema Subset for Adze

Given the Adze tool parameter types (`GetDimensionsParameters`, `GetFeatureTreeSliceParameters`, etc.), the safe subset that works across all providers is:

```json
{
  "type": "object",
  "properties": {
    "simple_string": {"type": "string", "description": "..."},
    "simple_int": {"type": "integer", "description": "..."},
    "simple_bool": {"type": "boolean", "description": "..."},
    "enum_string": {"type": "string", "enum": ["a", "b"], "description": "..."}
  },
  "required": []
}
```

All 10 current Adze tools use only these types. No nested objects, no arrays, no union types. This is a significant advantage for cross-provider compatibility.

---

## 4. Streaming Behavior Deep Dive

### 4.1 SSE Parsing on .NET Framework 4.8

Neither `HttpWebRequest` nor `WebClient` has built-in SSE support. Implementing SSE requires reading the response stream line-by-line:

```csharp
using (var response = (HttpWebResponse)request.GetResponse())
using (var stream = response.GetResponseStream())
using (var reader = new StreamReader(stream, Encoding.UTF8))
{
    string line;
    while ((line = reader.ReadLine()) != null)
    {
        if (line.StartsWith("data: "))
        {
            string json = line.Substring(6);
            if (json == "[DONE]") break;
            // Parse and accumulate
        }
    }
}
```

This works but has complications:
- `HttpWebRequest.Timeout` applies to the initial response, not to the stream reading. A long streaming response may hang.
- `ReadWriteTimeout` controls individual read operations but is not a total timeout.
- No built-in cancellation. Must use `request.Abort()` from another thread.

### 4.2 Tool-Call Streaming is Not Worth It Initially

For tool-calling turns, streaming provides no user-visible benefit:
- You cannot execute the tool until the full arguments JSON is assembled.
- Tool execution happens locally and is fast (milliseconds for the Adze read-only tools).
- The only benefit would be displaying the model's "thinking" text before the tool call, which is minor.

For the final answer turn, streaming provides real value (perceived responsiveness).

### 4.3 Recommendation

**Phase 1:** Non-streaming for all turns. Simpler, debuggable, and the total round-trip for a tool-calling turn is dominated by model inference time anyway.

**Phase 2:** Streaming for the final answer turn only. Use `"stream": true` when the request does not include tool definitions (the synthesis pass) or when the model is producing the terminal response.

**Phase 3:** Full streaming with tool-call buffering. Only if UX requirements demand real-time "thinking" text display during tool-calling turns.

---

## 5. Tool-Call Lifecycle Comparison

### 5.1 OpenAI-Format Lifecycle (OpenAI, OpenRouter, Ollama, LM Studio)

```
1. Client sends: messages + tools
2. Model returns: assistant message with tool_calls[] (finish_reason: "tool_calls")
3. Client appends: full assistant message to conversation
4. Client executes: each tool_call, builds results
5. Client appends: one "role":"tool" message per tool result
6. GOTO 1 (send full conversation back)
7. Eventually: model returns assistant message with content (finish_reason: "stop")
```

### 5.2 Anthropic-Format Lifecycle

```
1. Client sends: system + messages + tools
2. Model returns: content[] with text + tool_use blocks (stop_reason: "tool_use")
3. Client appends: full assistant message (all content blocks) to messages
4. Client executes: each tool_use block, builds results
5. Client appends: one "role":"user" message with tool_result[] blocks
6. GOTO 1 (send full conversation back)
7. Eventually: model returns content[] with text only (stop_reason: "end_turn")
```

### 5.3 Key Lifecycle Differences

| Step | OpenAI-Format | Anthropic |
|------|--------------|-----------|
| Echo assistant message | Must include `tool_calls` array | Must include full `content[]` with all blocks |
| Add tool results | One separate `role:"tool"` message per result | One `role:"user"` message with multiple `tool_result` blocks |
| Stop detection | Check `finish_reason == "stop"` | Check `stop_reason == "end_turn"` |
| Tool call detection | Check `finish_reason == "tool_calls"` or presence of `tool_calls` | Check `stop_reason == "tool_use"` or presence of `tool_use` blocks |
| Error signaling | Error text in `content` string | `is_error: true` flag available |

---

## 6. Conversation State Handling

### 6.1 Message Roles

| Role | OpenAI-Format | Anthropic |
|------|--------------|-----------|
| System instruction | `{"role":"system","content":"..."}` in messages | Top-level `"system":"..."` field |
| User message | `{"role":"user","content":"..."}` | `{"role":"user","content":"..."}` or `{"role":"user","content":[blocks]}` |
| Assistant text | `{"role":"assistant","content":"..."}` | `{"role":"assistant","content":[{"type":"text","text":"..."}]}` |
| Assistant tool call | `{"role":"assistant","tool_calls":[...]}` | `{"role":"assistant","content":[{"type":"tool_use",...}]}` |
| Tool result | `{"role":"tool","tool_call_id":"...","content":"..."}` | `{"role":"user","content":[{"type":"tool_result",...}]}` |

### 6.2 Content Block Types

**OpenAI-format:**
- `content` is a string (or null) for assistant messages
- `content` is a string for tool result messages
- No content block types -- content is always flat text

**Anthropic:**
- `content` is always an array of typed blocks
- Block types: `text`, `tool_use`, `tool_result`, `image` (for input)
- Mixed blocks: a single assistant response can contain multiple `text` and `tool_use` blocks interleaved

### 6.3 Multi-Turn Conversation Pattern

The provider-agnostic internal representation must support these patterns:

```
Turn 1: user -> assistant (text only)                    [no tools needed]
Turn 2: user -> assistant (tool calls) -> tool results   [one tool-call round]
Turn 3: user -> assistant (tool calls) -> tool results -> assistant (tool calls) -> tool results -> assistant (text)
         [two tool-call rounds before final answer]
```

The internal `ConversationMessage` must be rich enough to serialize to either format. The key design decision: **store messages in a provider-neutral internal format and serialize to the wire format at the provider boundary.**

---

## 7. Practical Abstraction Boundary

### 7.1 What Can Be Unified

1. **Tool definitions.** A single internal `AgentToolDefinition` (name, description, JSON schema as `Dictionary<string, object>`) can be serialized to either format by the provider client.

2. **Tool call requests.** A `ToolCallRequest` with `Id`, `Name`, and `InputJson` (string) works for both. Anthropic's parsed `input` object gets serialized to a string for internal use; OpenAI's `arguments` string is used as-is.

3. **Tool call results.** A `ToolCallResult` with `ToolCallId`, `OutputJson`, and `IsError` covers both formats. OpenAI ignores `IsError` (error goes in `OutputJson`); Anthropic maps it to `is_error`.

4. **Stop reason.** Normalize to an enum: `ToolUse`, `EndTurn`, `MaxTokens`, `Error`.

5. **Usage tracking.** A `ModelUsage` with `InputTokens`, `OutputTokens`, `TotalTokens` covers both. Already implemented.

6. **The agent loop itself.** The loop logic (send, check stop reason, execute tools, append results, repeat) is identical across all providers.

### 7.2 What Cannot Be Unified

1. **Request body construction.** Anthropic's system prompt, tool definition key names, and content block format differ fundamentally from OpenAI's. Each provider client must build its own request body.

2. **Response parsing.** Anthropic's content-block-based response structure vs. OpenAI's `choices[].message` structure require different parsing code. The `arguments` (string) vs. `input` (object) difference is particularly load-bearing.

3. **Tool result message construction.** Anthropic packs multiple results into one `user` message with `tool_result` blocks. OpenAI uses separate `role:"tool"` messages. This affects how conversation history is built.

4. **Streaming event formats.** Anthropic's named events vs. OpenAI's `data:`-only format require different SSE parsers.

5. **Authentication.** Header name and format differ (`Authorization: Bearer` vs. `x-api-key`).

### 7.3 The Right Boundary

The abstraction boundary should be at the **agent model client** level: each provider implements a method that takes a normalized conversation state and tool definitions, makes the API call, and returns a normalized response. The loop runner never knows which provider it is talking to.

```
AgentLoopRunner (provider-agnostic)
  |
  +-- calls IAgentModelClient.SendTurn(...)
  |     |
  |     +-- OpenAIAgentClient          (builds OpenAI request, parses OpenAI response)
  |     +-- AnthropicAgentClient       (builds Anthropic request, parses Anthropic response)
  |     +-- OllamaAgentClient          (extends OpenAI with defensive checks)
  |     +-- LMStudioAgentClient        (extends OpenAI with defensive checks)
  |     +-- OpenRouterAgentClient      (extends OpenAI with model naming)
  |
  +-- calls IToolExecutor.Execute(...)  (executes tools, provider-agnostic)
```

In practice, Ollama, LM Studio, and OpenRouter all use the OpenAI format, so they can share a base implementation with configuration overrides. The real split is **two** client implementations: Anthropic and OpenAI-format.

---

## 8. C# Interface Proposal

### 8.1 Core Abstractions

```csharp
namespace Adze.Broker.Abstractions;

/// <summary>
/// Normalized stop reason across all providers.
/// </summary>
public enum AgentStopReason
{
    /// <summary>Model produced a final text answer.</summary>
    EndTurn,

    /// <summary>Model requested one or more tool calls.</summary>
    ToolUse,

    /// <summary>Response was truncated by max_tokens limit.</summary>
    MaxTokens,

    /// <summary>An error occurred during the API call.</summary>
    Error
}

/// <summary>
/// Provider-agnostic tool definition for the agent loop.
/// Each provider client serializes this into its wire format.
/// </summary>
public sealed class AgentToolDefinition
{
    /// <summary>Tool name matching ToolNames constants.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description for the model.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema for the tool's input parameters, stored as a
    /// nested dictionary tree. Serialized to "input_schema" (Anthropic)
    /// or "parameters" (OpenAI) by the provider client.
    /// </summary>
    public Dictionary<string, object?> ParameterSchema { get; set; } = new();
}

/// <summary>
/// A tool call request extracted from the model's response.
/// Normalized across providers.
/// </summary>
public sealed class AgentToolCall
{
    /// <summary>
    /// Provider-assigned ID for this tool call.
    /// "toolu_*" for Anthropic, "call_*" for OpenAI-format providers.
    /// May be auto-generated for local providers that omit it.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Tool name the model wants to invoke.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Deserialized tool input arguments. Always a dictionary,
    /// regardless of whether the provider sent it as a JSON string
    /// (OpenAI) or a parsed object (Anthropic). The provider client
    /// handles this normalization.
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <summary>
    /// Raw arguments JSON string, preserved for logging and
    /// for re-serialization when echoing the assistant message.
    /// </summary>
    public string ArgumentsJson { get; set; } = string.Empty;
}

/// <summary>
/// Result of executing a tool, to be sent back to the model.
/// </summary>
public sealed class AgentToolResult
{
    /// <summary>The tool call ID this result corresponds to.</summary>
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>Tool name (for logging and dispatch).</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Serialized tool output as a JSON string.</summary>
    public string OutputJson { get; set; } = string.Empty;

    /// <summary>
    /// Whether the tool execution failed. Anthropic maps this to
    /// is_error on the tool_result block. OpenAI-format providers
    /// embed the error in OutputJson (no dedicated flag).
    /// </summary>
    public bool IsError { get; set; }
}

/// <summary>
/// Normalized response from a single model turn in the agent loop.
/// </summary>
public sealed class AgentTurnResponse
{
    /// <summary>Whether the API call succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Normalized stop reason.</summary>
    public AgentStopReason StopReason { get; set; }

    /// <summary>
    /// Text content from the model's response. Present on EndTurn
    /// responses (the final answer) and optionally on ToolUse
    /// responses (the model's "thinking" text before tool calls).
    /// </summary>
    public string TextContent { get; set; } = string.Empty;

    /// <summary>
    /// Tool calls requested by the model. Non-null and non-empty
    /// when StopReason is ToolUse.
    /// </summary>
    public List<AgentToolCall> ToolCalls { get; set; } = new();

    /// <summary>Token usage for this turn.</summary>
    public ModelUsage Usage { get; set; } = new();

    /// <summary>Error description when Success is false.</summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>Provider identifier (for tracing).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model identifier (for tracing).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Raw assistant message object, preserved in provider-specific
    /// format for echoing back in the next request. The provider
    /// client stores this opaquely and uses it when building the
    /// next request's message history.
    /// </summary>
    public object? RawAssistantMessage { get; set; }
}
```

### 8.2 Agent Model Client Interface

```csharp
namespace Adze.Broker.Abstractions;

/// <summary>
/// Model client that supports the tool-use conversation protocol.
/// Each provider implements this interface to handle its wire format.
///
/// This is separate from IModelClient (which handles the existing
/// single-turn broker/synthesis paths) to avoid breaking changes.
/// The two interfaces may be unified in a future refactor.
/// </summary>
public interface IAgentModelClient
{
    /// <summary>
    /// Sends a single turn of the agent conversation to the model.
    /// Builds the provider-specific request from the normalized
    /// conversation state and parses the response into a normalized
    /// AgentTurnResponse.
    /// </summary>
    /// <param name="systemPrompt">System instructions for the model.</param>
    /// <param name="conversationHistory">
    ///   Ordered list of prior turns. Each entry is an opaque object
    ///   that was previously returned as RawAssistantMessage or built
    ///   by the client's tool-result formatting method.
    ///   The provider client knows how to serialize these.
    /// </param>
    /// <param name="toolDefinitions">
    ///   Available tools the model may call. Empty list disables tool use.
    /// </param>
    /// <param name="settings">
    ///   Model settings (max_tokens, temperature, timeout, etc.).
    /// </param>
    /// <returns>Normalized response from this turn.</returns>
    AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings);

    /// <summary>
    /// Builds the initial user message in provider-specific format
    /// for insertion into the conversation history.
    /// </summary>
    object BuildUserMessage(string content);

    /// <summary>
    /// Builds tool result message(s) in provider-specific format
    /// for insertion into the conversation history.
    ///
    /// Returns a list because OpenAI-format requires one message per
    /// tool result, while Anthropic packs all results into one message.
    /// The caller appends all returned objects to the conversation.
    /// </summary>
    List<object> BuildToolResultMessages(List<AgentToolResult> results);
}
```

### 8.3 Agent Model Settings

```csharp
namespace Adze.Broker.Configuration;

/// <summary>
/// Configuration for agent loop model calls.
/// Extends the existing BrokerModelSettings pattern.
/// </summary>
public sealed class AgentModelSettings
{
    /// <summary>Max tokens for each individual agent turn.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>HTTP timeout per turn in milliseconds.</summary>
    public int TimeoutMilliseconds { get; set; } = 30000;

    /// <summary>Temperature for model generation.</summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Maximum number of tool-calling iterations before forcing
    /// a final answer or falling back to deterministic synthesis.
    /// </summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// Maximum consecutive API errors before aborting the loop.
    /// Tool execution errors (sent back to the model) do not count.
    /// </summary>
    public int MaxConsecutiveErrors { get; set; } = 2;

    /// <summary>
    /// Maximum total tokens (input + output accumulated across all
    /// turns) before forcing loop termination. 0 = no limit.
    /// </summary>
    public int MaxTotalTokens { get; set; } = 100000;

    /// <summary>
    /// Maximum size in characters of a single tool result before
    /// truncation. Prevents context window overflow from large
    /// feature trees or property sets.
    /// </summary>
    public int MaxToolResultChars { get; set; } = 8192;

    /// <summary>Whether to disable parallel tool calls.</summary>
    public bool DisableParallelToolCalls { get; set; } = false;
}
```

### 8.4 Agent Loop Runner

```csharp
namespace Adze.Broker.Orchestration;

/// <summary>
/// Executes the provider-agnostic multi-turn agent loop.
/// Called on a background thread. Uses IAgentModelClient to
/// communicate with the model and IToolExecutor to run tools.
/// </summary>
public sealed class AgentLoopRunner
{
    /// <summary>
    /// Runs the agent loop to completion or until cancelled/exhausted.
    /// </summary>
    /// <param name="modelClient">Provider-specific model client.</param>
    /// <param name="toolExecutor">Executes tool calls against SessionContext.</param>
    /// <param name="systemPrompt">System instructions.</param>
    /// <param name="userRequest">User's question.</param>
    /// <param name="toolDefinitions">Available tools.</param>
    /// <param name="settings">Loop configuration.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <param name="onProgress">Progress callback (marshaled to UI by caller).</param>
    /// <returns>Final result of the agent loop.</returns>
    public AgentLoopResult Run(
        IAgentModelClient modelClient,
        IToolExecutor toolExecutor,
        string systemPrompt,
        string userRequest,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        CancellationToken cancellationToken,
        Action<AgentProgressUpdate>? onProgress);
}

/// <summary>
/// Executes tool calls against the session context.
/// Abstracts the tool dispatch so the loop runner does not depend
/// on specific tool implementations.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes a single tool call and returns the result.
    /// </summary>
    /// <param name="toolName">Name of the tool to execute.</param>
    /// <param name="arguments">Deserialized arguments from the model.</param>
    /// <returns>Tool execution result.</returns>
    AgentToolResult Execute(string toolName, Dictionary<string, object?> arguments);
}
```

### 8.5 Tool Definition Builder

```csharp
namespace Adze.Broker.Formatting;

/// <summary>
/// Builds AgentToolDefinition instances from the existing tool
/// contracts (ToolNames, parameter classes, request schemas).
/// Provider-agnostic -- the provider client handles format conversion.
/// </summary>
public static class AgentToolDefinitionBuilder
{
    /// <summary>
    /// Builds tool definitions for all enabled tools.
    /// </summary>
    /// <param name="enabledToolNames">
    ///   Tool names from SessionContext.Policy.EnabledTools.
    /// </param>
    /// <returns>List of provider-agnostic tool definitions.</returns>
    public static List<AgentToolDefinition> BuildAll(IEnumerable<string> enabledToolNames);

    /// <summary>
    /// Builds a single tool definition by name.
    /// Returns null if the tool name is not recognized.
    /// </summary>
    public static AgentToolDefinition? Build(string toolName);
}
```

### 8.6 Provider Client Implementation Structure

```csharp
namespace Adze.Broker.Clients;

/// <summary>
/// OpenAI-format agent client. Shared base for OpenAI, OpenRouter,
/// Ollama, and LM Studio.
/// </summary>
public class OpenAIFormatAgentClient : IAgentModelClient
{
    // Builds OpenAI-format request bodies
    // Parses OpenAI-format responses
    // Handles: arguments as JSON string -> Dictionary deserialization
    // Handles: tool_calls[] array parsing
    // Handles: role:"tool" result messages
    // Handles: missing tool_call IDs (generates fallback)
    // Protected virtual methods for subclass customization

    public virtual AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings);

    public object BuildUserMessage(string content);

    public List<object> BuildToolResultMessages(List<AgentToolResult> results);

    /// <summary>
    /// Builds the HTTP request. Virtual so subclasses can customize
    /// headers (e.g., OpenRouter adds HTTP-Referer).
    /// </summary>
    protected virtual HttpWebRequest BuildRequest(
        string endpoint, string apiKey, int timeoutMs);

    /// <summary>
    /// Builds the tool definitions array in OpenAI format.
    /// Virtual so subclasses can add provider-specific fields.
    /// </summary>
    protected virtual object[] BuildToolDefinitionsPayload(
        List<AgentToolDefinition> toolDefinitions);
}

/// <summary>
/// OpenRouter-specific overrides. Mostly configuration.
/// </summary>
public sealed class OpenRouterAgentClient : OpenAIFormatAgentClient
{
    // Adds HTTP-Referer and X-Title headers
    // Model names use provider/model format
    // Otherwise identical to OpenAI format
}

/// <summary>
/// Ollama-specific overrides. Adds defensive handling.
/// </summary>
public sealed class OllamaAgentClient : OpenAIFormatAgentClient
{
    // Generates fallback tool_call IDs when missing
    // Handles arguments as object or string (normalizes to string)
    // Increases default timeout for model loading
    // No auth headers
}

/// <summary>
/// LM Studio-specific overrides.
/// </summary>
public sealed class LMStudioAgentClient : OpenAIFormatAgentClient
{
    // Similar to Ollama: defensive handling for missing fields
    // No auth headers
    // Handles absent usage data gracefully
}

/// <summary>
/// Anthropic-format agent client. Separate implementation
/// due to fundamentally different wire format.
/// </summary>
public sealed class AnthropicAgentClient : IAgentModelClient
{
    // Builds Anthropic-format request bodies
    // Parses Anthropic content-block-based responses
    // Handles: input as parsed object (no extra deserialization)
    // Handles: content[] block parsing (text + tool_use)
    // Handles: tool_result blocks in user messages
    // Handles: is_error flag mapping
    // Handles: system prompt as top-level field (not in messages)
    // Handles: anthropic-version header
    // Handles: max_tokens as required field
}
```

### 8.7 Agent Client Factory

```csharp
namespace Adze.Broker.Clients;

/// <summary>
/// Creates the appropriate IAgentModelClient based on provider
/// configuration. Extends the existing ModelClientFactory pattern.
/// </summary>
public static class AgentClientFactory
{
    /// <summary>
    /// Creates an agent model client from environment configuration.
    /// Returns null if no usable configuration is found.
    /// </summary>
    public static IAgentModelClient? CreateDefault();

    /// <summary>
    /// Creates an agent model client for a specific provider.
    /// </summary>
    /// <param name="provider">
    ///   Provider name: "openai", "anthropic", "openrouter",
    ///   "ollama", "lmstudio".
    /// </param>
    /// <param name="settings">Provider-specific settings.</param>
    public static IAgentModelClient? Create(string provider, BrokerModelSettings settings);
}
```

---

## 9. Provider-Specific Quirks and Gaps

### 9.1 Quirks That Affect the Abstraction

| Quirk | Provider | Impact on Abstraction |
|-------|----------|----------------------|
| `arguments` is a JSON string | OpenAI, OpenRouter, Ollama (compat), LM Studio | `OpenAIFormatAgentClient` must deserialize `arguments` string to `Dictionary` for `AgentToolCall.Arguments` |
| `input` is a parsed object | Anthropic | `AnthropicAgentClient` can use `input` directly; must serialize to JSON string for `AgentToolCall.ArgumentsJson` |
| `arguments` might be an object | Ollama (native endpoint) | `OllamaAgentClient` must handle both string and object |
| `tool_calls[].id` may be missing | Ollama, LM Studio (older versions) | Generate `"local_" + Guid.NewGuid().ToString("N")` as fallback |
| `content` can be null or string | OpenAI-format | Must handle both null and string in `message.content` |
| `content` is always block array | Anthropic | Must iterate blocks to extract text and tool_use |
| No `is_error` on tool results | OpenAI-format | Prepend "ERROR: " to `OutputJson` for clarity; model understands this |
| `system` is a top-level field | Anthropic | `AnthropicAgentClient` must extract system prompt from conversation |
| `max_tokens` is required | Anthropic | Always set explicitly; OpenAI-format can omit for defaults |
| `usage` may be absent | Ollama, LM Studio | Return zero-valued `ModelUsage` when missing |
| `finish_reason` vs `stop_reason` | All | Normalized to `AgentStopReason` enum by each client |
| `tool_choice` format differs | OpenAI (`"required"`) vs Anthropic (`{"type":"any"}`) | Each client translates `AgentModelSettings.DisableParallelToolCalls` to provider format |

### 9.2 Gaps That Cannot Be Bridged

| Gap | Affected Providers | Mitigation |
|-----|-------------------|------------|
| No server-side schema validation | Ollama, LM Studio | Client-side argument validation before tool execution |
| Tool-calling quality varies by model | Ollama, LM Studio | Document recommended models; add `is_error` fallback for malformed calls |
| `strict` mode not available | Anthropic, Ollama, LM Studio, OpenRouter | Do not depend on strict mode; keep schemas simple |
| No guaranteed parallel tool calls | Ollama, LM Studio | Sequential execution is the default; parallel is an optimization |
| Context window size unknown | Ollama, LM Studio | User-configurable `MaxTotalTokens`; conservative defaults |
| Model loading latency | Ollama, LM Studio | Generous first-request timeout; health check endpoint before loop |

### 9.3 Safety Boundaries

The following safety rules apply to all providers and should be enforced in the `AgentLoopRunner`, not in individual clients:

1. **Maximum iterations.** Hard cap at `MaxIterations` (default 10). After this, force a deterministic fallback answer.
2. **Maximum consecutive API errors.** Hard cap at `MaxConsecutiveErrors` (default 2). After this, stop the loop and fall back.
3. **Maximum total tokens.** Hard cap at `MaxTotalTokens` (default 100,000). After this, force termination.
4. **Tool result truncation.** Cap each tool result at `MaxToolResultChars` (default 8,192). Truncate with `... [truncated, {original_length} chars total]`.
5. **Unknown tool names.** Return an error result to the model: `"Unknown tool: {name}. Available tools: {list}"`.
6. **Malformed arguments.** Return an error result to the model: `"Failed to parse arguments: {error}. Expected schema: {schema_summary}"`.
7. **Cancellation.** Check `CancellationToken` before each API call and before each tool execution.

---

## 10. Implementation Priority

### Phase 1: Minimum Viable Agent Loop

**Files to create:**
- `src/Adze.Broker/Abstractions/IAgentModelClient.cs` -- interface + DTOs from section 8.1-8.2
- `src/Adze.Broker/Clients/AnthropicAgentClient.cs` -- Anthropic tool-use wire format
- `src/Adze.Broker/Clients/OpenAIFormatAgentClient.cs` -- OpenAI-format base (covers OpenAI + OpenRouter)
- `src/Adze.Broker/Orchestration/AgentLoopRunner.cs` -- provider-agnostic loop
- `src/Adze.Broker/Formatting/AgentToolDefinitionBuilder.cs` -- builds tool defs from existing contracts
- `src/Adze.Broker/Configuration/AgentModelSettings.cs` -- loop configuration

**Files to modify:**
- `src/Adze.Broker/Clients/ModelClientFactory.cs` -- add `CreateAgentClient()` method
- `src/Adze.Broker/Configuration/BrokerModelSettings.cs` -- add agent loop env vars

**Test coverage:**
- Conversation state construction (both formats)
- Tool call parsing (both formats, including edge cases)
- Tool result message building (both formats)
- Agent loop iteration logic (mock client)
- Tool definition builder (all 10 tools)
- Argument validation and error handling

### Phase 2: Local Provider Support

**Files to create:**
- `src/Adze.Broker/Clients/OllamaAgentClient.cs` -- defensive overrides
- `src/Adze.Broker/Clients/LMStudioAgentClient.cs` -- defensive overrides
- `src/Adze.Broker/Clients/OpenRouterAgentClient.cs` -- header and naming overrides

**Files to modify:**
- `src/Adze.Broker/Configuration/BrokerModelSettings.cs` -- add Ollama/LMStudio/OpenRouter env vars
- `src/Adze.Broker/Clients/ModelClientFactory.cs` -- add new provider cases

### Phase 3: Streaming Final Answer

**Files to create:**
- `src/Adze.Broker/Clients/SseStreamReader.cs` -- reusable SSE line parser for .NET 4.8
- Streaming variants of `SendTurn` or a separate `SendTurnStreaming` method

---

## 11. Risk Assessment

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| Local models produce malformed tool calls | Medium | High (Ollama/LMStudio) | Client-side validation, error results back to model, model quality guidance |
| Token accumulation exceeds budget in multi-turn loops | Medium | Medium | `MaxTotalTokens` cap, tool result truncation, conversation truncation |
| `arguments` JSON string is truncated by `max_tokens` | High | Low (with adequate max_tokens) | Validate assembled JSON, return parse error to model |
| SSE parsing complexity on .NET 4.8 | Medium | N/A (deferred to Phase 3) | Start non-streaming; add streaming only for final answer |
| Provider API changes break wire format | Medium | Low (formats are stable) | Defensive parsing with graceful fallback, version-pinned API headers |
| `JavaScriptSerializer` edge cases with nested dictionaries | Low | Low | Already working in current codebase; the same patterns apply |
| Ollama model loading adds 10-30s to first request | Medium | High | Health check before loop, generous first-request timeout, user guidance |
| OpenRouter adds latency to every request in multi-turn loop | Low | High | Configurable, documented trade-off |

---

## 12. Summary

**The safe abstraction boundary** is `IAgentModelClient` -- a per-turn interface where each provider client translates between the normalized internal format and the provider wire format. The loop runner is entirely provider-agnostic.

**The real implementation split** is two formats, not five providers:
- **Anthropic format:** One dedicated implementation.
- **OpenAI format:** One base implementation shared by OpenAI, OpenRouter, Ollama, and LM Studio, with subclass overrides for authentication, defensive parsing, and model naming.

**What works today and is reliable:**
- OpenAI and Anthropic tool calling: production-stable, well-documented, tested in the existing Adze codebase.
- OpenRouter: production-stable passthrough, uses OpenAI format.

**What works but is flaky:**
- Ollama tool calling: format works, model quality varies. Reliable with Llama 3.3 70b and Qwen 2.5 72b. Unreliable with smaller models.
- LM Studio tool calling: same as Ollama -- format is fine, model quality is the limiting factor.

**What to build first:**
- Anthropic and OpenAI agent clients (Phase 1). These cover the primary use case and the most reliable models.
- Local provider variants (Phase 2). Configuration variants of the OpenAI client with defensive handling.
- Streaming (Phase 3). Only for the final answer turn. Not needed for tool-calling turns.
