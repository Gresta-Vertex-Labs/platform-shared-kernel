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
    /// <param name="name">
    /// The health check registration name. Defaults to <see cref="HealthCheckNames.Search"/> suffixed
    /// with <paramref name="indexName"/> — <c>"search-products"</c>, not <c>"search"</c>.
    /// <para>
    /// <b>The suffix is what makes the obvious usage work.</b> A service with more than one index calls
    /// this once per index, which is exactly what the parameter invites; a constant default made the
    /// second call throw <c>ArgumentException: Duplicate health checks were registered with the
    /// name(s): search</c> at <c>MapDefaultHealthCheckEndpoints()</c> — at startup, with a message that
    /// names the framework rather than the call that caused it. Every registration is now distinct by
    /// construction, and the per-index name is also what an operator wants to see in the health
    /// response body: <c>search-products</c> says which index is unhealthy, <c>search</c> does not.
    /// </para>
    /// <para>
    /// Pass an explicit value to override it. Two calls with the same explicit name still collide —
    /// that is the framework's rule, not this method's.
    /// </para>
    /// </param>
    /// <param name="providerKey">
    /// The provider to probe with, when a host registers more than one. Leave <see langword="null"/> —
    /// the overwhelmingly common case — to resolve <see cref="ISearchIndexProvisioner"/> unkeyed. Pass
    /// <c>SearchWellKnown.MeilisearchProviderName</c> or <c>SearchWellKnown.ElasticSearchProviderName</c>
    /// in a host running both engines; see the remarks on <c>ResolveProvisioner</c> for why an unkeyed
    /// resolution silently picks the wrong one there.
    /// </param>
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
        string? name = null,
        string? providerKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        // Null rather than a constant default, so the per-index name can be composed from a parameter.
        var resolvedName = name ?? $"{HealthCheckNames.Search}-{indexName}";
        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Search];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.SearchReadinessHealthCheckExtensions",
            resolvedName,
            tags);

        return builder.Add(new HealthCheckRegistration(
            resolvedName,
            sp => new SearchReadinessHealthCheck(ResolveProvisioner(sp, providerKey), indexName),
            failureStatus: null,
            tags: tags));
    }

    /// <summary>
    /// Resolves the provisioner to probe with — keyed when <paramref name="providerKey"/> is supplied,
    /// unkeyed otherwise.
    /// </summary>
    /// <remarks>
    /// Unkeyed is right for the overwhelmingly common single-provider service, and keeps the call site
    /// free of a provider name it would otherwise have to know. The key matters only in a host that
    /// registers both engines: <see cref="ISearchIndexProvisioner"/> is non-generic, so the second
    /// registration shadows the first and an unkeyed resolution silently returns whichever provider was
    /// registered last. The symptom is remote from the cause — the check asks ElasticSearch about a
    /// Meilisearch index, gets "not addressable", and reports a perfectly healthy service unready
    /// forever. Each provider package registers its own provisioner under
    /// <c>SearchWellKnown.MeilisearchProviderName</c> / <c>SearchWellKnown.ElasticSearchProviderName</c>
    /// as well as unkeyed, and both resolutions return the same instance.
    /// </remarks>
    private static ISearchIndexProvisioner ResolveProvisioner(IServiceProvider services, string? providerKey)
        => providerKey is null
            ? services.GetRequiredService<ISearchIndexProvisioner>()
            : services.GetRequiredKeyedService<ISearchIndexProvisioner>(providerKey);
}
