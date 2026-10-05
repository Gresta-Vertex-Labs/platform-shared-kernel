namespace SharedKernel.Messaging.MassTransit.Options;

/// <summary>
/// Configuration options for the MassTransit retry pipeline applied globally to all consumers.
/// </summary>
/// <remarks>
/// <para>
/// When <c>.WithRetry()</c> is called on <c>MessagingBusBuilder</c>, these options configure a
/// global retry policy applied to all registered consumers. Consumer-specific overrides can be
/// configured via <c>AddConsumer&lt;T, TDefinition&gt;()</c>.
/// </para>
/// <para>
/// Business validation failures and domain exceptions that should not be retried (4xx-equivalent)
/// must be filtered via a retry filter on the consumer's <c>IConsumerDefinition&lt;T&gt;</c> —
/// never silently swallowed inside <c>ConsumeAsync</c>.
/// </para>
/// </remarks>
public sealed class RetryOptions
{
    /// <summary>
    /// Gets or sets the total number of delivery attempts, including the first delivery.
    /// Default is <c>3</c> (1 initial + 2 retries).
    /// </summary>
    public int Attempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay before the second attempt.
    /// Default is <c>1 second</c>.
    /// </summary>
    public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the amount added to the delay per subsequent attempt (linear back-off increment).
    /// Default is <c>1 second</c>.
    /// </summary>
    public TimeSpan IntervalIncrement { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the ceiling on the inter-attempt delay.
    /// Prevents unbounded growth with many retry attempts.
    /// Default is <c>30 seconds</c>.
    /// </summary>
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the number of immediate (zero-delay) retries before interval-based retries begin.
    /// Default is <c>0</c> (no immediate retries — all retries use interval back-off).
    /// </summary>
    public int ImmediateAttempts { get; set; } = 0;
}
