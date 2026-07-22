namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The result of probing a vector collection's readiness.</summary>
/// <remarks>
/// <para>
/// <b><see cref="CollectionAddressable"/> is separate from <see cref="Reachable"/>:</b> a reachable
/// cluster with a missing or mis-aliased collection, or a mis-scoped API key for this collection
/// specifically, passes a cluster-wide health check and returns 100% production failures — the
/// identical <c>SearchIndexHealth</c> precedent from <c>09.Search</c>, applied here.
/// </para>
/// <para>
/// <b><see cref="PendingWriteCount"/> is nullable, permanently:</b> Qdrant's optimizer-status
/// pending-operations signal and Milvus's segment-flush backlog are not the same shape, and one engine
/// may expose nothing comparable at all depending on version. Nullability models the capability gap
/// honestly rather than reporting a fabricated <c>0</c>. <c>13.ServiceDefaults</c> must not treat a
/// deep backlog as a readiness failure — it means results may be stale, not unavailable.
/// </para>
/// </remarks>
public sealed record VectorCollectionHealth
{
    /// <summary>Gets a value indicating whether the underlying cluster/instance is reachable.</summary>
    public required bool Reachable { get; init; }

    /// <summary>Gets a value indicating whether the specific collection is addressable with the caller's own credentials.</summary>
    public required bool CollectionAddressable { get; init; }

    /// <summary>Gets a value indicating whether the collection currently answers queries.</summary>
    public required bool Queryable { get; init; }

    /// <summary>Gets the collection's current vector count.</summary>
    public required long VectorCount { get; init; }

    /// <summary>
    /// Gets the number of pending write operations, or <see langword="null"/> when the provider
    /// exposes no comparable signal. Permanently nullable — never fabricated as <c>0</c>.
    /// </summary>
    public long? PendingWriteCount { get; init; }

    /// <summary>Gets the connected engine's reported version.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>Gets the collection's recorded schema fingerprint, or <see langword="null"/> when unavailable.</summary>
    public string? SchemaFingerprint { get; init; }

    /// <summary>Gets the total elapsed probe duration.</summary>
    public required TimeSpan Latency { get; init; }
}
