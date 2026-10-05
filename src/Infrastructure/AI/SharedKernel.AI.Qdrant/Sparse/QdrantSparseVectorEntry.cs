namespace SharedKernel.AI.Qdrant.Sparse;

/// <summary>One non-zero dimension of a sparse vector: a dimension index paired with its weight.</summary>
public readonly record struct QdrantSparseVectorEntry
{
    /// <summary>Gets the sparse dimension index.</summary>
    public required uint Index { get; init; }

    /// <summary>Gets the weight at <see cref="Index"/>.</summary>
    public required float Value { get; init; }
}
