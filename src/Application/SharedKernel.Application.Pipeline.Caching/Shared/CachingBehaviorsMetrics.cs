using SharedKernel.Application.Caching;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Application.Pipeline.Tracing;

namespace SharedKernel.Application.Pipeline.Caching.Shared;

/// <summary>
/// Holds the cache instruments of this package, recorded under the <c>05.Application</c> domain meter.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="PipelineCachingDiagnostics.MeterName"/> (the value of <c>ApplicationDiagnostics.MeterName</c>), the same meter as <c>SharedKernel.Application.Pipeline</c>, so
/// the <c>SharedKernel.*</c> wildcard of <c>AddServiceDefaults</c> exports these instruments with no new registration.
/// </para>
/// <para>
/// A DI-created singleton resolved from <see cref="IMeterFactory"/> (not a static field), matching
/// <c>ApplicationMetrics</c>, and versioned like it. Registered by <c>WithCaching()</c> alongside
/// <c>services.AddMetrics()</c> so an <see cref="IMeterFactory"/> is always resolvable.
/// </para>
/// </remarks>
internal sealed class CachingBehaviorsMetrics
{
    /// <summary>The instrument name for query cache lookups.</summary>
    internal const string LookupsName = "sharedkernel.application.cache.lookups";

    /// <summary>The instrument name for post-commit cache evictions.</summary>
    internal const string EvictionsName = "sharedkernel.application.cache.evictions";

    private readonly Counter<long> _lookups;
    private readonly Counter<long> _evictions;

    /// <summary>Initialises a new <see cref="CachingBehaviorsMetrics"/>.</summary>
    /// <param name="meterFactory">The factory used to create the <see cref="Meter"/> of this domain.</param>
    public CachingBehaviorsMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(
            new MeterOptions(PipelineCachingDiagnostics.MeterName) { Version = ApplicationDiagnostics.TelemetryVersionText });

        _lookups = meter.CreateCounter<long>(
            LookupsName,
            unit: "{lookup}",
            description: "Query cache lookups, tagged by query type, scope and result. Hit ratio per query type is hit / (hit + miss).");

        _evictions = meter.CreateCounter<long>(
            EvictionsName,
            unit: "{eviction}",
            description: "Post-commit cache evictions, tagged by command type, target and whether the eviction succeeded.");
    }

    internal void RecordLookup(string queryType, CacheScope scope, CacheOutcome outcome)
    {
        var tags = new TagList
        {
            { ApplicationTagKeys.QueryType, queryType },
            { ApplicationTagKeys.CacheScope, CacheTelemetryNames.Of(scope) },
            { ApplicationTagKeys.CacheResult, CacheTelemetryNames.Of(outcome) },
        };

        _lookups.Add(1, tags);
    }

    internal void RecordEviction(string commandType, string target, bool succeeded)
    {
        var tags = new TagList
        {
            { ApplicationTagKeys.CommandType, commandType },
            { ApplicationTagKeys.CacheTarget, target },
            { ApplicationTagKeys.CacheEvicted, succeeded },
        };

        _evictions.Add(1, tags);
    }
}
