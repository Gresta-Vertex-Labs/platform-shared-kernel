namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>A request to atomically cut a staging collection over to serve as the live collection.</summary>
/// <remarks>
/// Both Qdrant and Milvus genuinely support native collection aliases, so
/// <see cref="LiveCollectionName"/> is an alias on both providers and the swap is atomic on both.
/// <see cref="DeleteStagingAfterCutover"/> (default <see langword="true"/>) still exists because the
/// staging collection's underlying data remains allocated until explicitly deleted on both engines.
/// </remarks>
public sealed record VectorCollectionCutoverRequest
{
    /// <summary>Gets the staging collection's name.</summary>
    public required string StagingCollectionName { get; init; }

    /// <summary>Gets the live (alias) collection's name.</summary>
    public required string LiveCollectionName { get; init; }

    /// <summary>Gets a value indicating whether the staging collection is deleted after a successful cutover.</summary>
    public bool DeleteStagingAfterCutover { get; init; } = true;
}
