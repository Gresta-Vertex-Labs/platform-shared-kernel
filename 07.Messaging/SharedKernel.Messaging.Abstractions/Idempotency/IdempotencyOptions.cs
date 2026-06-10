namespace SharedKernel.Messaging.Abstractions.Idempotency;

/// <summary>
/// Configuration options for the consumer-side idempotency deduplication mechanism.
/// </summary>
/// <remarks>
/// <para>
/// Bound from DI options section <c>"SharedKernel:Messaging:Idempotency"</c>.
/// Configure via <c>MessagingBusBuilder.WithIdempotency(o =&gt; o.ExpiryWindow = ...)</c>.
/// </para>
/// <para>
/// These options are available to the consuming service's <see cref="IIdempotencyStore"/>
/// implementation via <c>IOptions&lt;IdempotencyOptions&gt;</c>. SharedKernel does not enforce
/// the expiry window — the implementing service decides how to use it.
/// </para>
/// </remarks>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// The DI options section name for <see cref="IdempotencyOptions"/>.
    /// </summary>
    public const string SectionName = "SharedKernel:Messaging:Idempotency";

    /// <summary>
    /// Gets or sets the advisory expiry window for time-windowed
    /// <see cref="IIdempotencyStore"/> implementations.
    /// Defaults to <c>24 hours</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Advisory only.</strong> This value is a hint for time-windowed store implementations
    /// (e.g., a Redis store with TTL). SharedKernel does not prune expired records itself —
    /// the consuming service's <see cref="IIdempotencyStore"/> implementation decides whether
    /// and how to apply this window.
    /// </para>
    /// <para>
    /// Setting this value to a window shorter than the maximum message re-delivery delay for
    /// the configured transport introduces the risk of duplicate processing. Set it to be at
    /// least as long as the transport's dead-letter retry budget.
    /// </para>
    /// </remarks>
    public TimeSpan ExpiryWindow { get; set; } = TimeSpan.FromHours(24);
}
