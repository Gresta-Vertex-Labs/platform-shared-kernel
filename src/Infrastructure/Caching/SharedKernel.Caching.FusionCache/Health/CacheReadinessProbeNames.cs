namespace SharedKernel.Caching.FusionCache.Health;

/// <summary>The names of the readiness probes this package registers.</summary>
public static class CacheReadinessProbeNames
{
    /// <summary>
    /// The probe of the registered <c>ICacheService</c>, registered by <c>AddSharedKernelCaching</c>. It reports
    /// <c>Degraded</c>, never <c>Unhealthy</c>, when the cache cannot be read.
    /// </summary>
    public const string Cache = "cache";
}
