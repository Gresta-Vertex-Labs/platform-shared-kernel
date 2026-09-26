namespace SharedKernel.Caching.Redis.Core.Health;

/// <summary>The names of the readiness probes this package registers.</summary>
public static class RedisReadinessProbeNames
{
    /// <summary>
    /// The probe of the shared Redis connection, registered by <c>AddRedisConnection</c>. It sends a <c>PING</c>
    /// over the connection every Redis package uses and reports the round trip as the report's latency.
    /// </summary>
    public const string Connection = "redis";
}
