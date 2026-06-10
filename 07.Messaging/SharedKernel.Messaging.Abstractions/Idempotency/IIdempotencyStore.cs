namespace SharedKernel.Messaging.Abstractions.Idempotency;

/// <summary>
/// Provides storage for consumer-side message deduplication.
/// The platform uses this interface through <c>IdempotentConsumerBehavior&lt;TMessage&gt;</c>
/// to record and query processed message identifiers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>SharedKernel does not provide an implementation.</strong> The consuming service must
/// register a concrete implementation (e.g., <c>RedisIdempotencyStore</c>,
/// <c>EfCoreIdempotencyStore</c>) before calling <c>WithIdempotency()</c> on
/// <c>MessagingBusBuilder</c>. Calling <c>Build()</c> without a registered implementation
/// throws <see cref="InvalidOperationException"/> with a diagnostic message.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> Never implement custom deduplication logic inside
/// <c>ConsumeAsync</c> bodies (e.g., checking a local <c>HashSet&lt;Guid&gt;</c> or querying
/// the database on every message). The platform-standard deduplication mechanism is
/// <c>WithIdempotency()</c> with a registered <c>IIdempotencyStore</c> implementation.
/// Ad-hoc in-consumer deduplication is non-standard and creates inconsistency across services.
/// </para>
/// <para>
/// The storage backend and retention window are implementation concerns.
/// Use <see cref="IdempotencyOptions.ExpiryWindow"/> as an advisory hint
/// to time-windowed store implementations.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="messageId"/> has already been
    /// successfully processed; <see langword="false"/> when it is a novel message.
    /// </summary>
    /// <param name="messageId">
    /// The unique identifier of the message to query, sourced from
    /// <c>ConsumeContext.MessageId</c>.
    /// </param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>
    /// <see langword="true"/> if the message has already been processed;
    /// <see langword="false"/> otherwise.
    /// </returns>
    /// <remarks>
    /// Called by <c>IdempotentConsumerBehavior&lt;TMessage&gt;</c> before invoking the consumer
    /// body. When this method returns <see langword="true"/>, the message is acknowledged to the
    /// broker without invoking the consumer — <see cref="MarkProcessedAsync"/> is NOT called
    /// on the duplicate short-circuit path.
    /// </remarks>
    Task<bool> HasProcessedAsync(Guid messageId, CancellationToken ct);

    /// <summary>
    /// Records <paramref name="messageId"/> as successfully processed.
    /// </summary>
    /// <param name="messageId">
    /// The unique identifier of the message that was successfully consumed.
    /// </param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    /// <remarks>
    /// Called by <c>IdempotentConsumerBehavior&lt;TMessage&gt;</c> <strong>only after</strong>
    /// the consumer body completes without exception. Never called on failure or on duplicate
    /// short-circuit. The implementation must be durable enough to survive at least one message
    /// re-delivery window (see <see cref="IdempotencyOptions.ExpiryWindow"/>).
    /// </remarks>
    Task MarkProcessedAsync(Guid messageId, CancellationToken ct);
}
