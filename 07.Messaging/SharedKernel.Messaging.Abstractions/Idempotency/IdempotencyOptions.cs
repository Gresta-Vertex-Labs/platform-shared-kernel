namespace SharedKernel.Messaging.Abstractions.Idempotency;

/// <summary>
/// How long consumer-side idempotency holds a message id while its consumer runs, and how long a consumed id keeps
/// deduplicating redeliveries.
/// </summary>
/// <remarks>
/// Configure via <c>MessagingBusBuilder.WithIdempotency(o =&gt; o.ExpiryWindow = ...)</c>. Both values are passed to the
/// <c>SharedKernel.Idempotency.Abstractions.IIdempotencyStore</c> registered for <c>IdempotencyPurpose.Message</c> on
/// every call, so the store needs no retention settings of its own.
/// </remarks>
public sealed class IdempotencyOptions
{
    /// <summary>The DI options section name for <see cref="IdempotencyOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Messaging:Idempotency";

    /// <summary>
    /// How long a message id is held while its consumer runs. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// Must outlast the slowest consumer: once the lease expires mid-consume, a redelivery can reserve the same id and
    /// run the consumer a second time. A consumer that crashes without releasing the id blocks redeliveries for at most
    /// this long.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a consumed message id keeps deduplicating redeliveries. Defaults to 24 hours.
    /// </summary>
    /// <remarks>
    /// A window shorter than the transport's maximum redelivery delay lets a late redelivery run the consumer again. Set
    /// it to at least the dead-letter retry budget.
    /// </remarks>
    public TimeSpan ExpiryWindow { get; set; } = TimeSpan.FromHours(24);
}
