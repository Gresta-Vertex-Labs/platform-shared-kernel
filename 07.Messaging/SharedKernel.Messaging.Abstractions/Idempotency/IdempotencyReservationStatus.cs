namespace SharedKernel.Messaging.Abstractions.Idempotency;

/// <summary>
/// The outcome of <see cref="IIdempotencyStore.TryBeginAsync"/> — whether this delivery owns the
/// right to run the consumer for a message id.
/// </summary>
/// <remarks>
/// The distinction between <see cref="InProgress"/> and <see cref="AlreadyProcessed"/> is the
/// point of this enum. The contract this replaced (P-560) could only answer a single boolean, so a
/// concurrent in-flight delivery and a genuinely completed one were indistinguishable — and both
/// shipped stores resolved that ambiguity by treating "in flight" as "processed", acknowledging a
/// redelivery whose original attempt had failed. That silently dropped the message.
/// </remarks>
public enum IdempotencyReservationStatus
{
    /// <summary>
    /// This delivery now holds the reservation and must run the consumer, then call
    /// <see cref="IIdempotencyStore.CompleteAsync"/> on success or
    /// <see cref="IIdempotencyStore.ReleaseAsync"/> on failure.
    /// </summary>
    Started,

    /// <summary>
    /// Another delivery of the same message id is currently running the consumer. The message must
    /// <strong>not</strong> be acknowledged: let the broker redeliver it, so it is still processed
    /// if the in-flight attempt ultimately fails.
    /// </summary>
    InProgress,

    /// <summary>
    /// The message was already consumed to completion. This is a true duplicate and is acknowledged
    /// without invoking the consumer.
    /// </summary>
    AlreadyProcessed,
}
