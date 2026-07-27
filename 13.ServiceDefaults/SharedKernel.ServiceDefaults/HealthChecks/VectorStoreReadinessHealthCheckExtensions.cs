using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.AI.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in vector-store connectivity health check, wrapping <c>10.Intelligence</c>'s
/// <see cref="IVectorCollectionProvisioner.ProbeAsync"/> probe.
/// </summary>
public static class VectorStoreReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies vector-collection connectivity for
    /// <paramref name="collectionName"/> via the <see cref="IVectorCollectionProvisioner"/> resolved
    /// from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="collectionName">
    /// The collection to probe. A required, explicit parameter — deliberately never inferred from a
    /// concrete provider options type (<c>QdrantOptions</c>/<c>MilvusOptions</c>), since doing so
    /// would reintroduce exactly the provider-specific coupling this method exists to avoid. Mirrors
    /// <see cref="SearchReadinessHealthCheckExtensions.AddSearchReadinessCheck"/>'s
    /// <c>indexName</c>-parameter precedent exactly.
    /// </param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.VectorStore"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.VectorStore"/>,
    /// never <see cref="HealthCheckTags.Live"/>. Resolves only
    /// <see cref="IVectorCollectionProvisioner"/> from DI — works uniformly against whichever
    /// provider (<c>SharedKernel.AI.Qdrant</c> or <c>SharedKernel.AI.Milvus</c>) a service has
    /// registered, with zero provider-specific branching in this package. Reports
    /// <see cref="HealthStatus.Unhealthy"/> — never <see cref="HealthStatus.Degraded"/> — when the
    /// underlying probe fails or reports the collection as unreachable, unaddressable, or
    /// unqueryable. A deep <c>PendingWriteCount</c> backlog is surfaced only as informational data
    /// and is never treated as a readiness failure. Opt-in only — never registered by
    /// <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddVectorStoreReadinessCheck(
        this IHealthChecksBuilder builder,
        string collectionName,
        string name = HealthCheckNames.VectorStore)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new VectorStoreReadinessHealthCheck(
                sp.GetRequiredService<IVectorCollectionProvisioner>(),
                collectionName),
            failureStatus: null,
            tags: [HealthCheckTags.Ready, HealthCheckTags.VectorStore]));
    }
}
