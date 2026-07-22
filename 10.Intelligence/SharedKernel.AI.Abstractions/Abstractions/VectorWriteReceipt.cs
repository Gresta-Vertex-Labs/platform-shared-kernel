namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The acknowledgement of a single write (or delete) operation against a vector collection.</summary>
/// <remarks>
/// <see cref="AcceptedAt"/> is sourced from <c>IClock</c> (<c>01.Core</c>) in both adapters — never
/// <c>DateTimeOffset.UtcNow</c> directly. <see cref="ProviderToken"/> is opaque (a Qdrant operation id,
/// or a Milvus insert timestamp used as a <c>guarantee_timestamp</c>) — consumers must never parse it;
/// its only legal use is being handed back to
/// <c>IVectorCollection{TRecord}.WaitUntilQueryableAsync</c> on the same
/// <c>IVectorCollection{TRecord}</c> instance.
/// </remarks>
public sealed record VectorWriteReceipt
{
    /// <summary>Gets the name of the collection this write targeted.</summary>
    public required string CollectionName { get; init; }

    /// <summary>Gets the provider's opaque operation token for this write.</summary>
    public required string ProviderToken { get; init; }

    /// <summary>Gets the number of records affected by this write.</summary>
    public required int AffectedCount { get; init; }

    /// <summary>Gets the instant this write was accepted by the provider.</summary>
    public required DateTimeOffset AcceptedAt { get; init; }
}
