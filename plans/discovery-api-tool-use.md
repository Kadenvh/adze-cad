# Discovery Brief: API Tool Use for Agentic Loop

**Date:** 2026-03-15
**Mode:** Research
**Status:** Ready for implementation planning

---

## Context

Adze currently uses a two-pass architecture: (1) the broker sends a single-shot prompt to the model and parses a JSON response containing tool recommendations, then (2) the host executes those tools locally and sends a second synthesis prompt with the results. This works but is not a true agentic tool loop -- the model never directly calls tools or decides to call additional tools based on intermediate results.

This brief documents the exact API wire formats for native tool use on both Anthropic Messages API and OpenAI Chat Completions API, plus OpenRouter passthrough, to inform the design of a real agentic loop in the existing .NET Framework 4.8 / `HttpWebRequest` codebase.

---

## 1. Anthropic Messages API -- Tool Use

### 1.1 Sending Tool Definitions

Tools are defined in a top-level `tools` array in the request body. Each tool has a `name`, `description`, and `input_schema` (a JSON Schema object).

```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 1024,
  "system": "You are a SOLIDWORKS assistant.",
  "tools": [
    {
      "name": "get_feature_tree_slice",
      "description": "Returns the feature tree for the active document, optionally filtered by depth or feature type.",
      "input_schema": {
        "type": "object",
        "properties": {
          "max_depth": {
            "type": "integer",
            "description": "Maximum tree depth to return. Default is full depth."
          },
          "feature_type_filter": {
            "type": "string",
            "description": "Optional filter to only return features of this type."
          }
        },
        "required": []
      }
    },
    {
      "name": "get_dimensions",
      "description": "Returns all dimensions in the active document or a specific feature.",
      "input_schema": {
        "type": "object",
        "properties": {
          "feature_name": {
            "type": "string",
            "description": "Optional feature name to scope the dimension query."
          }
        },
        "required": []
      }
    }
  ],
  "messages": [
    {
      "role": "user",
      "content": "What are the key dimensions of the Boss-Extrude1 feature?"
    }
  ]
}
```

**Key details:**
- `input_schema` uses standard JSON Schema (type, properties, required, description).
- The `tools` array is a sibling of `messages`, `model`, `system`, etc.
- Anthropic API version header `anthropic-version: 2023-06-01` supports tool use.
- Optional `tool_choice` field controls tool invocation behavior (see section 1.5).

### 1.2 Model Response with tool_use

When the model decides to use a tool, the response `stop_reason` is `"tool_use"` (not `"end_turn"`), and the `content` array contains a `tool_use` block:

```json
{
  "id": "msg_01XFDUDYJgAACzvnptvVoYEL",
  "type": "message",
  "role": "assistant",
  "stop_reason": "tool_use",
  "content": [
    {
      "type": "text",
      "text": "I'll look up the dimensions for the Boss-Extrude1 feature."
    },
    {
      "type": "tool_use",
      "id": "toolu_01A09q90qw90lq917835lq9",
      "name": "get_dimensions",
      "input": {
        "feature_name": "Boss-Extrude1"
      }
    }
  ],
  "usage": {
    "input_tokens": 345,
    "output_tokens": 87
  }
}
```

**Key details:**
- The `content` array can contain both `text` and `tool_use` blocks in the same response.
- Each `tool_use` block has a unique `id` that must be referenced when returning results.
- The `input` field contains the parsed arguments as a JSON object (not a string).
- The model can request **multiple tools** in a single response (multiple `tool_use` blocks).
- `stop_reason` is `"tool_use"` when the model wants tool results before continuing.

### 1.3 Sending Tool Results Back

To continue the conversation after tool execution, append the full assistant message (including all content blocks) and a new `user` message with `tool_result` content blocks:

```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 1024,
  "system": "You are a SOLIDWORKS assistant.",
  "tools": [/* same tools array as before */],
  "messages": [
    {
      "role": "user",
      "content": "What are the key dimensions of the Boss-Extrude1 feature?"
    },
    {
      "role": "assistant",
      "content": [
        {
          "type": "text",
          "text": "I'll look up the dimensions for the Boss-Extrude1 feature."
        },
        {
          "type": "tool_use",
          "id": "toolu_01A09q90qw90lq917835lq9",
          "name": "get_dimensions",
          "input": {
            "feature_name": "Boss-Extrude1"
          }
        }
      ]
    },
    {
      "role": "user",
      "content": [
        {
          "type": "tool_result",
          "tool_use_id": "toolu_01A09q90qw90lq917835lq9",
          "content": "{\"dimensions\": [{\"name\": \"D1@Boss-Extrude1\", \"value\": 25.4, \"unit\": \"mm\"}]}"
        }
      ]
    }
  ]
}
```

**Key details:**
- The `tool_result` block references the `tool_use_id` from the model's response.
- The `content` field inside `tool_result` is a string (the serialized tool output).
- The `content` can alternatively be an array of content blocks (text, image) for rich results.
- You can include `"is_error": true` in the `tool_result` block to indicate a tool failure.
- Tool results go in a `user` role message (not a special role).
- If the model called multiple tools, include multiple `tool_result` blocks in the same `user` message.

### 1.4 Error Handling in Tool Results

```json
{
  "type": "tool_result",
  "tool_use_id": "toolu_01A09q90qw90lq917835lq9",
  "is_error": true,
  "content": "No active document is open in SOLIDWORKS."
}
```

### 1.5 tool_choice

Controls whether and how the model uses tools:

```json
"tool_choice": {"type": "auto"}
```

| Value | Behavior |
|-------|----------|
| `{"type": "auto"}` | Model decides whether to use tools (default) |
| `{"type": "any"}` | Model must use at least one tool |
| `{"type": "tool", "name": "get_dimensions"}` | Model must use this specific tool |

### 1.6 Parallel Tool Use

Anthropic supports the model requesting multiple tools in a single turn. The `content` array will have multiple `tool_use` blocks. You should execute all of them and return all `tool_result` blocks in the same `user` message.

To disable parallel tool use:

```json
"tool_choice": {"type": "auto", "disable_parallel_tool_use": true}
```

---

## 2. OpenAI Chat Completions API -- Tool Use

### 2.1 Sending Tool Definitions

Tools are defined in a top-level `tools` array. Each entry has `type: "function"` and a `function` object with `name`, `description`, and `parameters` (JSON Schema).

```json
{
  "model": "gpt-4o",
  "messages": [
    {"role": "system", "content": "You are a SOLIDWORKS assistant."},
    {"role": "user", "content": "What are the key dimensions of the Boss-Extrude1 feature?"}
  ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_feature_tree_slice",
        "description": "Returns the feature tree for the active document.",
        "parameters": {
          "type": "object",
          "properties": {
            "max_depth": {
              "type": "integer",
              "description": "Maximum tree depth to return."
            }
          },
          "required": []
        }
      }
    },
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "Returns all dimensions in the active document or a specific feature.",
        "parameters": {
          "type": "object",
          "properties": {
            "feature_name": {
              "type": "string",
              "description": "Optional feature name."
            }
          },
          "required": []
        }
      }
    }
  ]
}
```

**Key differences from Anthropic:**
- Extra wrapper: `{"type": "function", "function": {...}}`.
- Schema goes in `parameters` (not `input_schema`).
- System message is in the `messages` array (not a top-level `system` field).

### 2.2 Model Response with tool_calls

When the model decides to call tools, `finish_reason` is `"tool_calls"` and the assistant message contains a `tool_calls` array:

```json
{
  "id": "chatcmpl-abc123",
  "object": "chat.completion",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": null,
        "tool_calls": [
          {
            "id": "call_abc123",
            "type": "function",
            "function": {
              "name": "get_dimensions",
              "arguments": "{\"feature_name\": \"Boss-Extrude1\"}"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ],
  "usage": {
    "prompt_tokens": 345,
    "completion_tokens": 42,
    "total_tokens": 387
  }
}
```

**Key differences from Anthropic:**
- `content` is typically `null` when tool calls are present (can sometimes contain text too).
- `arguments` is a **JSON string** (not a parsed object) -- you must deserialize it yourself.
- Tool call `id` format differs (`call_xxx` vs `toolu_xxx`).
- `finish_reason` is `"tool_calls"` (not `"tool_use"`).
- Multiple `tool_calls` entries indicate parallel tool invocation.

### 2.3 Sending Tool Results Back

Append the full assistant message (including `tool_calls`) and then a separate message per tool result with `role: "tool"`:

```json
{
  "model": "gpt-4o",
  "messages": [
    {"role": "system", "content": "You are a SOLIDWORKS assistant."},
    {"role": "user", "content": "What are the key dimensions of the Boss-Extrude1 feature?"},
    {
      "role": "assistant",
      "content": null,
      "tool_calls": [
        {
          "id": "call_abc123",
          "type": "function",
          "function": {
            "name": "get_dimensions",
            "arguments": "{\"feature_name\": \"Boss-Extrude1\"}"
          }
        }
      ]
    },
    {
      "role": "tool",
      "tool_call_id": "call_abc123",
      "content": "{\"dimensions\": [{\"name\": \"D1@Boss-Extrude1\", \"value\": 25.4, \"unit\": \"mm\"}]}"
    }
  ],
  "tools": [/* same tools array */]
}
```

**Key differences from Anthropic:**
- Tool results use a dedicated `role: "tool"` (not `role: "user"` with `tool_result` blocks).
- Each tool result is a **separate message** (not multiple blocks in one message).
- The field is `tool_call_id` (not `tool_use_id`).
- `content` is always a string.

### 2.4 tool_choice

```json
"tool_choice": "auto"
```

| Value | Behavior |
|-------|----------|
| `"auto"` | Model decides (default) |
| `"required"` | Must call at least one tool |
| `"none"` | Must not call any tools |
| `{"type": "function", "function": {"name": "get_dimensions"}}` | Must call this specific tool |

### 2.5 Parallel Tool Calls

OpenAI supports parallel tool calls by default. The `tool_calls` array may contain multiple entries. You can disable this with:

```json
"parallel_tool_calls": false
```

When parallel calls are returned, each gets its own `role: "tool"` result message, all inserted in sequence after the assistant message.

---

## 3. OpenRouter Compatibility

### 3.1 Format

OpenRouter uses the **OpenAI-compatible format** for all providers. You send OpenAI-shaped `tools` definitions and receive OpenAI-shaped `tool_calls` responses, regardless of the underlying model.

**Endpoint:** `https://openrouter.ai/api/v1/chat/completions`
**Auth:** `Authorization: Bearer <OPENROUTER_API_KEY>`

```json
{
  "model": "anthropic/claude-sonnet-4-20250514",
  "messages": [...],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": { "type": "object", "properties": {...} }
      }
    }
  ]
}
```

### 3.2 Provider Coverage

OpenRouter supports tool calling for models that natively support it, including:
- Anthropic models (3.5 Sonnet, 3.5 Haiku, Opus, Sonnet 4, etc.)
- OpenAI GPT-4o, GPT-4 Turbo, GPT-4.1 series
- Google Gemini models
- Mistral models with function calling support

OpenRouter translates between the OpenAI wire format and each provider's native format transparently.

### 3.3 Implications for Adze

If Adze adds OpenRouter as a third provider option, the implementation only needs the OpenAI-format client code. The `model` field uses the `provider/model-name` format (e.g., `anthropic/claude-sonnet-4-20250514`). No Anthropic-format handling is needed when going through OpenRouter.

---

## 4. Multi-Turn Agentic Tool Loop

### 4.1 General Pattern

```
User sends initial request
  |
  v
Loop:
  Send messages + tools to API
  |
  v
  Parse response
  |
  +--> stop_reason == "end_turn" / finish_reason == "stop"
  |      --> Extract final text answer, exit loop
  |
  +--> stop_reason == "tool_use" / finish_reason == "tool_calls"
         --> Append assistant message to conversation history
         --> For each tool call:
              Execute tool locally
              Build tool result message/block
         --> Append tool result(s) to conversation history
         --> Continue loop (send again)
```

### 4.2 Anthropic Loop (Pseudocode)

```
messages = [user_message]

while true:
    response = POST /v1/messages {model, system, tools, messages, max_tokens}

    # Append full assistant message to history
    messages.append({role: "assistant", content: response.content})

    if response.stop_reason == "end_turn":
        # Extract text blocks from response.content
        return final_answer

    if response.stop_reason == "tool_use":
        tool_results = []
        for block in response.content:
            if block.type == "tool_use":
                result = execute_tool(block.name, block.input)
                tool_results.append({
                    type: "tool_result",
                    tool_use_id: block.id,
                    content: serialize(result)
                })

        messages.append({role: "user", content: tool_results})

    if iteration_count > MAX_ITERATIONS:
        return error("Loop limit exceeded")
```

### 4.3 OpenAI Loop (Pseudocode)

```
messages = [system_message, user_message]

while true:
    response = POST /v1/chat/completions {model, messages, tools, max_tokens}
    choice = response.choices[0]

    # Append full assistant message to history
    messages.append(choice.message)

    if choice.finish_reason == "stop":
        return choice.message.content

    if choice.finish_reason == "tool_calls":
        for tool_call in choice.message.tool_calls:
            args = JSON.parse(tool_call.function.arguments)
            result = execute_tool(tool_call.function.name, args)
            messages.append({
                role: "tool",
                tool_call_id: tool_call.id,
                content: serialize(result)
            })

    if iteration_count > MAX_ITERATIONS:
        return error("Loop limit exceeded")
```

### 4.4 Full Anthropic Conversation Example (3 turns)

**Turn 1 -- Initial request:**
```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 1024,
  "system": "You are a SOLIDWORKS CAD assistant...",
  "tools": [
    {"name": "get_active_document", "description": "...", "input_schema": {"type": "object", "properties": {}}},
    {"name": "get_dimensions", "description": "...", "input_schema": {"type": "object", "properties": {"feature_name": {"type": "string"}}}}
  ],
  "messages": [
    {"role": "user", "content": "What is this part and what are its key dimensions?"}
  ]
}
```

**Turn 1 -- Response (model calls first tool):**
```json
{
  "stop_reason": "tool_use",
  "content": [
    {"type": "text", "text": "Let me first check what document is open."},
    {"type": "tool_use", "id": "toolu_01AAA", "name": "get_active_document", "input": {}}
  ]
}
```

**Turn 2 -- Send tool result, model calls second tool:**
```json
{
  "messages": [
    {"role": "user", "content": "What is this part and what are its key dimensions?"},
    {"role": "assistant", "content": [
      {"type": "text", "text": "Let me first check what document is open."},
      {"type": "tool_use", "id": "toolu_01AAA", "name": "get_active_document", "input": {}}
    ]},
    {"role": "user", "content": [
      {"type": "tool_result", "tool_use_id": "toolu_01AAA", "content": "{\"name\": \"Bracket.SLDPRT\", \"type\": \"Part\"}"}
    ]}
  ]
}
```

**Turn 2 -- Response:**
```json
{
  "stop_reason": "tool_use",
  "content": [
    {"type": "text", "text": "This is Bracket.SLDPRT. Let me get the dimensions."},
    {"type": "tool_use", "id": "toolu_01BBB", "name": "get_dimensions", "input": {}}
  ]
}
```

**Turn 3 -- Send second tool result, model generates final answer:**
```json
{
  "messages": [
    {"role": "user", "content": "What is this part and what are its key dimensions?"},
    {"role": "assistant", "content": [
      {"type": "text", "text": "Let me first check what document is open."},
      {"type": "tool_use", "id": "toolu_01AAA", "name": "get_active_document", "input": {}}
    ]},
    {"role": "user", "content": [
      {"type": "tool_result", "tool_use_id": "toolu_01AAA", "content": "{\"name\": \"Bracket.SLDPRT\", \"type\": \"Part\"}"}
    ]},
    {"role": "assistant", "content": [
      {"type": "text", "text": "This is Bracket.SLDPRT. Let me get the dimensions."},
      {"type": "tool_use", "id": "toolu_01BBB", "name": "get_dimensions", "input": {}}
    ]},
    {"role": "user", "content": [
      {"type": "tool_result", "tool_use_id": "toolu_01BBB", "content": "{\"dimensions\": [{\"name\": \"D1\", \"value\": 50.0, \"unit\": \"mm\"}]}"}
    ]}
  ]
}
```

**Turn 3 -- Response (final answer):**
```json
{
  "stop_reason": "end_turn",
  "content": [
    {"type": "text", "text": "This is **Bracket.SLDPRT**, a Part document. Its key dimension is D1 at 50.0 mm."}
  ]
}
```

---

## 5. Streaming with Tool Use

### 5.1 Anthropic Streaming

Use `"stream": true` in the request. The response is a Server-Sent Events (SSE) stream. Tool use blocks are delivered incrementally:

```
event: message_start
data: {"type":"message_start","message":{"id":"msg_...","type":"message","role":"assistant","content":[],...}}

event: content_block_start
data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

event: content_block_delta
data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Let me check"}}

event: content_block_stop
data: {"type":"content_block_stop","index":0}

event: content_block_start
data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01AAA","name":"get_dimensions","input":{}}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"feature"}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"_name\": \"Boss\"}"}}

event: content_block_stop
data: {"type":"content_block_stop","index":1}

event: message_delta
data: {"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":87}}

event: message_stop
data: {"type":"message_stop"}
```

**Key details:**
- Tool use input arrives as `input_json_delta` fragments that must be concatenated and parsed after `content_block_stop`.
- You cannot execute the tool until the full `input` JSON is assembled.
- Text blocks stream normally with `text_delta`.
- The `message_delta` event carries the `stop_reason`.

### 5.2 OpenAI Streaming

Use `"stream": true`. Tool calls stream in the `delta` field of each chunk:

```json
{"choices":[{"delta":{"role":"assistant","content":null,"tool_calls":[{"index":0,"id":"call_abc123","type":"function","function":{"name":"get_dimensions","arguments":""}}]},"finish_reason":null}]}

{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"feat"}}]},"finish_reason":null}]}

{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"ure_name\":"}}]},"finish_reason":null}]}

{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":" \"Boss\"}"}}]},"finish_reason":null}]}

{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
```

**Key details:**
- The first chunk contains the `id`, `name`, and the start of `arguments`.
- Subsequent chunks append to `arguments` for the tool call at the given `index`.
- You must concatenate `arguments` fragments and parse the final JSON.
- `finish_reason: "tool_calls"` appears in the final chunk.
- For parallel tool calls, different `index` values identify different calls.

### 5.3 Streaming Recommendation for Adze

Streaming is valuable for the final text answer (perceived responsiveness in the Task Pane) but adds significant complexity during tool-call turns where you must buffer the entire tool invocation anyway. The recommended approach is:

1. **Non-streaming for tool loop turns** -- simpler parsing, and tool execution latency dominates anyway.
2. **Optional streaming for the final answer turn** -- can be added later when UX responsiveness matters.

This avoids the SSE parsing complexity in .NET Framework 4.8 (no built-in SSE client) during the initial implementation.

---

## 6. C# Implementation Considerations (.NET Framework 4.8)

### 6.1 Serialization Strategy

The codebase currently uses `System.Web.Script.Serialization.JavaScriptSerializer` for JSON. This works for tool use but requires careful handling:

**Building tool definitions (Anthropic format):**
```csharp
var tools = new object[]
{
    new Dictionary<string, object>
    {
        ["name"] = "get_dimensions",
        ["description"] = "Returns dimensions for the active document.",
        ["input_schema"] = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["feature_name"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional feature name."
                }
            },
            ["required"] = new string[0]
        }
    }
};
```

**Building tool definitions (OpenAI format):**
```csharp
var tools = new object[]
{
    new Dictionary<string, object>
    {
        ["type"] = "function",
        ["function"] = new Dictionary<string, object>
        {
            ["name"] = "get_dimensions",
            ["description"] = "Returns dimensions for the active document.",
            ["parameters"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["feature_name"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Optional feature name."
                    }
                },
                ["required"] = new string[0]
            }
        }
    }
};
```

### 6.2 Response Parsing

**Anthropic -- detecting tool_use in response:**
```csharp
// After deserializing the response JSON:
var responseDict = (IDictionary<string, object>)payload;
string stopReason = Convert.ToString(responseDict["stop_reason"]); // "tool_use" or "end_turn"
var contentBlocks = (IEnumerable)responseDict["content"];

var toolCalls = new List<ToolCallInfo>();
var textParts = new List<string>();

foreach (IDictionary<string, object> block in contentBlocks)
{
    string blockType = Convert.ToString(block["type"]);
    if (blockType == "tool_use")
    {
        toolCalls.Add(new ToolCallInfo
        {
            Id = Convert.ToString(block["id"]),
            Name = Convert.ToString(block["name"]),
            InputJson = serializer.Serialize(block["input"])
        });
    }
    else if (blockType == "text")
    {
        textParts.Add(Convert.ToString(block["text"]));
    }
}
```

**OpenAI -- detecting tool_calls in response:**
```csharp
var responseDict = (IDictionary<string, object>)payload;
var choices = (IEnumerable)responseDict["choices"];
var firstChoice = (IDictionary<string, object>)choices.Cast<object>().First();
string finishReason = Convert.ToString(firstChoice["finish_reason"]); // "tool_calls" or "stop"
var message = (IDictionary<string, object>)firstChoice["message"];

var toolCalls = new List<ToolCallInfo>();

if (message.TryGetValue("tool_calls", out object toolCallsValue) && toolCallsValue is IEnumerable calls)
{
    foreach (IDictionary<string, object> call in calls)
    {
        var function = (IDictionary<string, object>)call["function"];
        toolCalls.Add(new ToolCallInfo
        {
            Id = Convert.ToString(call["id"]),
            Name = Convert.ToString(function["name"]),
            InputJson = Convert.ToString(function["arguments"]) // Already a JSON string
        });
    }
}
```

### 6.3 Building Tool Result Messages

**Anthropic format:**
```csharp
// Append assistant content blocks as-is, then add tool results:
var toolResultMessage = new Dictionary<string, object>
{
    ["role"] = "user",
    ["content"] = toolCalls.Select(tc => new Dictionary<string, object>
    {
        ["type"] = "tool_result",
        ["tool_use_id"] = tc.Id,
        ["content"] = tc.ResultJson
        // Add ["is_error"] = true for failures
    }).ToArray()
};
```

**OpenAI format:**
```csharp
// Append full assistant message first (including tool_calls), then one message per result:
var toolResultMessages = toolCalls.Select(tc => new Dictionary<string, object>
{
    ["role"] = "tool",
    ["tool_call_id"] = tc.Id,
    ["content"] = tc.ResultJson
}).ToList();
```

### 6.4 Conversation History Management

The key architectural difference from the current two-pass design: the agentic loop must maintain and grow a `List<object>` of messages across iterations. Each iteration appends the assistant response and tool results, then sends the entire history back.

```csharp
var messages = new List<object>();
messages.Add(new Dictionary<string, object> { ["role"] = "user", ["content"] = userRequest });

int iteration = 0;
const int MaxIterations = 5;

while (iteration++ < MaxIterations)
{
    // Build request body with full messages list
    var requestBody = BuildRequestBody(messages, tools);
    var response = SendRequest(requestBody);

    // Append assistant message to history
    messages.Add(ExtractAssistantMessage(response));

    if (IsTerminalResponse(response))
    {
        return ExtractFinalAnswer(response);
    }

    // Execute tools and append results
    var toolResults = ExecuteToolCalls(response);
    AppendToolResults(messages, toolResults); // Format differs by provider
}
```

### 6.5 Key Data Structures to Define

```csharp
/// <summary>Parsed tool call from a model response.</summary>
public sealed class ToolCallRequest
{
    public string Id { get; set; }        // "toolu_xxx" or "call_xxx"
    public string Name { get; set; }      // "get_dimensions"
    public string InputJson { get; set; } // Raw JSON arguments
}

/// <summary>Result of executing a tool call.</summary>
public sealed class ToolCallResult
{
    public string ToolCallId { get; set; }
    public string Name { get; set; }
    public string OutputJson { get; set; }
    public bool IsError { get; set; }
}

/// <summary>Parsed model response with support for tool calls.</summary>
public sealed class AgenticTurnResult
{
    public bool IsToolCall { get; set; }
    public bool IsTerminal { get; set; }
    public string FinalText { get; set; }
    public List<ToolCallRequest> ToolCalls { get; set; }
    public object RawAssistantMessage { get; set; } // Preserve for history
    public ModelUsage Usage { get; set; }
}
```

### 6.6 Token Budget Tracking

Each iteration adds to the conversation history, increasing input token costs. Track cumulative usage:

```csharp
var totalUsage = new ModelUsage();
// After each API call:
totalUsage = totalUsage + response.Usage;
// Monitor: if totalUsage.TotalTokens > budget, force termination
```

---

## 7. Side-by-Side Format Comparison

| Aspect | Anthropic Messages API | OpenAI Chat Completions API |
|--------|----------------------|---------------------------|
| Tool definition key | `input_schema` | `parameters` (inside `function` wrapper) |
| Tool definition wrapper | None | `{"type": "function", "function": {...}}` |
| System prompt | Top-level `system` field | Message with `role: "system"` |
| Tool call response location | `content[]` blocks with `type: "tool_use"` | `message.tool_calls[]` array |
| Tool call arguments | Parsed object in `input` | JSON **string** in `arguments` |
| Stop signal | `stop_reason: "tool_use"` | `finish_reason: "tool_calls"` |
| Terminal signal | `stop_reason: "end_turn"` | `finish_reason: "stop"` |
| Tool result role | `role: "user"` with `tool_result` blocks | `role: "tool"` (dedicated role) |
| Tool result ID field | `tool_use_id` | `tool_call_id` |
| Multiple results | Array of blocks in one `user` message | Separate `tool` messages |
| Error signaling | `is_error: true` on `tool_result` | Error text in `content` (no flag) |
| Parallel control | `disable_parallel_tool_use` in `tool_choice` | `parallel_tool_calls: false` |

---

## Recommendations

### 1. Implement a Provider-Agnostic Agentic Loop

Create an `IAgenticClient` interface that abstracts over the provider-specific wire formats. Each provider client builds the request, parses the response, and produces a unified `AgenticTurnResult`. The loop logic itself is provider-agnostic:

```
IAgenticClient
  +-- AnthropicAgenticClient   (builds Anthropic-format requests/responses)
  +-- OpenAIAgenticClient      (builds OpenAI-format requests/responses)

AgenticLoopRunner
  Takes: IAgenticClient, tool executor, max iterations, token budget
  Returns: final answer text + usage + trace
```

### 2. Reuse Existing Infrastructure

- Keep `HttpWebRequest`-based HTTP (it works, .NET 4.8 constraint).
- Keep `JavaScriptSerializer` (it handles the nested dictionaries well enough).
- Keep `BrokerModelSettings` and `ModelClientFactory` patterns.
- Keep the deterministic fallback as the safety net.

### 3. Add Tool Schema Generation

Create a `ToolSchemaBuilder` that generates tool definitions from the existing `ToolCatalog` entries. Each tool already has a name and can define its accepted parameters. The schema builder produces the provider-appropriate format.

### 4. Preserve the Two-Pass Path as Fallback

The current broker-then-synthesize path should remain as the fallback when:
- Tool use is explicitly disabled.
- The model does not support tool use.
- The agentic loop exceeds iteration or token limits.

### 5. Start Without Streaming

Implement the non-streaming agentic loop first. Add streaming for the final answer turn as a separate enhancement. SSE parsing in .NET Framework 4.8 requires manual line-by-line reading of the response stream, which is doable but orthogonal to the core loop.

### 6. Consider OpenRouter as a Unified Provider

Adding OpenRouter support would simplify the codebase by allowing a single OpenAI-format implementation to access both Anthropic and OpenAI models. This could reduce the provider client count from two to one for tool use, at the cost of an additional API dependency and slightly higher latency.

---

## Risks

### 1. Token Accumulation

Each loop iteration appends more messages, increasing token consumption. A 3-turn loop with 10 tool results could consume 3-5x the tokens of a single-shot request. Mitigation: enforce a max iteration count (3-5) and a total token budget.

### 2. Latency Stacking

Each loop turn requires a full API round trip (1-5 seconds) plus tool execution time. A 3-turn loop could take 10-15 seconds. The current two-pass architecture has predictable latency. Mitigation: set tight timeouts per turn, show incremental status in the Task Pane.

### 3. Tool Argument Parsing

The model may produce malformed tool arguments, especially with `JavaScriptSerializer` edge cases. The OpenAI format is especially tricky because `arguments` is a JSON string that must be deserialized separately. Mitigation: wrap all argument parsing in try-catch with graceful `is_error` responses.

### 4. History Serialization Size

Conversation histories with large tool results (e.g., full feature trees) can grow large. Sending the full history each turn means the request body grows with every iteration. Mitigation: truncate or summarize large tool outputs before inserting them into the history.

### 5. Parallel Tool Call Ordering

Both APIs support parallel tool calls. If the model requests `get_active_document` and `get_dimensions` simultaneously, and `get_dimensions` depends on knowing the active document, the results may be inconsistent. Mitigation: execute parallel calls safely (the current tools are all read-only and independent of each other) or disable parallel tool use in the initial implementation.

### 6. .NET Framework 4.8 Constraints

No `System.Text.Json`, no `HttpClient` best practices, no `async/await` without `Microsoft.Bcl.Async`. The existing `HttpWebRequest` + `JavaScriptSerializer` pattern works but is synchronous and blocking. For the Task Pane UI, tool loop execution should happen on a background thread to avoid freezing the SOLIDWORKS UI.

### 7. API Version Compatibility

Anthropic's tool use format has been stable since the `2023-06-01` API version, which the codebase already targets. OpenAI's format has been stable since the migration from `functions` to `tools` in late 2023. Both should remain stable but monitor for breaking changes.

### 8. Cost Visibility

The agentic loop multiplies per-request costs. The current single-shot path is predictable. Users and developers need visibility into how many iterations occurred and total token consumption. The existing `ModelUsage` accumulation pattern supports this.
