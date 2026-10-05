namespace SharedKernel.Search.Abstractions.Models;

/// <summary>What a provider measured about one index, turned into a readiness report by <see cref="Abstractions.SearchIndexReadinessProbe"/>.</summary>
/// <remarks>
/// <c>09.Search</c> ships no <c>IHealthCheck</c> implementation; mapping the probes into
/// <c>AddHealthChecks()</c> is <c>13.ServiceDefaults</c>'s <c>AddSharedKernelReadiness()</c>.
/// </remarks>
public sealed record SearchIndexHealth
{
    /// <summary>
    /// Gets a value indicating whether the search engine itself is reachable (e.g. Meilisearch
    /// <c>/health</c>, ElasticSearch cluster health).
    /// </summary>
    public required bool Reachable { get; init; }

    /// <summary>
    /// Gets a value indicating whether the specific index/alias this service queries is addressable
    /// using this service's own credentials.
    /// </summary>
    /// <remarks>
    /// The single most commonly omitted readiness assertion: a green ElasticSearch cluster with a
    /// missing or misnamed read alias passes every cluster-health check and returns 100% production
    /// failures. Likewise Meilisearch's <c>/health</c> is instance-wide and says nothing about
    /// whether this service's API key is scoped to this index.
    /// </remarks>
    public required bool IndexAddressable { get; init; }

    /// <summary>Gets a value indicating whether a real zero-row search against the index succeeded.</summary>
    public required bool Searchable { get; init; }

    /// <summary>Gets the number of documents currently in the index.</summary>
    public required long DocumentCount { get; init; }

    /// <summary>
    /// Gets the number of writes not yet visible to search, or <see langword="null"/> when the
    /// provider has no equivalent scalar.
    /// </summary>
    /// <remarks>
    /// Permanently nullable, not a TODO — ElasticSearch has no equivalent scalar, and returning 0 for
    /// it would make a "backlog is zero" alert meaningless. A deep backlog is a metric/alert, never a
    /// readiness failure: it means results are stale, not unavailable, and failing readiness would
    /// remove serving capacity exactly when it is most needed.
    /// </remarks>
    public required long? PendingWriteCount { get; init; }

    /// <summary>Gets the search engine's reported version string.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>
    /// Gets the schema fingerprint recorded in index metadata at provisioning time, or
    /// <see langword="null"/> when none was recorded.
    /// </summary>
    public string? SchemaFingerprint { get; init; }

    /// <summary>Gets the total elapsed time this probe took.</summary>
    public required TimeSpan Latency { get; init; }
}
