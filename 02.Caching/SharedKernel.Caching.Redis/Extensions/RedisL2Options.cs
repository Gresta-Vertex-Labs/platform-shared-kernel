using System.ComponentModel.DataAnnotations;
using SharedKernel.Caching.Redis.Core;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// Configuration options for the Redis L2 distributed backplane.
/// Bound to <c>SharedKernel:Caching:Redis</c> section in application configuration.
/// </summary>
public sealed class RedisL2Options
{
    /// <summary>
    /// The configuration section name used when binding these options from
    /// <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "SharedKernel:Caching:Redis";

    /// <summary>
    /// StackExchange.Redis connection string.
    /// Required; must not be null or whitespace.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional Redis key prefix applied to all cache entries written to L2.
    /// Useful when multiple services share the same Redis instance.
    /// Defaults to an empty string (no prefix).
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Connection timeout in milliseconds for the Redis multiplexer.
    /// Defaults to <c>5000</c> ms.
    /// </summary>
    [Range(100, 60_000, ErrorMessage = "ConnectTimeout must be between 100 ms and 60 000 ms.")]
    public int ConnectTimeoutMs { get; set; } = 5_000;

    /// <summary>
    /// Opt-in Polly v8 circuit breaker configuration for Redis L2 operations.
    /// When <see cref="RedisCircuitBreakerOptions.Enabled"/> is <see langword="false"/> (the
    /// default), no Polly pipeline is registered and all existing behavior is preserved unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As of Phase 33, this property is the canonical, top-level
    /// <see cref="RedisCircuitBreakerOptions"/> type from
    /// <c>SharedKernel.Caching.Redis.Core</c> — previously a class nested inside
    /// <see cref="RedisL2Options"/>. This is a type relocation, not a rename:
    /// <c>options.CircuitBreaker.Enabled = true</c> continues to compile unchanged.
    /// </para>
    /// <para>
    /// When <see cref="RedisCircuitBreakerOptions.Enabled"/> is <see langword="true"/>, a Polly
    /// <c>ResiliencePipeline</c> singleton is registered in DI via
    /// <c>SharedKernel.Caching.Redis.Core.Extensions.RedisCircuitBreakerExtensions.AddRedisCircuitBreaker</c>.
    /// When the circuit is open, Redis operations short-circuit immediately — FusionCache
    /// fail-safe serves stale L1 data with zero Redis wait time, eliminating timeout
    /// accumulation during outages.
    /// </para>
    /// <para>
    /// FusionCache's own fail-safe is not replaced — the circuit breaker is complementary
    /// and fires before the FusionCache timeout logic is reached.
    /// </para>
    /// <para>
    /// <b>AOT compatibility:</b> Polly.Core 8.x is AOT-compatible. No reflection is used
    /// by the circuit breaker strategy.
    /// </para>
    /// </remarks>
    public RedisCircuitBreakerOptions CircuitBreaker { get; } = new();
}
