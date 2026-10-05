using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Abstractions;

/// <summary>
/// The neutral, non-generic contract for provisioning, cutting over, and verifying search indexes — one
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
/// <see cref="VerifyRegisteredIndexesAsync"/> and each index's readiness probe can detect drift.
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
/// <b>Readiness is not on this contract:</b> each provider registers one <see cref="SearchIndexReadinessProbe"/> per
/// registered index (named <see cref="SearchIndexReadinessProbe.ProbeNameFor(string, string)"/>). <c>09.Search</c> ships no
/// <c>IHealthCheck</c> implementation and neither provider references
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>. Mapping the probes into <c>AddHealthChecks()</c> is
/// <c>13.ServiceDefaults</c>'s <c>AddSharedKernelReadiness()</c>.
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

    /// <summary>
    /// Verifies every index registered with this provider against the live engine: that it exists, is
    /// addressable with this service's credentials, and carries the schema fingerprint of the
    /// <see cref="SearchIndexDefinition"/> the composition root declared for it.
    /// </summary>
    /// <returns>
    /// A successful <see cref="Result"/> when every registered index matches; otherwise a failed
    /// <see cref="Result"/> whose <see cref="Primitives.Errors.Error"/> names every index that is
    /// missing, unreachable, or drifted.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The deployment check this domain previously only claimed to have.</b> The failure it catches
    /// is a real and quiet one: code ships declaring a field, a synonym list or a stop-word list that
    /// the live index was never rebuilt for, so filters silently match nothing and relevance silently
    /// changes. <see cref="EnsureIndexAsync"/> catches it only if something calls it, and
    /// an index's readiness probe catches it only for one index at a time and only if the caller already
    /// knows the expected fingerprint — this member needs neither, because the provider already holds
    /// every registered definition.
    /// </para>
    /// <para>
    /// <b>Asynchronous and explicitly invoked, never a hidden startup side effect.</b> It is a real
    /// network round trip per index, so it is called deliberately — from a startup task, a readiness
    /// check wired by <c>13.ServiceDefaults</c>, or a deployment smoke test — rather than fired
    /// implicitly from a DI factory, which would block a thread on I/O at first resolution and could
    /// land mid-request. It replaces the former <c>MeilisearchOptions.ValidateIndexSettingsOnStart</c>
    /// flag, which was declared, defaulted to <see langword="true"/>, documented as doing exactly this,
    /// and read by nothing.
    /// </para>
    /// </remarks>
    Task<Result> VerifyRegisteredIndexesAsync(CancellationToken cancellationToken = default);
}
