using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharedKernel.Application.Behaviors.Caching.Shared;

/// <summary>
/// Holds this package's cache instruments, recorded under the <c>05.Application</c> domain meter.
/// </summary>
/// <remarks>
/// <para>
/// Uses the same <c>"SharedKernel.Application"</c> meter name as <c>SharedKernel.Application.Behaviors</c>,
/// so <c>13.ServiceDefaults</c>' existing <c>WithApplicationTelemetry()</c> exports these
/// instruments with no new registration and no change to that package. A second meter name would
/// have needed its own <c>AddMeter</c> call and silently exported nothing until a host added it.
/// </para>
/// <para>
/// A DI-created singleton resolved from <see cref="IMeterFactory"/> — not a static field — matching
/// <c>ApplicationMetrics</c>. Registered by <c>AddCachingBehaviors()</c> alongside
/// <c>services.AddMetrics()</c> so an <see cref="IMeterFactory"/> is always resolvable.
/// </para>
/// </remarks>
internal sealed class CachingBehaviorsMetrics
{
    /// <summary>The meter name every instrument in this domain is recorded under.</summary>
    internal const string MeterName = "SharedKernel.Application";

    /// <summary>The instrument name for query cache outcomes.</summary>
    internal const string QueryCacheOutcomeName = "sharedkernel.application.query.cache.outcome";

    /// <summary>The instrument name for post-commit cache evictions.</summary>
    internal const string InvalidationName = "sharedkernel.application.command.cache.invalidation";

    private readonly Counter<long> _queryCacheOutcome;
    private readonly Counter<long> _invalidation;

    /// <summary>Initialises a new <see cref="CachingBehaviorsMetrics"/>.</summary>
    /// <param name="meterFactory">The factory used to create this domain's <see cref="Meter"/>.</param>
    public CachingBehaviorsMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);

        _queryCacheOutcome = meter.CreateCounter<long>(
            QueryCacheOutcomeName,
            unit: "{query}",
            description: "Query cache outcomes, tagged by query type, scope and outcome. Hit ratio per query type is hit / (hit + miss).");

        _invalidation = meter.CreateCounter<long>(
            InvalidationName,
            unit: "{entry}",
            description: "Post-commit cache evictions, tagged by command type and whether the eviction succeeded.");
    }

    internal void RecordQueryOutcome(string queryType, CacheScope scope, CacheOutcome outcome)
    {
        var tags = new TagList
        {
            { "sharedkernel.query.type", queryType },
            { "sharedkernel.cache.scope", scope.ToString() },
            { "sharedkernel.cache.outcome", outcome.ToString() },
        };

        _queryCacheOutcome.Add(1, tags);
    }

    internal void RecordInvalidation(string commandType, string target, bool succeeded)
    {
        var tags = new TagList
        {
            { "sharedkernel.command.type", commandType },
            { "sharedkernel.cache.target", target },
            { "sharedkernel.cache.evicted", succeeded },
        };

        _invalidation.Add(1, tags);
    }
}
