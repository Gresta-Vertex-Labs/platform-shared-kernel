namespace SharedKernel.Messaging.Abstractions.Faults;

/// <summary>
/// Configuration options for the MassTransit circuit breaker middleware.
/// </summary>
/// <remarks>
/// Consumed by <c>MessagingBusBuilder.WithCircuitBreaker()</c> in the MassTransit package.
/// The circuit breaker is a global policy — not per-consumer. When both
/// <c>WithRetry()</c> and <c>WithCircuitBreaker()</c> are called, retry is applied inner
/// (first) and circuit breaker is applied outer (second). This is the correct resilience
/// layering: retry exhaustion within the current breaker state, then the breaker guards
/// against sustained failure.
/// </remarks>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// The DI configuration section name for <see cref="CircuitBreakerOptions"/>.
    /// </summary>
    public const string SectionName = "SharedKernel:Messaging:CircuitBreaker";

    /// <summary>
    /// Number of consecutive failures before the circuit breaker trips to the Open state.
    /// </summary>
    /// <remarks>Default: <c>5</c>.</remarks>
    public int TripThreshold { get; set; } = 5;

    /// <summary>
    /// Minimum number of active messages that must be in-flight before the circuit breaker
    /// begins evaluating failures. Prevents spurious trips when traffic is low.
    /// </summary>
    /// <remarks>Default: <c>10</c>.</remarks>
    public int ActiveThreshold { get; set; } = 10;

    /// <summary>
    /// Duration the circuit breaker remains in the Open state before moving to HalfOpen
    /// and allowing a probe message through.
    /// </summary>
    /// <remarks>Default: <c>60 seconds</c>.</remarks>
    public TimeSpan ResetInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Rolling window duration over which failures are counted toward <see cref="TripThreshold"/>.
    /// </summary>
    /// <remarks>Default: <c>60 seconds</c>.</remarks>
    public TimeSpan TrackingPeriod { get; set; } = TimeSpan.FromSeconds(60);
}
