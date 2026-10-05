namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The result of embedding a batch of texts in one logical call.</summary>
/// <remarks>
/// <b>Order-preserving, no per-item failure surface — unlike a search bulk receipt, deliberately:</b>
/// <c>Embeddings[i]</c> corresponds to the <c>i</c>-th input text. Embedding-provider batch APIs are
/// atomic per request (the whole call embeds every item or the call fails), unlike a search engine's
/// bulk endpoint which routinely partial-fails. A failed <see cref="SharedKernel.Primitives.Results.Result{T}"/>
/// is therefore sufficient; there is no per-item failure type. A provider that genuinely offers native
/// partial-batch failure exposes it as a provider-exclusive contract, never faked here.
/// </remarks>
public sealed record EmbeddingBatchResult
{
    /// <summary>Gets the produced embedding vectors, in the same order as the input texts.</summary>
    public required IReadOnlyList<ReadOnlyMemory<float>> Embeddings { get; init; }

    /// <summary>Gets the identifier of the model that produced <see cref="Embeddings"/>.</summary>
    public required string ModelId { get; init; }

    /// <summary>Gets the dimension of every vector in <see cref="Embeddings"/>.</summary>
    public required int Dimension { get; init; }

    /// <summary>Gets the token usage this call billed.</summary>
    public required TokenUsage TokenUsage { get; init; }
}
