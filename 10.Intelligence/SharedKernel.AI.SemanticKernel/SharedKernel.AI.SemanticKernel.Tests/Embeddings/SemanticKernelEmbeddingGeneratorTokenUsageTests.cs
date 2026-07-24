using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Embeddings;
using SharedKernel.AI.SemanticKernel.Embeddings;
using SharedKernel.Testing.Communication;

namespace SharedKernel.AI.SemanticKernel.Tests.Embeddings;

/// <summary>
/// Real-shape <see cref="EmbeddingClient"/> proof (T-08) of <c>TokenUsage</c> accounting.
/// <see cref="EmbeddingClient"/> ships no interface (see <see cref="SemanticKernelEmbeddingGeneratorNoIoTests"/>'s
/// remarks), so — rather than a mocked/synthetic double — a genuine <see cref="EmbeddingClient"/> instance
/// is constructed with its <see cref="System.ClientModel.Primitives.PipelineTransport"/> routed through
/// <see cref="FakeHttpMessageHandler"/>, mirroring the production wiring pattern
/// (<c>HttpClientPipelineTransport</c> over a named <c>IHttpClientFactory</c> client). This proves real
/// SDK response-parsing and token-usage extraction without a network call or a live/paid endpoint.
/// </summary>
public sealed class SemanticKernelEmbeddingGeneratorTokenUsageTests
{
    private static SemanticKernelEmbeddingGenerator CreateGenerator(FakeHttpMessageHandler handler, int dimension = 3) =>
        new(
            BuildClient(handler),
            "text-embedding-3-small",
            dimension,
            maxBatchSize: 10,
            NullLogger<SemanticKernelEmbeddingGenerator>.Instance);

    private static EmbeddingClient BuildClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) };
        return new EmbeddingClient("text-embedding-3-small", new ApiKeyCredential("test-key"), options);
    }

    private static HttpResponseMessage BuildEmbeddingsResponse(int promptTokens, int totalTokens, params float[][] vectors)
    {
        var data = string.Join(
            ",",
            vectors.Select((v, i) => $$"""{ "object": "embedding", "index": {{i}}, "embedding": "{{ToBase64(v)}}" }"""));

        var json = $$"""
        {
          "object": "list",
          "data": [ {{data}} ],
          "model": "text-embedding-3-small",
          "usage": { "prompt_tokens": {{promptTokens}}, "total_tokens": {{totalTokens}} }
        }
        """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static string ToBase64(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsTheProvidersReportedTokenUsage_NeverReDerivedLocally()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(BuildEmbeddingsResponse(promptTokens: 7, totalTokens: 7, [0.1f, 0.2f, 0.3f]));
        var generator = CreateGenerator(handler);

        var result = await generator.EmbedAsync("hello world");

        result.IsSuccess.Should().BeTrue();
        result.Value.TokenUsage.PromptTokens.Should().Be(7);
        result.Value.TokenUsage.CompletionTokens.Should().Be(0, "an embedding call has no completion half");
        result.Value.TokenUsage.TotalTokens.Should().Be(7);
        result.Value.Vector.ToArray().Should().BeEquivalentTo(new float[] { 0.1f, 0.2f, 0.3f });
        result.Value.ModelId.Should().Be("text-embedding-3-small");
        result.Value.Dimension.Should().Be(3);
    }

    [Fact]
    public async Task EmbedManyAsync_PreservesInputOrder_AndReportsTokenUsageUnconditionally()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(BuildEmbeddingsResponse(promptTokens: 12, totalTokens: 12, [1f, 0f, 0f], [0f, 1f, 0f]));
        var generator = CreateGenerator(handler);

        var result = await generator.EmbedManyAsync(["first", "second"]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Embeddings.Should().HaveCount(2);
        result.Value.Embeddings[0].ToArray().Should().BeEquivalentTo(new float[] { 1f, 0f, 0f });
        result.Value.Embeddings[1].ToArray().Should().BeEquivalentTo(new float[] { 0f, 1f, 0f });
        result.Value.TokenUsage.TotalTokens.Should().Be(12);
    }

    [Fact]
    public async Task EmbedManyAsync_DispatchesTheDeclaredDimension_AndBase64EncodingFormat()
    {
        // The request body is captured INSIDE the response factory (i.e. at the moment SendAsync is
        // invoked) rather than read back from handler.Requests[0].Content afterward — by the time
        // EmbedManyAsync returns, System.ClientModel's pipeline has already disposed the underlying
        // HttpRequestMessage's content stream, so a post-hoc read throws ObjectDisposedException.
        var handler = new FakeHttpMessageHandler();
        string? capturedBody = null;
        handler.EnqueueResponse(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return BuildEmbeddingsResponse(promptTokens: 1, totalTokens: 1, [0f, 0f, 0f]);
        });
        var generator = CreateGenerator(handler, dimension: 3);

        await generator.EmbedManyAsync(["hello"]);

        handler.Requests.Should().ContainSingle();
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"encoding_format\":\"base64\"");
        capturedBody.Should().Contain("\"dimensions\":3");
    }
}
