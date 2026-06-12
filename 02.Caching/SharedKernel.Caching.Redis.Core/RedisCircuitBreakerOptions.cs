using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Caching.Redis.Core;

/// <summary>
/// Canonical, top-level configuration for the opt-in Polly v8 circuit breaker that wraps
/// Redis operations across the <c>SharedKernel.Caching.Redis.*</c> packages.
/// </summary>
/// <remarks>
/// <para>
/// This is the single shared shape for circuit breaker configuration — consuming packages
/// (<c>.Redis</c>, <c>.DistributedLocking</c>, <c>.HashStore</c>, <c>.PubSub</c>) bind to this
/// type rather than declaring local copies. It is a direct generalization of the circuit
/// breaker options nested inside <c>RedisL2Options</c> introduced for Redis L2 — same five
/// properties, same defaults, same validation.
/// </para>
/// <para>
/// When <see cref="Enabled"/> is <see langword="true"/>, a Polly <c>ResiliencePipeline</c>
/// singleton is registered via
/// <see cref="Extensions.RedisCircuitBreakerExtensions.AddRedisCircuitBreaker"/>. When the
/// circuit is open, Redis operations short-circuit immediately — FusionCache fail-safe serves
/// stale L1 data with zero Redis wait time, eliminating timeout accumulation during outages.
/// </para>
/// <para>
/// FusionCache's own fail-safe is not replaced — the circuit breaker is complementary and
/// fires before the FusionCache timeout logic is reached.
/// </para>
/// <para>
/// <b>AOT compatibility:</b> Polly.Core 8.x is AOT-compatible. No reflection is used by the
/// circuit breaker strategy.
/// </para>
/// </remarks>
public sealed class RedisCircuitBreakerOptions
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
    /// Defaults to <c>30 seconds</c>. Polly v8 enforces a minimum of <c>500 ms</c>.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum number of requests that must pass through the sampling window before the
    /// circuit breaker evaluates the failure threshold. Defaults to <c>3</c>.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MinimumThroughput must be >= 1.")]
    public int MinimumThroughput { get; set; } = 3;
}
