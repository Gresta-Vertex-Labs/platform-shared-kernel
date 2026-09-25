using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// The readiness probe of one registered vector collection. A vector-store provider registers one per collection it
/// was given, named <see cref="ProbeNameFor(string, string)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Ready means the store is reachable, the collection is addressable with this service's credentials, and it answers
/// queries. A write backlog never fails readiness: it means results may be stale, not unavailable.
/// </para>
/// <para>
/// The report's <see cref="ReadinessReport.Data"/> carries the measured <see cref="VectorCollectionHealth"/> under
/// the keys declared on this class; <see cref="PendingWriteCountKey"/> and <see cref="SchemaFingerprintKey"/> are
/// absent when the provider has no value for them. A failed probe carries <see cref="ErrorCodeKey"/> instead.
/// </para>
/// </remarks>
public sealed class VectorCollectionReadinessProbe : IReadinessProbe
{
    /// <summary>Data key: the provider name (<see cref="string"/>).</summary>
    public const string ProviderKey = "Provider";

    /// <summary>Data key: the collection name (<see cref="string"/>).</summary>
    public const string CollectionKey = "Collection";

    /// <summary>Data key: <see cref="VectorCollectionHealth.Reachable"/> (<see cref="bool"/>).</summary>
    public const string ReachableKey = "Reachable";

    /// <summary>Data key: <see cref="VectorCollectionHealth.CollectionAddressable"/> (<see cref="bool"/>).</summary>
    public const string CollectionAddressableKey = "CollectionAddressable";

    /// <summary>Data key: <see cref="VectorCollectionHealth.Queryable"/> (<see cref="bool"/>).</summary>
    public const string QueryableKey = "Queryable";

    /// <summary>Data key: <see cref="VectorCollectionHealth.VectorCount"/> (<see cref="long"/>).</summary>
    public const string VectorCountKey = "VectorCount";

    /// <summary>Data key: <see cref="VectorCollectionHealth.PendingWriteCount"/> (<see cref="long"/>), when known.</summary>
    public const string PendingWriteCountKey = "PendingWriteCount";

    /// <summary>Data key: <see cref="VectorCollectionHealth.EngineVersion"/> (<see cref="string"/>).</summary>
    public const string EngineVersionKey = "EngineVersion";

    /// <summary>Data key: <see cref="VectorCollectionHealth.SchemaFingerprint"/> (<see cref="string"/>), when recorded.</summary>
    public const string SchemaFingerprintKey = "SchemaFingerprint";

    /// <summary>Data key: the error code of a probe that could not measure the collection.</summary>
    public const string ErrorCodeKey = "ErrorCode";

    private readonly string _providerName;
    private readonly string _collectionName;
    private readonly Func<string, CancellationToken, Task<Result<VectorCollectionHealth>>> _probe;

    /// <summary>Creates the probe of one collection.</summary>
    /// <param name="providerName">The provider name, e.g. <see cref="Constants.IntelligenceWellKnown.QdrantProviderName"/>.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="probe">Measures the collection; returns a failed result, never throws, when it cannot.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="providerName"/> or <paramref name="collectionName"/> is <see langword="null"/>, empty or
    /// whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="probe"/> is <see langword="null"/>.</exception>
    public VectorCollectionReadinessProbe(
        string providerName,
        string collectionName,
        Func<string, CancellationToken, Task<Result<VectorCollectionHealth>>> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentNullException.ThrowIfNull(probe);

        _providerName = providerName;
        _collectionName = collectionName;
        _probe = probe;
        Name = ProbeNameFor(providerName, collectionName);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// The probe name of <paramref name="collectionName"/> on <paramref name="providerName"/>:
    /// <c>vector-store-{provider}-{collection}</c>.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <returns>The probe name.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="providerName"/> or <paramref name="collectionName"/> is <see langword="null"/>, empty or
    /// whitespace.
    /// </exception>
    public static string ProbeNameFor(string providerName, string collectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        return $"vector-store-{providerName}-{collectionName}";
    }

    /// <inheritdoc />
    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _probe(_collectionName, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return ReadinessReport.Unhealthy(
                result.Error.Message,
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [ProviderKey] = _providerName,
                    [CollectionKey] = _collectionName,
                    [ErrorCodeKey] = result.Error.Code,
                });
        }

        var health = result.Value;
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [ProviderKey] = _providerName,
            [CollectionKey] = _collectionName,
            [ReachableKey] = health.Reachable,
            [CollectionAddressableKey] = health.CollectionAddressable,
            [QueryableKey] = health.Queryable,
            [VectorCountKey] = health.VectorCount,
            [EngineVersionKey] = health.EngineVersion,
        };
        if (health.PendingWriteCount is { } pending)
            data[PendingWriteCountKey] = pending;
        if (health.SchemaFingerprint is { } fingerprint)
            data[SchemaFingerprintKey] = fingerprint;

        return health is { Reachable: true, CollectionAddressable: true, Queryable: true }
            ? ReadinessReport.Healthy("Vector collection reachable, addressable, and queryable.", data, health.Latency)
            : ReadinessReport.Unhealthy(
                $"Vector collection '{_collectionName}' not ready (Reachable={health.Reachable}, "
                + $"CollectionAddressable={health.CollectionAddressable}, Queryable={health.Queryable}).",
                data,
                health.Latency);
    }
}
