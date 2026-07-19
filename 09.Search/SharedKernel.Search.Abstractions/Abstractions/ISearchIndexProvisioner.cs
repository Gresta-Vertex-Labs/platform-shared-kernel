using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Abstractions;

/// <summary>
/// The neutral, non-generic contract for provisioning, cutting over, and probing search indexes — one
/// implementation registered per provider.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="EnsureIndexAsync"/> is idempotent and additive-only:</b> it creates the index if
/// absent and applies the field declarations. It never drops a field and never rewrites an
/// incompatible mapping — ElasticSearch cannot change an existing field's type in place, so a truly
/// convergent "make it match" is unimplementable on one engine. A definition conflicting with the live
/// mapping returns a conflict error; the remedy is staging, bulk-load, then <see cref="CutoverAsync"/>.
/// It also writes the definition's schema fingerprint into index metadata so
/// <see cref="ProbeAsync"/> can detect drift.
/// </para>
/// <para>
/// <b><see cref="CutoverAsync"/></b> is one neutral name over two mechanics — see
/// <see cref="IndexCutoverRequest"/>'s own remarks for the ElasticSearch-alias-versus-Meilisearch-swap
/// asymmetry.
/// </para>
/// <para>
/// <b>What this does not own:</b> the rebuild itself. There is no rebuild-driver member, because the
/// aggregate-to-search-document mapping is business logic belonging to the owning service, and a
/// rebuild source would force a <c>06.Persistence</c> or <c>07.Messaging</c> reference this layer may
/// not take. The consumer sequences four calls itself: <see cref="EnsureIndexAsync"/> against a
/// staging index, its own bulk load, <see cref="CutoverAsync"/>, then an optional
/// <see cref="DeleteIndexAsync"/> for the staging index.
/// </para>
/// <para>
/// <b><see cref="ProbeAsync"/> is a primitive, not a health check:</b> <c>09.Search</c> ships no
/// <c>IHealthCheck</c> implementation and neither provider references
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>. Wiring into <c>AddHealthChecks()</c> is
/// <c>13.ServiceDefaults</c>'s concern.
/// </para>
/// </remarks>
public interface ISearchIndexProvisioner
{
    /// <summary>Creates <paramref name="definition"/>'s index if absent and applies its field declarations.</summary>
    Task<Result> EnsureIndexAsync(SearchIndexDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Returns whether an index named <paramref name="indexName"/> exists.</summary>
    Task<Result<bool>> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the index named <paramref name="indexName"/>.</summary>
    Task<Result> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default);

    /// <summary>Atomically cuts a staging index over to serve as the live index.</summary>
    Task<Result> CutoverAsync(IndexCutoverRequest request, CancellationToken cancellationToken = default);

    /// <summary>Probes the readiness of the index named <paramref name="indexName"/>.</summary>
    Task<Result<SearchIndexHealth>> ProbeAsync(string indexName, CancellationToken cancellationToken = default);
}
