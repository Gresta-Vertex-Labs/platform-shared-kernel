using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;

namespace SharedKernel.Testing.Intelligence;

/// <summary>
/// In-memory test double for <see cref="IEmbeddingGenerator"/>. Simulates behavioral correctness
/// (a deterministic, model+text-derived vector and token accounting) -- not real embedding-model
/// output or transport faults.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deterministic hash-derived vector -- this fake's own hard acceptance criterion:</b> each
/// embedding is <c>SHA-256(UTF8 bytes of ModelId + text)</c>, expanded into <see cref="Dimension"/>
/// float components via a <see cref="Random"/> seeded off the hash bytes (never time-based, never an
/// implicitly-seeded <see cref="Random"/>, never <see cref="Guid.NewGuid()"/>) -- the same
/// (<see cref="ModelId"/>, text) pair always yields the byte-identical vector, across processes and
/// machines running the same .NET runtime.
/// </para>
/// <para>
/// <see cref="Intelligence"/> (this namespace, <c>SharedKernel.Testing.Intelligence</c>) references
/// only <c>SharedKernel.AI.Abstractions</c> -- never <c>SharedKernel.AI.Qdrant</c>/<c>.Milvus</c>/
/// <c>.SemanticKernel</c> (the concrete provider packages) nor any sibling capability folder in this
/// package, including <c>Containers/QdrantContainerFixture</c>/<c>.MilvusContainerFixture</c>. This
/// type is also deliberately independent of its five sibling <c>Intelligence/</c> fakes -- see
/// <see cref="InMemoryVectorCollectionProvisioner"/>'s remarks for the full non-coupling rationale.
/// </para>
/// </remarks>
public sealed class InMemoryEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly ConcurrentQueue<string> _embeddedTexts = new();

    /// <summary>
    /// Initializes a new <see cref="InMemoryEmbeddingGenerator"/> bound to <paramref name="modelId"/>
    /// and <paramref name="dimension"/>.
    /// </summary>
    /// <param name="modelId">The embedding model identity this generator reports and validates against.</param>
    /// <param name="dimension">The vector dimension this generator produces. Must be positive.</param>
    public InMemoryEmbeddingGenerator(string modelId, int dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimension, 0);

        ModelId = modelId;
        Dimension = dimension;
    }

    /// <inheritdoc />
    public string ModelId { get; }

    /// <inheritdoc />
    public int Dimension { get; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="EmbedAsync"/>/<see cref="EmbedManyAsync"/>
    /// should simulate a provider fault. When <see langword="true"/>, both members return
    /// <c>IntelligenceErrors.EngineFault</c> instead of embedding.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Gets every text ever successfully embedded, thread-safe, append-only, in call order --
    /// single- and batch-call texts both append.
    /// </summary>
    public IReadOnlyList<string> EmbeddedTexts => _embeddedTexts.ToArray();

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<EmbeddingResult>> EmbedAsync(
        string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<EmbeddingResult>.Failure(
                IntelligenceErrors.EngineFault(ModelId, nameof(EmbedAsync), "SimulateFailure enabled")));
        }

        var vector = ComputeVector(text);
        _embeddedTexts.Enqueue(text);

        var result = new EmbeddingResult
        {
            Vector = vector,
            ModelId = ModelId,
            Dimension = Dimension,
            TokenUsage = BuildTokenUsage(text),
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<EmbeddingResult>.Success(result));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<EmbeddingBatchResult>> EmbedManyAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<EmbeddingBatchResult>.Failure(
                IntelligenceErrors.EngineFault(ModelId, nameof(EmbedManyAsync), "SimulateFailure enabled")));
        }

        var embeddings = new List<ReadOnlyMemory<float>>(texts.Count);
        var promptTokens = 0;

        foreach (var text in texts)
        {
            embeddings.Add(ComputeVector(text));
            promptTokens += CountWords(text);
            _embeddedTexts.Enqueue(text);
        }

        var result = new EmbeddingBatchResult
        {
            Embeddings = embeddings,
            ModelId = ModelId,
            Dimension = Dimension,
            TokenUsage = new TokenUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = 0,
                TotalTokens = promptTokens,
            },
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<EmbeddingBatchResult>.Success(result));
    }

    /// <summary>Clears <see cref="EmbeddedTexts"/>.</summary>
    public void Reset() => _embeddedTexts.Clear();

    private ReadOnlyMemory<float> ComputeVector(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ModelId + text));
        var seed = BitConverter.ToInt32(hash, 0);
        var rng = new Random(seed);

        var vector = new float[Dimension];
        for (var i = 0; i < Dimension; i++)
        {
            vector[i] = (float)((rng.NextDouble() * 2.0) - 1.0);
        }

        return vector;
    }

    private static TokenUsage BuildTokenUsage(string text)
    {
        var promptTokens = CountWords(text);
        return new TokenUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = 0,
            TotalTokens = promptTokens,
        };
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
