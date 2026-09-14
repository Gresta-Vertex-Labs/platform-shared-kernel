using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Search.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in search-index connectivity health check, wrapping <c>09.Search</c>'s
/// <see cref="ISearchIndexProvisioner.ProbeAsync"/> probe.
/// </summary>
public static class SearchReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies search-index connectivity for
    /// <paramref name="indexName"/> via the <see cref="ISearchIndexProvisioner"/> resolved from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="indexName">
    /// The index to probe. A required, explicit parameter — deliberately never inferred from a
    /// concrete provider options type (<c>MeilisearchOptions</c>/<c>ElasticSearchOptions</c>), since
    /// doing so would reintroduce exactly the provider-specific coupling this method exists to avoid.
    /// Mirrors <c>StorageReadinessHealthCheckExtensions.AddStorageReadinessCheck</c>'s
    /// <c>bucket</c>-parameter precedent exactly.
    /// </param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Search"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Search"/>, never
    /// <see cref="HealthCheckTags.Live"/>. Resolves only <see cref="ISearchIndexProvisioner"/> from
    /// DI — works uniformly against whichever provider (<c>SharedKernel.Search.Meilisearch</c> or
    /// <c>SharedKernel.Search.ElasticSearch</c>) a service has registered, with zero provider-specific
    /// branching in this package. Reports <see cref="HealthStatus.Unhealthy"/> — never
    /// <see cref="HealthStatus.Degraded"/> — when the underlying probe fails or reports the index as
    /// unreachable, unaddressable, or unsearchable. A deep <c>PendingWriteCount</c> backlog is
    /// surfaced only as informational data and is never treated as a readiness failure. Opt-in
    /// only — never registered by <c>AddServiceDefaults()</c> or
    /// <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddSearchReadinessCheck(
        this IHealthChecksBuilder builder,
        string indexName,
        string name = HealthCheckNames.Search)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Search];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.SearchReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new SearchReadinessHealthCheck(sp.GetRequiredService<ISearchIndexProvisioner>(), indexName),
            failureStatus: null,
            tags: tags));
    }
}
