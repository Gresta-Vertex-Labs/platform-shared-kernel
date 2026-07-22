using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// The neutral, non-generic contract for provisioning, cutting over, and probing vector collections —
/// one implementation registered per provider.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="EnsureCollectionAsync"/> is idempotent and additive-only:</b> creates the collection if
/// absent and applies the field declarations (payload/scalar indexes on <c>Filterable</c> fields). It
/// never rewrites an incompatible existing definition — a definition conflicting with the live
/// collection returns <c>IntelligenceErrors.CollectionDefinitionConflict</c>; the remedy is
/// staging → <c>UpsertManyAsync</c> → <see cref="CutoverAsync"/>. It also persists
/// <see cref="VectorCollectionDefinition.Fingerprint"/> so <see cref="ProbeAsync"/> can detect drift —
/// Qdrant via a reserved sentinel point's payload, Milvus via a native collection property — each
/// provider's concrete persistence mechanism is a Core-phase decision for that provider.
/// </para>
/// <para>
/// <b><see cref="CutoverAsync"/>:</b> both Qdrant and Milvus genuinely have native collection aliases,
/// so <c>LiveCollectionName</c> is an alias on both providers and the swap is atomic on both.
/// </para>
/// <para>
/// <b>What this does not own:</b> the re-embedding/rebuild itself. There is no rebuild-driver member,
/// because the source-entity → embedding mapping is business logic belonging to the owning service, and
/// a rebuild source would force a <c>06.Persistence</c> or <c>07.Messaging</c> reference this layer may
/// not take. The consumer sequences: <see cref="EnsureCollectionAsync"/>(staging) →
/// <c>UpsertManyAsync</c>(from its own <see cref="IAsyncEnumerable{T}"/>, fed through its own
/// <see cref="IEmbeddingGenerator"/> calls) → <see cref="CutoverAsync"/> → optional
/// <see cref="DeleteCollectionAsync"/>.
/// </para>
/// <para>
/// <b><see cref="ProbeAsync"/> is a primitive, not a health check:</b> <c>10.Intelligence</c> ships no
/// <c>IHealthCheck</c> implementation and no provider references
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>. Wiring into <c>AddHealthChecks()</c> is
/// <c>13.ServiceDefaults</c>'s concern.
/// </para>
/// </remarks>
public interface IVectorCollectionProvisioner
{
    /// <summary>Creates <paramref name="definition"/>'s collection if absent and applies its field declarations.</summary>
    Task<Result> EnsureCollectionAsync(VectorCollectionDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Returns whether a collection named <paramref name="collectionName"/> exists.</summary>
    Task<Result<bool>> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the collection named <paramref name="collectionName"/>.</summary>
    Task<Result> DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>Atomically cuts a staging collection over to serve as the live collection.</summary>
    Task<Result> CutoverAsync(VectorCollectionCutoverRequest request, CancellationToken cancellationToken = default);

    /// <summary>Probes the readiness of the collection named <paramref name="collectionName"/>.</summary>
    Task<Result<VectorCollectionHealth>> ProbeAsync(string collectionName, CancellationToken cancellationToken = default);
}
