using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Adze.Broker.Clients;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class SseStreamReaderTests
{
    private static MemoryStream CreateSseStream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    // --- SseStreamReader.ReadStream tests ---

    [Test]
    public void ReadStream_SingleChunk_ExtractsContent()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: [DONE]\n";

        var chunks = new List<string>();
        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, chunk => chunks.Add(chunk));

        Assert.That(result.FullText, Is.EqualTo("Hello"));
        Assert.That(chunks.Count, Is.EqualTo(1));
        Assert.That(chunks[0], Is.EqualTo("Hello"));
    }

    [Test]
    public void ReadStream_MultipleChunks_AccumulatesText()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"!\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: [DONE]\n";

        var chunks = new List<string>();
        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, chunk => chunks.Add(chunk));

        Assert.That(result.FullText, Is.EqualTo("Hello world!"));
        Assert.That(chunks.Count, Is.EqualTo(3));
        Assert.That(chunks[0], Is.EqualTo("Hello"));
        Assert.That(chunks[1], Is.EqualTo(" world"));
        Assert.That(chunks[2], Is.EqualTo("!"));
    }

    [Test]
    public void ReadStream_EmptyDelta_SkipsCallback()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n" +
            "\n" +
            "data: [DONE]\n";

        var chunks = new List<string>();
        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, chunk => chunks.Add(chunk));

        Assert.That(result.FullText, Is.EqualTo("Hi"));
        Assert.That(chunks.Count, Is.EqualTo(1));
        Assert.That(result.FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public void ReadStream_WithUsageInFinalChunk_ParsesUsage()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"OK\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}\n" +
            "\n" +
            "data: [DONE]\n";

        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, null);

        Assert.That(result.FullText, Is.EqualTo("OK"));
        Assert.That(result.Usage.PromptTokens, Is.EqualTo(10));
        Assert.That(result.Usage.CompletionTokens, Is.EqualTo(5));
        Assert.That(result.Usage.TotalTokens, Is.EqualTo(15));
        Assert.That(result.FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public void ReadStream_NullCallback_StillAccumulatesText()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"test\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: [DONE]\n";

        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, null);

        Assert.That(result.FullText, Is.EqualTo("test"));
    }

    [Test]
    public void ReadStream_MalformedJsonLine_SkipsGracefully()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"before\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: {not valid json}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" after\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: [DONE]\n";

        var chunks = new List<string>();
        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, chunk => chunks.Add(chunk));

        Assert.That(result.FullText, Is.EqualTo("before after"));
        Assert.That(chunks.Count, Is.EqualTo(2));
    }

    [Test]
    public void ReadStream_NonDataLines_AreIgnored()
    {
        string sse =
            ": this is a comment\n" +
            "event: chunk\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: [DONE]\n";

        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, null);

        Assert.That(result.FullText, Is.EqualTo("Hi"));
    }

    [Test]
    public void ReadStream_NoDoneTerminator_ReadsToEnd()
    {
        string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":null}]}\n" +
            "\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" text\"},\"finish_reason\":\"stop\"}]}\n";

        using var stream = CreateSseStream(sse);
        SseStreamResult result = SseStreamReader.ReadStream(stream, null);

        Assert.That(result.FullText, Is.EqualTo("partial text"));
        Assert.That(result.FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public void ReadStream_EmptyStream_ReturnsEmptyResult()
    {
        using var stream = CreateSseStream("");
        SseStreamResult result = SseStreamReader.ReadStream(stream, null);

        Assert.That(result.FullText, Is.Empty);
        Assert.That(result.FinishReason, Is.Empty);
    }

    [Test]
    public void ReadStream_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SseStreamReader.ReadStream(null!, null));
    }

    // --- SseStreamReader.ParseChunk tests ---

    [Test]
    public void ParseChunk_ValidContentDelta_ExtractsText()
    {
        string json = "{\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}";
        SseChunkResult result = SseStreamReader.ParseChunk(json);

        Assert.That(result.ContentDelta, Is.EqualTo("Hello"));
        Assert.That(result.FinishReason, Is.Empty);
        Assert.That(result.Usage, Is.Null);
    }

    [Test]
    public void ParseChunk_EmptyDelta_ReturnsEmptyContent()
    {
        string json = "{\"choices\":[{\"delta\":{},\"finish_reason\":null}]}";
        SseChunkResult result = SseStreamReader.ParseChunk(json);

        Assert.That(result.ContentDelta, Is.Empty);
    }

    [Test]
    public void ParseChunk_FinishReasonStop_ExtractsReason()
    {
        string json = "{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}";
        SseChunkResult result = SseStreamReader.ParseChunk(json);

        Assert.That(result.FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public void ParseChunk_WithUsage_ParsesTokenCounts()
    {
        string json = "{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":50,\"total_tokens\":150}}";
        SseChunkResult result = SseStreamReader.ParseChunk(json);

        Assert.That(result.Usage, Is.Not.Null);
        Assert.That(result.Usage!.PromptTokens, Is.EqualTo(100));
        Assert.That(result.Usage.CompletionTokens, Is.EqualTo(50));
        Assert.That(result.Usage.TotalTokens, Is.EqualTo(150));
    }

    [Test]
    public void ParseChunk_MalformedJson_ReturnsEmptyResult()
    {
        SseChunkResult result = SseStreamReader.ParseChunk("{not valid}");

        Assert.That(result.ContentDelta, Is.Empty);
        Assert.That(result.FinishReason, Is.Empty);
        Assert.That(result.Usage, Is.Null);
    }

    [Test]
    public void ParseChunk_EmptyString_ReturnsEmptyResult()
    {
        SseChunkResult result = SseStreamReader.ParseChunk("");

        Assert.That(result.ContentDelta, Is.Empty);
    }

    [Test]
    public void ParseChunk_NullString_ReturnsEmptyResult()
    {
        SseChunkResult result = SseStreamReader.ParseChunk(null!);

        Assert.That(result.ContentDelta, Is.Empty);
    }

    [Test]
    public void ParseChunk_NoChoicesArray_ReturnsEmpty()
    {
        SseChunkResult result = SseStreamReader.ParseChunk("{\"id\":\"test\"}");

        Assert.That(result.ContentDelta, Is.Empty);
    }

    // --- IStreamingModelClient interface tests ---

    [Test]
    public void OpenAIModelClient_ImplementsIStreamingModelClient()
    {
        var settings = new Adze.Broker.Configuration.BrokerModelSettings
        {
            Enabled = true,
            ApiKey = "test-key",
            Provider = "openai",
            Model = "gpt-4o",
            Endpoint = "https://api.openai.com/v1/chat/completions"
        };

        var client = new OpenAIModelClient(settings);
        Assert.That(client, Is.InstanceOf<Adze.Broker.Abstractions.IStreamingModelClient>());
        Assert.That(client, Is.InstanceOf<Adze.Broker.Abstractions.IModelClient>());
    }
}
