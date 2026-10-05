using SharedKernel.Configuration;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// Settings for the Redis distributed layer and backplane, bound from the
/// <c>SharedKernel:Caching:Redis:L2</c> configuration section or set in code.
/// </summary>
/// <remarks>
/// The connection itself (connection string, TLS, timeouts) is configured once with
/// <c>AddRedisConnection</c>. Every setting here is read when the cache is first resolved and validated at
/// host startup.
/// </remarks>
/// <example>
/// <code>
/// // appsettings.json
/// // "SharedKernel": { "Caching": { "Redis": { "ConnectionString": "redis:6379",
/// //                                          "L2": { "KeyPrefix": "staging:" } } } }
/// </code>
/// </example>
public sealed class RedisL2Options : ISectionBoundOptions
{
    /// <summary>Gets the configuration section path: <c>SharedKernel:Caching:Redis:L2</c>.</summary>
    public static string SectionName => "SharedKernel:Caching:Redis:L2";

    /// <summary>
    /// Gets or sets a prefix for every key the distributed layer writes. Defaults to empty.
    /// </summary>
    /// <remarks>
    /// Cache keys already start with the service name. Use a prefix only to separate environments or
    /// deployments that share one Redis database. The stored key is <c>{KeyPrefix}v2:{cache key}</c> (FusionCache adds <c>v2:</c>), and the prefix
    /// also becomes the backplane channel prefix, so notifications stay within the deployment. At most 64 characters.
    /// </remarks>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long the cache stops using the distributed layer after an operation on it fails.
    /// Defaults to 2 seconds; <see cref="TimeSpan.Zero"/> turns the circuit breaker off.
    /// </summary>
    /// <remarks>
    /// While the circuit is open, reads and writes use the memory cache only, with fail-safe values when the
    /// policy allows them, so an unreachable Redis does not add a timeout to every request. Between zero and
    /// 10 minutes.
    /// </remarks>
    public TimeSpan DistributedCacheCircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how long the cache stops using the backplane after an operation on it fails.
    /// Defaults to 2 seconds; <see cref="TimeSpan.Zero"/> turns the circuit breaker off.
    /// </summary>
    /// <remarks>
    /// While the circuit is open, removals and expirations are not sent to other instances, which keep their
    /// memory entries until those expire. Between zero and 10 minutes.
    /// </remarks>
    public TimeSpan BackplaneCircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(2);
}
