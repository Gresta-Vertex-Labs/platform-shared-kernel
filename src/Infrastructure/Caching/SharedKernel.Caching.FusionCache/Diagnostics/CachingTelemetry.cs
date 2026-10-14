namespace SharedKernel.Caching.FusionCache.Diagnostics;

/// <summary>
/// The telemetry names of the cache: the <c>ActivitySource</c> and <c>Meter</c> a host subscribes to.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddServiceDefaults</c> subscribes to every <c>SharedKernel.*</c> source and meter by wildcard, so a host
/// needs no registration for these. Only the FusionCache package emits telemetry in this domain; locks, the hash
/// store and Pub/Sub emit none.
/// </para>
/// <para>
/// Instruments are named <c>sharedkernel.caching.*</c> and tagged with the keys in <see cref="CachingTagKeys"/>. A tag
/// never carries an id, a tenant or a full cache key.
/// </para>
/// </remarks>
public static class CachingTelemetry
{
    /// <summary>The name of the cache's <c>ActivitySource</c> (<c>SharedKernel.Caching</c>).</summary>
    public const string SourceName = "SharedKernel.Caching";

    /// <summary>The name of the cache's <c>Meter</c> (<c>SharedKernel.Caching</c>).</summary>
    public const string MeterName = "SharedKernel.Caching";

    /// <summary>The counter of reads answered from a cache layer (<c>sharedkernel.caching.hits</c>, unit <c>{hit}</c>).</summary>
    public const string HitsInstrument = "sharedkernel.caching.hits";

    /// <summary>The counter of reads no cache layer could answer (<c>sharedkernel.caching.misses</c>, unit <c>{miss}</c>).</summary>
    public const string MissesInstrument = "sharedkernel.caching.misses";

    /// <summary>The counter of failed cache writes and factory runs (<c>sharedkernel.caching.errors</c>, unit <c>{error}</c>).</summary>
    public const string ErrorsInstrument = "sharedkernel.caching.errors";

    /// <summary>The counter of memory-cache evictions (<c>sharedkernel.caching.evictions</c>, unit <c>{eviction}</c>).</summary>
    public const string EvictionsInstrument = "sharedkernel.caching.evictions";

    /// <summary>The histogram of factory run durations in seconds (<c>sharedkernel.caching.factory.duration</c>, unit <c>s</c>).</summary>
    public const string FactoryDurationInstrument = "sharedkernel.caching.factory.duration";
}
