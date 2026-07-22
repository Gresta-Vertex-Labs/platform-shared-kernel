using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Testing.Intelligence;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Proves <see cref="InMemoryEmbeddingGenerator"/> against <c>IEmbeddingGenerator</c>'s documented
/// deterministic-vector/token-accounting contract -- no consuming domain has adopted this fake yet, so
/// this self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryEmbeddingGeneratorTests
{
    [Fact]
    public void Constructor_NullModelId_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemoryEmbeddingGenerator(null!, 8));

    [Fact]
    public void Constructor_WhitespaceModelId_Throws() =>
        Assert.Throws<ArgumentException>(() => new InMemoryEmbeddingGenerator("   ", 8));

    [Fact]
    public void Constructor_NonPositiveDimension_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryEmbeddingGenerator("test-model", 0));

    [Fact]
    public void ModelId_And_Dimension_ReturnConstructorValues()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);

        Assert.Equal("test-model", generator.ModelId);
        Assert.Equal(8, generator.Dimension);
    }

    [Fact]
    public async Task EmbedAsync_SameModelAndText_IsDeterministic_AcrossInstances()
    {
        var first = new InMemoryEmbeddingGenerator("test-model", 8);
        var second = new InMemoryEmbeddingGenerator("test-model", 8);

        var firstResult = await first.EmbedAsync("hello world", CancellationToken.None);
        var secondResult = await second.EmbedAsync("hello world", CancellationToken.None);

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(firstResult.Value.Vector.ToArray(), secondResult.Value.Vector.ToArray());
    }

    [Fact]
    public async Task EmbedAsync_DifferentText_ProducesDifferentVector()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);

        var first = await generator.EmbedAsync("hello world", CancellationToken.None);
        var second = await generator.EmbedAsync("goodbye world", CancellationToken.None);

        Assert.NotEqual(first.Value.Vector.ToArray(), second.Value.Vector.ToArray());
    }

    [Fact]
    public async Task EmbedAsync_DifferentModelId_ProducesDifferentVector_ForSameText()
    {
        var first = new InMemoryEmbeddingGenerator("model-a", 8);
        var second = new InMemoryEmbeddingGenerator("model-b", 8);

        var firstResult = await first.EmbedAsync("hello world", CancellationToken.None);
        var secondResult = await second.EmbedAsync("hello world", CancellationToken.None);

        Assert.NotEqual(firstResult.Value.Vector.ToArray(), secondResult.Value.Vector.ToArray());
    }

    [Fact]
    public async Task EmbedAsync_ReturnsVectorOfDeclaredDimension()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 16);

        var result = await generator.EmbedAsync("hello world", CancellationToken.None);

        Assert.Equal(16, result.Value.Vector.Length);
        Assert.Equal(16, result.Value.Dimension);
        Assert.Equal("test-model", result.Value.ModelId);
    }

    [Fact]
    public async Task EmbedAsync_TokenUsage_PromptTokensIsWordCount_CompletionTokensIsZero()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);

        var result = await generator.EmbedAsync("the quick brown fox", CancellationToken.None);

        Assert.Equal(4, result.Value.TokenUsage.PromptTokens);
        Assert.Equal(0, result.Value.TokenUsage.CompletionTokens);
        Assert.Equal(4, result.Value.TokenUsage.TotalTokens);
    }

    [Fact]
    public async Task EmbedAsync_RecordsTextInEmbeddedTexts()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);

        await generator.EmbedAsync("hello world", CancellationToken.None);

        Assert.Contains("hello world", generator.EmbeddedTexts);
    }

    [Fact]
    public async Task EmbedAsync_NullText_Throws()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);

        await Assert.ThrowsAsync<ArgumentNullException>(() => generator.EmbedAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task EmbedAsync_SimulateFailure_ReturnsEngineFault_AndDoesNotRecord()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8) { SimulateFailure = true };

        var result = await generator.EmbedAsync("hello world", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntelligenceErrors.EngineFault("test-model", "EmbedAsync", "SimulateFailure enabled"), result.Error);
        Assert.Empty(generator.EmbeddedTexts);
    }

    [Fact]
    public async Task EmbedManyAsync_PreservesOrder_AndRecordsEveryText()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);
        string[] texts = ["alpha", "beta", "gamma"];

        var result = await generator.EmbedManyAsync(texts, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Embeddings.Count);
        Assert.Equal(texts, generator.EmbeddedTexts);

        // Order-preserving: Embeddings[i] must match a single EmbedAsync call for texts[i].
        var singleAlpha = await new InMemoryEmbeddingGenerator("test-model", 8).EmbedAsync("alpha", CancellationToken.None);
        Assert.Equal(singleAlpha.Value.Vector.ToArray(), result.Value.Embeddings[0].ToArray());
    }

    [Fact]
    public async Task EmbedManyAsync_TokenUsage_SumsPromptTokensAcrossTexts()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);

        var result = await generator.EmbedManyAsync(["one two", "three four five"], CancellationToken.None);

        Assert.Equal(5, result.Value.TokenUsage.PromptTokens);
        Assert.Equal(0, result.Value.TokenUsage.CompletionTokens);
        Assert.Equal(5, result.Value.TokenUsage.TotalTokens);
    }

    [Fact]
    public async Task EmbedManyAsync_NullTexts_Throws()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);

        await Assert.ThrowsAsync<ArgumentNullException>(() => generator.EmbedManyAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task EmbedManyAsync_SimulateFailure_ReturnsFailure_AndDoesNotRecord()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8) { SimulateFailure = true };

        var result = await generator.EmbedManyAsync(["alpha"], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(generator.EmbeddedTexts);
    }

    [Fact]
    public async Task Reset_ClearsEmbeddedTexts()
    {
        var generator = new InMemoryEmbeddingGenerator("test-model", 8);
        await generator.EmbedAsync("hello world", CancellationToken.None);

        generator.Reset();

        Assert.Empty(generator.EmbeddedTexts);
    }
}
