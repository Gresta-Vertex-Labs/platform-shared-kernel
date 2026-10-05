using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Abstractions;

/// <summary>
/// The readiness probe of one registered search index. Each provider registers one per index it was given, so a
/// host running both engines gets one probe per engine and index, named
/// <see cref="ProbeNameFor(string, string)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Ready means the engine is reachable, the index is addressable with this service's credentials, and a zero-row
/// search against it succeeded. A write backlog never fails readiness: it means results are stale, not unavailable.
/// </para>
/// <para>
/// The report's <see cref="ReadinessReport.Data"/> carries the measured <see cref="SearchIndexHealth"/> under the
/// keys declared on this class; <see cref="PendingWriteCountKey"/> and <see cref="SchemaFingerprintKey"/> are absent
/// when the provider has no value for them. A failed probe carries <see cref="ErrorCodeKey"/> instead.
/// </para>
/// </remarks>
public sealed class SearchIndexReadinessProbe : IReadinessProbe
{
    /// <summary>Data key: the provider name (<see cref="string"/>).</summary>
    public const string ProviderKey = "Provider";

    /// <summary>Data key: the index name (<see cref="string"/>).</summary>
    public const string IndexKey = "Index";

    /// <summary>Data key: <see cref="SearchIndexHealth.Reachable"/> (<see cref="bool"/>).</summary>
    public const string ReachableKey = "Reachable";

    /// <summary>Data key: <see cref="SearchIndexHealth.IndexAddressable"/> (<see cref="bool"/>).</summary>
    public const string IndexAddressableKey = "IndexAddressable";

    /// <summary>Data key: <see cref="SearchIndexHealth.Searchable"/> (<see cref="bool"/>).</summary>
    public const string SearchableKey = "Searchable";

    /// <summary>Data key: <see cref="SearchIndexHealth.DocumentCount"/> (<see cref="long"/>).</summary>
    public const string DocumentCountKey = "DocumentCount";

    /// <summary>Data key: <see cref="SearchIndexHealth.PendingWriteCount"/> (<see cref="long"/>), when known.</summary>
    public const string PendingWriteCountKey = "PendingWriteCount";

    /// <summary>Data key: <see cref="SearchIndexHealth.EngineVersion"/> (<see cref="string"/>).</summary>
    public const string EngineVersionKey = "EngineVersion";

    /// <summary>Data key: <see cref="SearchIndexHealth.SchemaFingerprint"/> (<see cref="string"/>), when recorded.</summary>
    public const string SchemaFingerprintKey = "SchemaFingerprint";

    /// <summary>Data key: the <c>search.*</c> error code of a probe that could not measure the index.</summary>
    public const string ErrorCodeKey = "ErrorCode";

    private readonly string _providerName;
    private readonly string _indexName;
    private readonly Func<string, CancellationToken, Task<Result<SearchIndexHealth>>> _probe;

    /// <summary>Creates the probe of one index.</summary>
    /// <param name="providerName">The provider name, e.g. <see cref="Constants.SearchWellKnown.MeilisearchProviderName"/>.</param>
    /// <param name="indexName">The index name.</param>
    /// <param name="probe">Measures the index; returns a failed result, never throws, when it cannot.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="providerName"/> or <paramref name="indexName"/> is <see langword="null"/>, empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="probe"/> is <see langword="null"/>.</exception>
    public SearchIndexReadinessProbe(
        string providerName,
        string indexName,
        Func<string, CancellationToken, Task<Result<SearchIndexHealth>>> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentNullException.ThrowIfNull(probe);

        _providerName = providerName;
        _indexName = indexName;
        _probe = probe;
        Name = ProbeNameFor(providerName, indexName);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The probe name of <paramref name="indexName"/> on <paramref name="providerName"/>: <c>search-{provider}-{index}</c>.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="indexName">The index name.</param>
    /// <returns>The probe name.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="providerName"/> or <paramref name="indexName"/> is <see langword="null"/>, empty or whitespace.
    /// </exception>
    public static string ProbeNameFor(string providerName, string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        return $"search-{providerName}-{indexName}";
    }

    /// <inheritdoc />
    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _probe(_indexName, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return ReadinessReport.Unhealthy(
                result.Error.Message,
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [ProviderKey] = _providerName,
                    [IndexKey] = _indexName,
                    [ErrorCodeKey] = result.Error.Code,
                });
        }

        var health = result.Value;
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [ProviderKey] = _providerName,
            [IndexKey] = _indexName,
            [ReachableKey] = health.Reachable,
            [IndexAddressableKey] = health.IndexAddressable,
            [SearchableKey] = health.Searchable,
            [DocumentCountKey] = health.DocumentCount,
            [EngineVersionKey] = health.EngineVersion,
        };
        if (health.PendingWriteCount is { } pending)
            data[PendingWriteCountKey] = pending;
        if (health.SchemaFingerprint is { } fingerprint)
            data[SchemaFingerprintKey] = fingerprint;

        return health is { Reachable: true, IndexAddressable: true, Searchable: true }
            ? ReadinessReport.Healthy("Search index reachable, addressable, and searchable.", data, health.Latency)
            : ReadinessReport.Unhealthy(
                $"Search index '{_indexName}' not ready (Reachable={health.Reachable}, "
                + $"IndexAddressable={health.IndexAddressable}, Searchable={health.Searchable}).",
                data,
                health.Latency);
    }
}
