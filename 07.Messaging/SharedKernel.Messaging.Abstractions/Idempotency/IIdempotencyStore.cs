namespace SharedKernel.Messaging.Abstractions.Idempotency;

/// <summary>
/// Atomic reservation store for consumer-side message deduplication, used by
/// <c>IdempotentConsumerBehavior&lt;TMessage&gt;</c> to guarantee a message id is consumed at most once.
/// </summary>
/// <remarks>
/// <para>
/// <strong>SharedKernel does not provide an implementation.</strong> The consuming service registers
/// one — <c>18.Idempotency</c> ships <c>SharedKernel.Idempotency.Redis</c> and
/// <c>SharedKernel.Idempotency.EfCore</c> — before calling <c>WithIdempotency()</c> on
/// <c>MessagingBusBuilder</c>. <c>Build()</c> throws <see cref="InvalidOperationException"/> when
/// none is registered.
/// </para>
/// <para>
/// <strong>The reservation must be atomic.</strong> <see cref="TryBeginAsync"/> is a single
/// compare-and-set against the backing store — a Lua <c>SET NX</c>, an
/// <c>INSERT … ON CONFLICT DO NOTHING</c>, or equivalent. A read-then-write pair is not a valid
/// implementation: two concurrent deliveries of the same id would both observe "not seen" and both
/// invoke the consumer.
/// </para>
/// <para>
/// <strong>Why this replaced <c>HasProcessedAsync</c>/<c>MarkProcessedAsync</c> (P-560).</strong>
/// That pair was documented as a query followed by a write, which is inherently racy. Both shipped
/// stores had already abandoned the documented semantics to stay correct — each made
/// <c>HasProcessedAsync</c> a mutating atomic reserve whose <see langword="true"/> meant "someone
/// else holds this", not "this completed". Because the boolean could not express that difference,
/// a redelivery arriving after a <em>failed</em> attempt was read as a duplicate, acknowledged and
/// dropped. This contract makes ownership explicit and adds the missing release path.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> never hand-roll deduplication inside a <c>ConsumeAsync</c> body
/// (a local <c>HashSet</c>, an ad hoc table lookup). Use <c>WithIdempotency()</c> with a registered
/// store so every service behaves identically.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically attempts to reserve <paramref name="messageId"/> for consumption by this delivery.
    /// </summary>
    /// <param name="messageId">
    /// The unique message identifier, sourced from <c>ConsumeContext.MessageId</c>.
    /// </param>
    /// <param name="ct">Cancellation token for the store operation.</param>
    /// <returns>
    /// An <see cref="IdempotencyReservation"/> describing whether this delivery acquired the
    /// reservation, another delivery is mid-flight, or the message was already completed.
    /// </returns>
    /// <remarks>
    /// A reservation taken here is held under a lease. An implementation must let the lease expire
    /// so a crashed consumer cannot block a message id forever; the lease must outlast the slowest
    /// expected consumer, since expiry mid-consume allows a redelivery to acquire the same id.
    /// </remarks>
    Task<IdempotencyReservation> TryBeginAsync(Guid messageId, CancellationToken ct);

    /// <summary>
    /// Marks <paramref name="messageId"/> as consumed to completion, so later deliveries resolve to
    /// <see cref="IdempotencyReservationStatus.AlreadyProcessed"/>.
    /// </summary>
    /// <param name="messageId">The message identifier that was successfully consumed.</param>
    /// <param name="reservationToken">
    /// The token from the <see cref="IdempotencyReservationStatus.Started"/> reservation. A store
    /// must ignore the call when the token no longer matches the current holder.
    /// </param>
    /// <param name="ct">
    /// Cancellation token. <c>IdempotentConsumerBehavior</c> deliberately passes
    /// <see cref="CancellationToken.None"/>: the consumer has already succeeded and its side effects
    /// are durable, so cancelling this write would leave completed work looking unprocessed and
    /// cause it to run again on redelivery.
    /// </param>
    /// <returns>A task that completes when the write is durable.</returns>
    Task CompleteAsync(Guid messageId, string reservationToken, CancellationToken ct);

    /// <summary>
    /// Releases the reservation on <paramref name="messageId"/> without marking it processed, so the
    /// broker's redelivery can consume it.
    /// </summary>
    /// <param name="messageId">The message identifier whose consumer failed.</param>
    /// <param name="reservationToken">
    /// The token from the <see cref="IdempotencyReservationStatus.Started"/> reservation. A store
    /// must ignore the call when the token no longer matches the current holder.
    /// </param>
    /// <param name="ct">
    /// Cancellation token. As with <see cref="CompleteAsync"/>, the behavior passes
    /// <see cref="CancellationToken.None"/> — a cancelled release would strand the id until its
    /// lease expired, needlessly delaying the retry.
    /// </param>
    /// <returns>A task that completes when the reservation has been released.</returns>
    /// <remarks>
    /// Called when the consumer throws. Without it a failed delivery would hold the id for the whole
    /// lease window and every redelivery inside that window would be discarded as a duplicate.
    /// </remarks>
    Task ReleaseAsync(Guid messageId, string reservationToken, CancellationToken ct);
}
