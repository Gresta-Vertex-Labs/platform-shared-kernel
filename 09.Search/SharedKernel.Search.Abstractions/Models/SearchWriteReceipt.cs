namespace SharedKernel.Search.Abstractions.Models;

/// <summary>The acknowledgement returned by a single-document or batch write.</summary>
/// <remarks>
/// <see cref="ProviderToken"/> is opaque — a Meilisearch <c>taskUid</c> as a string, or an
/// ElasticSearch <c>"{index}:{seqNo}:{primaryTerm}"</c>. Consumers must never parse it. Its only
/// legal use is being handed back to <c>ISearchIndex&lt;TDocument&gt;.WaitUntilSearchableAsync</c> on
/// the same <c>ISearchIndex&lt;TDocument&gt;</c> instance within the same process. Modelling it as one
/// opaque string rather than two typed engine-specific fields is what keeps two fundamentally
/// different acknowledgement models behind one honest contract.
/// </remarks>
public sealed record SearchWriteReceipt
{
    /// <summary>Gets the name of the index this receipt applies to.</summary>
    public required string IndexName { get; init; }

    /// <summary>
    /// Gets the opaque provider token for this write. Never parse this value — see the type-level
    /// remarks.
    /// </summary>
    public required string ProviderToken { get; init; }

    /// <summary>Gets the number of documents affected by this write.</summary>
    public required int AffectedCount { get; init; }

    /// <summary>Gets the consistency the caller requested for this write.</summary>
    public required SearchWriteConsistency RequestedConsistency { get; init; }

    /// <summary>
    /// Gets the time this write was accepted by the provider, sourced from <c>IClock</c> — never
    /// <c>DateTimeOffset.UtcNow</c> directly.
    /// </summary>
    public required DateTimeOffset AcceptedAt { get; init; }
}
