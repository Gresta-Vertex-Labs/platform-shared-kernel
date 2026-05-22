using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// Configuration options for the Redis L2 distributed backplane.
/// Bound to <c>SharedKernelCaching:Redis</c> section in application configuration.
/// </summary>
public sealed class RedisL2Options
{
    /// <summary>
    /// The configuration section name used when binding these options from
    /// <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "SharedKernelCaching:Redis";

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
    /// When <see cref="CircuitBreakerOptions.Enabled"/> is <see langword="false"/> (the default),
    /// no Polly pipeline is registered and all existing behavior is preserved unchanged.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; } = new();

    /// <summary>
    /// Configuration for the Polly v8 circuit breaker that wraps Redis L2 operations
    /// in <c>RedisHashService</c> and <c>RedisChannelService</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="Enabled"/> is <see langword="true"/>, a Polly
    /// <c>ResiliencePipeline</c> singleton is registered in DI. When the circuit is open,
    /// Redis operations short-circuit immediately — FusionCache fail-safe serves stale L1
    /// data with zero Redis wait time, eliminating timeout accumulation during outages.
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
    public sealed class CircuitBreakerOptions
    {
        /// <summary>
        /// Whether the circuit breaker is enabled.
        /// Defaults to <see langword="false"/> — all existing behavior is preserved when disabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Number of failures within the <see cref="SamplingDuration"/> window required to open
        /// the circuit. Defaults to <c>5</c>.
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "FailureThreshold must be >= 1.")]
        public int FailureThreshold { get; set; } = 5;

        /// <summary>
        /// Duration of the sliding window used to count failures.
        /// Defaults to <c>10 seconds</c>.
        /// </summary>
        public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Duration the circuit remains open before transitioning to half-open to probe recovery.
        /// Defaults to <c>30 seconds</c>.
        /// </summary>
        public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Minimum number of requests that must pass through the sampling window before the
        /// circuit breaker evaluates the failure threshold. Defaults to <c>3</c>.
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "MinimumThroughput must be >= 1.")]
        public int MinimumThroughput { get; set; } = 3;
    }
}
