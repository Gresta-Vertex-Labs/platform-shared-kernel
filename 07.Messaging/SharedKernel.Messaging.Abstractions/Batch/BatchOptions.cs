namespace SharedKernel.Messaging.Abstractions.Batch;

/// <summary>
/// Configuration options for batch consumer endpoints.
/// </summary>
/// <remarks>
/// Applied via <c>MessagingBusBuilder.AddBatchConsumer&lt;TConsumer&gt;()</c> in the MassTransit
/// package. These values are applied per endpoint, not globally.
/// </remarks>
public sealed class BatchOptions
{
    /// <summary>
    /// The DI configuration section name for <see cref="BatchOptions"/>.
    /// </summary>
    public const string SectionName = "SharedKernel:Messaging:Batch";

    /// <summary>
    /// Maximum number of messages delivered in a single batch.
    /// </summary>
    /// <remarks>Default: <c>10</c>.</remarks>
    public int MessageLimit { get; set; } = 10;

    /// <summary>
    /// Maximum duration to wait for the batch to reach <see cref="MessageLimit"/> before
    /// delivering a partial batch.
    /// </summary>
    /// <remarks>
    /// Default: <c>1 second</c>. When <see cref="TimeLimit"/> elapses and fewer than
    /// <see cref="MessageLimit"/> messages are available, the partial batch is delivered
    /// immediately rather than waiting for more messages.
    /// </remarks>
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum number of concurrent batch deliveries for this endpoint.
    /// </summary>
    /// <remarks>Default: <c>1</c>.</remarks>
    public int ConcurrencyLimit { get; set; } = 1;
}
