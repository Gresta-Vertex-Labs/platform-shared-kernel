using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.SemanticKernel.Errors;
using SharedKernel.AI.SemanticKernel.Logging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.SemanticKernel.Embeddings;

/// <summary>The Semantic Kernel implementation of <see cref="IEmbeddingGenerator"/>.</summary>
/// <remarks>
/// <b>Built directly on the OpenAI SDK's own <see cref="EmbeddingClient"/>, not
/// <c>Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService</c>:</b> verified directly
/// against the compiled assemblies — SK's neutral embedding service returns a bare
/// <c>IList&lt;ReadOnlyMemory&lt;float&gt;&gt;</c> with no token-usage metadata attached anywhere on the
/// call. Domain Invariant #5 requires <see cref="TokenUsage"/> unconditionally on every result, and the
/// underlying OpenAI SDK's <see cref="EmbeddingClient.GenerateEmbeddingsAsync(System.Collections.Generic.IEnumerable{string},EmbeddingGenerationOptions,System.Threading.CancellationToken)"/>
/// genuinely reports it (<see cref="OpenAIEmbeddingCollection.Usage"/>) — this is the one place this
/// package drops one level below <c>Microsoft.SemanticKernel</c>'s own connector to keep that invariant
/// honest rather than fabricate a usage value. The chat/completion half
/// (<see cref="Orchestration.SemanticKernelOrchestrator"/>) uses SK's own
/// <c>IChatCompletionService</c> directly, since that connector's metadata dictionary does carry usage.
/// </remarks>
internal sealed class SemanticKernelEmbeddingGenerator : IEmbeddingGenerator
{
    private const string ProviderName = IntelligenceWellKnown.SemanticKernelProviderName;

    private readonly EmbeddingClient _client;
    private readonly int _maxBatchSize;
    private readonly ILogger<SemanticKernelEmbeddingGenerator> _logger;

    public SemanticKernelEmbeddingGenerator(
        EmbeddingClient client,
        string modelId,
        int dimension,
        int maxBatchSize,
        ILogger<SemanticKernelEmbeddingGenerator> logger)
    {
        // client is deliberately NOT null-checked here: EmbeddingClient ships no interface, so the
        // "construct with a null! client" no-I/O proof technique (09.Search's MeilisearchIndex
        // precedent) is this package's only way to prove a rejection path never reaches the client.
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        ModelId = modelId;
        Dimension = dimension;
        _maxBatchSize = maxBatchSize;
        _logger = logger;
    }

    public string ModelId { get; }

    public int Dimension { get; }

    public async Task<Result<EmbeddingResult>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var batchResult = await EmbedManyAsync([text], cancellationToken).ConfigureAwait(false);
        if (batchResult.IsFailure)
        {
            return Result<EmbeddingResult>.Failure(batchResult.Error);
        }

        var batch = batchResult.Value;
        return Result<EmbeddingResult>.Success(new EmbeddingResult
        {
            Vector = batch.Embeddings[0],
            ModelId = batch.ModelId,
            Dimension = batch.Dimension,
            TokenUsage = batch.TokenUsage,
        });
    }

    public async Task<Result<EmbeddingBatchResult>> EmbedManyAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return Result<EmbeddingBatchResult>.Failure(IntelligenceErrors.InvalidQuery("At least one text must be supplied."));
        }

        if (texts.Count > _maxBatchSize)
        {
            _logger.SemanticKernelBatchSizeExceeded(texts.Count, _maxBatchSize);
            return Result<EmbeddingBatchResult>.Failure(IntelligenceErrors.BatchSizeExceeded(texts.Count, _maxBatchSize, ProviderName));
        }

        try
        {
            var options = new EmbeddingGenerationOptions { Dimensions = Dimension };
            var response = await _client.GenerateEmbeddingsAsync(texts, options, cancellationToken).ConfigureAwait(false);
            var collection = response.Value;

            var vectors = collection
                .OrderBy(embedding => embedding.Index)
                .Select(embedding => embedding.ToFloats())
                .ToList();

            var tokenUsage = new TokenUsage
            {
                PromptTokens = collection.Usage.InputTokenCount,
                CompletionTokens = 0,
                TotalTokens = collection.Usage.TotalTokenCount,
            };

            _logger.SemanticKernelEmbeddingCompleted(texts.Count, ModelId, tokenUsage.TotalTokens);

            return Result<EmbeddingBatchResult>.Success(new EmbeddingBatchResult
            {
                Embeddings = vectors,
                ModelId = ModelId,
                Dimension = Dimension,
                TokenUsage = tokenUsage,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.SemanticKernelEngineFault(nameof(EmbedManyAsync), ModelId);
            return Result<EmbeddingBatchResult>.Failure(SemanticKernelErrors.FromException(ex, ProviderName, nameof(EmbedManyAsync), ModelId));
        }
    }
}
