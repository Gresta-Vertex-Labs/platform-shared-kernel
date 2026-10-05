using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// The neutral contract for text-to-vector embedding generation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately non-generic</b> — this domain is scoped to text embedding for retrieval/RAG, not
/// multi-modal embedding. This is the deliberate deviation from
/// <c>Microsoft.Extensions.AI.IEmbeddingGenerator&lt;TInput,TEmbedding&gt;</c> and is why that type was
/// not adopted verbatim, even informally, for <c>SharedKernel.AI.Abstractions</c>.
/// </para>
/// <para>
/// <see cref="ModelId"/>/<see cref="Dimension"/> are zero-I/O properties bound at construction (from
/// options), mirroring <c>ISearchIndex.IndexName</c> — cheap enough to read at composition time to
/// validate a <c>VectorCollectionDefinition.EmbeddingModelId</c>/<c>.Dimension</c> pairing before any
/// embedding call is ever made.
/// </para>
/// <para>
/// <b>Batch size is a provider-descriptor concern, not here:</b> <see cref="EmbedManyAsync"/> does not
/// itself cap the input count — over-ceiling batches are the concrete provider's own responsibility to
/// validate against its own known ceiling before any I/O, returning
/// <c>IntelligenceErrors.BatchSizeExceeded</c>.
/// </para>
/// </remarks>
public interface IEmbeddingGenerator
{
    /// <summary>Gets the identifier of the embedding model this generator is bound to.</summary>
    string ModelId { get; }

    /// <summary>Gets the vector dimension this generator produces.</summary>
    int Dimension { get; }

    /// <summary>Embeds a single piece of text.</summary>
    Task<Result<EmbeddingResult>> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embeds a batch of texts in one logical call.</summary>
    Task<Result<EmbeddingBatchResult>> EmbedManyAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
