using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Messaging.Abstractions.Idempotency;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Consume filter enforcing at-most-once consumption of a message id through the <see cref="IIdempotencyStore"/>
/// registered for <see cref="IdempotencyPurpose.Message"/>.
/// </summary>
/// <remarks>
/// <para>
/// The message id (<c>ConsumeContext.MessageId</c>, "D" form) is the key, and the fingerprint is fixed: an id already
/// identifies one message, so there is no body to compare. The lease and retention come from
/// <see cref="IdempotencyOptions"/>. The key is scoped by the tenant of the ambient request context, which
/// <c>WithInboundRequestContext()</c> sets before this filter runs.
/// </para>
/// <para>
/// A completed id is acknowledged without running the consumer. An id another delivery holds throws
/// <see cref="ConcurrentMessageDeliveryException"/>, leaving the message unacknowledged so it is still processed if the
/// running attempt fails. A consumer that throws releases the id at once, so the redelivery is not discarded.
/// </para>
/// </remarks>
internal sealed class IdempotentConsumerBehavior<TMessage> : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    /// <summary>The fingerprint every message reservation carries.</summary>
    internal const string MessageFingerprint = "message";

    private readonly IIdempotencyStore _store;
    private readonly IOptions<IdempotencyOptions> _options;

    public IdempotentConsumerBehavior(
        [FromKeyedServices(IdempotencyPurpose.Message)] IIdempotencyStore store,
        IOptions<IdempotencyOptions> options)
    {
        _store = store;
        _options = options;
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
        var key = messageId.ToString("D");
        var options = _options.Value;

        var reservation = await _store
            .TryBeginAsync(IdempotencyPurpose.Message, key, MessageFingerprint, options.LeaseDuration, context.CancellationToken)
            .ConfigureAwait(false);

        switch (reservation.Status)
        {
            case IdempotencyReservationStatus.Completed:
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
                    $"{_store.GetType().Name}.{nameof(IIdempotencyStore.TryBeginAsync)} returned " +
                    $"{nameof(IdempotencyReservationStatus)}.{reservation.Status} for a message reservation, whose " +
                    "fingerprint is fixed.");
        }

        var token = reservation.Token
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
            await _store.ReleaseAsync(IdempotencyPurpose.Message, key, token, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // Reached only when the consumer returned without throwing. CancellationToken.None because
        // the consumer's side effects are already durable: cancelling this write would leave
        // completed work recorded as unprocessed and run it again on the next delivery.
        await _store
            .CompleteAsync(IdempotencyPurpose.Message, key, token, response: null, options.ExpiryWindow, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
