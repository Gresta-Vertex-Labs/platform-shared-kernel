using MassTransit;
using SharedKernel.Messaging.Abstractions.Idempotency;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Consume filter enforcing at-most-once consumption of a message id through an
/// <see cref="IIdempotencyStore"/> reservation.
/// </summary>
/// <remarks>
/// Rewritten by P-560 onto the atomic reserve/complete/release contract. The previous
/// check-then-act version let two concurrent deliveries of the same id both run the consumer, and
/// never released the reservation when the consumer failed.
/// </remarks>
internal sealed class IdempotentConsumerBehavior<TMessage> : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    private readonly IIdempotencyStore _store;

    public IdempotentConsumerBehavior(IIdempotencyStore store)
    {
        _store = store;
    }

    public void Probe(ProbeContext context)
        => context.CreateFilterScope("idempotent-consumer");

    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        // A transport that supplies no MessageId gives us nothing to deduplicate on. Pass through
        // rather than inventing an id, which would make every delivery look unique anyway.
        if (context.MessageId is null)
        {
            await next.Send(context).ConfigureAwait(false);
            return;
        }

        var messageId = context.MessageId.Value;
        var reservation = await _store.TryBeginAsync(messageId, context.CancellationToken).ConfigureAwait(false);

        switch (reservation.Status)
        {
            case IdempotencyReservationStatus.AlreadyProcessed:
                // A true duplicate: the original delivery ran the consumer to completion. Return
                // without invoking it, which acknowledges the message.
                return;

            case IdempotencyReservationStatus.InProgress:
                // Another delivery is running the consumer right now. Throwing keeps the message
                // unacknowledged so the broker redelivers it — if the in-flight attempt fails, the
                // message is still processed. Returning here instead would acknowledge a message
                // that may never have been consumed.
                throw new ConcurrentMessageDeliveryException(messageId, typeof(TMessage));

            case IdempotencyReservationStatus.Started:
                break;

            default:
                throw new InvalidOperationException(
                    $"Unknown {nameof(IdempotencyReservationStatus)} '{reservation.Status}' returned by " +
                    $"{_store.GetType().Name}.{nameof(IIdempotencyStore.TryBeginAsync)}.");
        }

        var token = reservation.ReservationToken
            ?? throw new InvalidOperationException(
                $"{_store.GetType().Name}.{nameof(IIdempotencyStore.TryBeginAsync)} returned " +
                $"{nameof(IdempotencyReservationStatus.Started)} without a reservation token. A started " +
                "reservation must carry the token required to complete or release it.");

        try
        {
            await next.Send(context).ConfigureAwait(false);
        }
        catch
        {
            // Release before the exception propagates, so MassTransit's retry/redelivery can
            // re-acquire the id immediately instead of waiting out the lease.
            // CancellationToken.None: a cancelled release would strand the id for the whole lease
            // window, which is exactly the failure mode this call exists to prevent.
            await _store.ReleaseAsync(messageId, token, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // Reached only when the consumer returned without throwing. CancellationToken.None because
        // the consumer's side effects are already durable: cancelling this write would leave
        // completed work recorded as unprocessed and run it again on the next delivery.
        await _store.CompleteAsync(messageId, token, CancellationToken.None).ConfigureAwait(false);
    }
}
