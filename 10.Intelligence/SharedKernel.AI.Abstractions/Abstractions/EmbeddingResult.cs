namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The result of embedding a single piece of text.</summary>
public sealed record EmbeddingResult
{
    /// <summary>Gets the produced embedding vector.</summary>
    public required ReadOnlyMemory<float> Vector { get; init; }

    /// <summary>Gets the identifier of the model that produced <see cref="Vector"/>.</summary>
    public required string ModelId { get; init; }

    /// <summary>Gets the dimension of <see cref="Vector"/>.</summary>
    public required int Dimension { get; init; }

    /// <summary>Gets the token usage this call billed.</summary>
    public required TokenUsage TokenUsage { get; init; }
}
