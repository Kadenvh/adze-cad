using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Adze.Broker.Clients;

/// <summary>
/// Reads Server-Sent Events (SSE) from an HTTP response stream and extracts
/// text content deltas from OpenAI-compatible streaming chat completions.
/// SSE format: lines prefixed with "data: ", terminated by "data: [DONE]".
/// Each data line contains a JSON chunk with choices[0].delta.content.
/// </summary>
public static class SseStreamReader
{
    /// <summary>
    /// Reads an SSE stream line-by-line, extracts text deltas from OpenAI-format
    /// streaming chunks, and calls onTextChunk for each non-empty content delta.
    /// Returns the accumulated full text and usage from the final chunk.
    /// </summary>
    public static SseStreamResult ReadStream(
        Stream responseStream,
        Action<string>? onTextChunk)
    {
        if (responseStream == null)
            throw new ArgumentNullException(nameof(responseStream));

        var fullText = new StringBuilder();
        var usage = new Models.ModelUsage();
        string? finishReason = null;

        using var reader = new StreamReader(responseStream, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Skip empty lines (SSE uses blank lines as event separators)
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // SSE lines must start with "data: "
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            string data = line.Substring(6); // Remove "data: " prefix

            // Check for stream terminator
            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                break;

            // Parse the JSON chunk
            SseChunkResult chunk = ParseChunk(data);

            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                fullText.Append(chunk.ContentDelta);
                onTextChunk?.Invoke(chunk.ContentDelta);
            }

            if (!string.IsNullOrEmpty(chunk.FinishReason))
            {
                finishReason = chunk.FinishReason;
            }

            // Usage is typically in the final chunk (when finish_reason is set)
            if (chunk.Usage != null)
            {
                usage = chunk.Usage;
            }
        }

        return new SseStreamResult
        {
            FullText = fullText.ToString(),
            Usage = usage,
            FinishReason = finishReason ?? string.Empty
        };
    }

    /// <summary>
    /// Parses a single SSE data line (JSON chunk) and extracts the content delta,
    /// finish_reason, and usage fields.
    /// </summary>
    public static SseChunkResult ParseChunk(string json)
    {
        var result = new SseChunkResult();

        if (string.IsNullOrWhiteSpace(json))
            return result;

        try
        {
            var serializer = ModelResponseParser.CreateSerializer();
            object? payload = serializer.DeserializeObject(json);

            if (payload is not IDictionary<string, object> root)
                return result;

            // Extract choices[0].delta.content and choices[0].finish_reason
            if (root.TryGetValue("choices", out object? choicesValue) &&
                choicesValue is object[] choices &&
                choices.Length > 0 &&
                choices[0] is IDictionary<string, object> firstChoice)
            {
                // finish_reason
                if (firstChoice.TryGetValue("finish_reason", out object? frValue) && frValue != null)
                {
                    result.FinishReason = Convert.ToString(frValue) ?? string.Empty;
                }

                // delta.content
                if (firstChoice.TryGetValue("delta", out object? deltaValue) &&
                    deltaValue is IDictionary<string, object> delta &&
                    delta.TryGetValue("content", out object? contentValue) &&
                    contentValue != null)
                {
                    result.ContentDelta = Convert.ToString(contentValue) ?? string.Empty;
                }
            }

            // Extract usage (typically present in the final chunk)
            if (root.TryGetValue("usage", out object? usageValue) &&
                usageValue is IDictionary<string, object> usageDict)
            {
                result.Usage = new Models.ModelUsage
                {
                    PromptTokens = TryGetInt(usageDict, "prompt_tokens"),
                    CompletionTokens = TryGetInt(usageDict, "completion_tokens"),
                    TotalTokens = TryGetInt(usageDict, "total_tokens")
                };
            }
        }
        catch
        {
            // Malformed JSON: skip this chunk silently
        }

        return result;
    }

    private static int TryGetInt(IDictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out object? value) && value != null)
        {
            if (value is int intVal) return intVal;
            if (int.TryParse(Convert.ToString(value), out int parsed)) return parsed;
        }
        return 0;
    }
}

public sealed class SseStreamResult
{
    public string FullText { get; set; } = string.Empty;

    public Models.ModelUsage Usage { get; set; } = new();

    public string FinishReason { get; set; } = string.Empty;
}

public sealed class SseChunkResult
{
    public string ContentDelta { get; set; } = string.Empty;

    public string FinishReason { get; set; } = string.Empty;

    public Models.ModelUsage? Usage { get; set; }
}
