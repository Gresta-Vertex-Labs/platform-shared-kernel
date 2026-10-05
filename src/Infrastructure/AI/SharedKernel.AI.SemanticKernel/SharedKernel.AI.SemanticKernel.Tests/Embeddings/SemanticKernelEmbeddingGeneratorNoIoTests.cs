using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.AI.SemanticKernel.Embeddings;

namespace SharedKernel.AI.SemanticKernel.Tests.Embeddings;

/// <summary>
/// Fail-loud, no-I/O proof tests. <c>OpenAI.Embeddings.EmbeddingClient</c> ships no interface, so
/// (mirroring <c>09.Search</c>'s <c>MeilisearchIndex</c> precedent) the adapter is constructed with a
/// <see langword="null"/>! client — a clean rejection proves no call was attempted, and the companion
/// test proves the guard itself is what stopped it by showing the identical call path throws
/// <see cref="NullReferenceException"/> once every validation passes.
/// </summary>
public sealed class SemanticKernelEmbeddingGeneratorNoIoTests
{
    private static SemanticKernelEmbeddingGenerator CreateGenerator(int maxBatchSize = 10) => new(
        client: null!,
        modelId: "text-embedding-3-small",
        dimension: 1536,
        maxBatchSize: maxBatchSize,
        NullLogger<SemanticKernelEmbeddingGenerator>.Instance);

    [Fact]
    public async Task EmbedManyAsync_EmptyList_ReturnsFailure_WithNoIo()
    {
        var generator = CreateGenerator();

        var result = await generator.EmbedManyAsync([]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.invalid_query");
    }

    [Fact]
    public async Task EmbedManyAsync_ExceedsMaxBatchSize_ReturnsFailure_WithNoIo()
    {
        var generator = CreateGenerator(maxBatchSize: 2);
        var texts = new[] { "one", "two", "three" };

        var result = await generator.EmbedManyAsync(texts);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.batch_size_exceeded");
    }

    [Fact]
    public async Task EmbedManyAsync_ValidatedInputs_ReachesClient_ProvingTheGuardStoppedTheRejectedCases()
    {
        // Companion to the two rejection tests above: the adapter's own catch-all exception handler
        // maps the null-client dereference into a Result.Failure(CompletionFailed) rather than letting
        // NullReferenceException propagate — so the proof here is that the FAILURE CODE differs from
        // the earlier validation-specific codes (invalid_query / batch_size_exceeded), showing the
        // identical call path genuinely reached the client once every validation passed.
        var generator = CreateGenerator();

        var result = await generator.EmbedManyAsync(["hello world"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.completion_failed");
    }

    [Fact]
    public void ModelIdAndDimension_AreZeroIoConstructionTimeProperties()
    {
        var generator = CreateGenerator();

        generator.ModelId.Should().Be("text-embedding-3-small");
        generator.Dimension.Should().Be(1536);
    }
}
