using MassTransit;
using SharedKernel.Messaging.Abstractions.Idempotency;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Global MassTransit consume pipeline filter that provides consumer-side message deduplication.
/// Applied to all consumers when <c>WithIdempotency()</c> is called on <c>MessagingBusBuilder</c>.
/// </summary>
/// <typeparam name="TMessage">The message type being consumed.</typeparam>
/// <remarks>
/// <para>
/// Pipeline logic:
/// <list type="number">
///   <item>Read <c>ConsumeContext.MessageId</c> as <c>Guid?</c>. If <see langword="null"/>, pass through without idempotency check (defensive).</item>
///   <item>Call <see cref="IIdempotencyStore.HasProcessedAsync"/> before delegating to the next filter.</item>
///   <item>If <see langword="true"/> (already processed): acknowledge the message to the broker without invoking the consumer body. <see cref="IIdempotencyStore.MarkProcessedAsync"/> is NOT called on the duplicate short-circuit path.</item>
///   <item>If <see langword="false"/> (novel message): call <c>next.Send(context, ct)</c> to invoke the consumer body.</item>
///   <item>After <c>next.Send</c> returns successfully: call <see cref="IIdempotencyStore.MarkProcessedAsync"/>.</item>
///   <item>If <c>next.Send</c> throws: propagate the exception without calling <see cref="IIdempotencyStore.MarkProcessedAsync"/> — the consumer failed; the message should be retried, not marked as processed.</item>
/// </list>
/// </para>
/// <para>
/// This is the only approved deduplication mechanism. Never implement deduplication logic
/// inside <c>ConsumeAsync</c> bodies.
/// </para>
/// </remarks>
internal sealed class IdempotentConsumerBehavior<TMessage> : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    private readonly IIdempotencyStore _store;

    public IdempotentConsumerBehavior(IIdempotencyStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public void Probe(ProbeContext context)
        => context.CreateFilterScope("idempotent-consumer");

    /// <inheritdoc />
    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        var ct = context.CancellationToken;

        // Step 1: If MessageId is null, pass through without idempotency check (defensive).
        if (context.MessageId is null)
        {
            await next.Send(context).ConfigureAwait(false);
            return;
        }

        var messageId = context.MessageId.Value;

        // Step 2: Check whether this message has already been successfully processed.
        var alreadyProcessed = await _store.HasProcessedAsync(messageId, ct).ConfigureAwait(false);

        if (alreadyProcessed)
        {
            // Step 3: Duplicate — acknowledge without invoking the consumer body.
            // MarkProcessedAsync is NOT called on the duplicate short-circuit path.
            return;
        }

        // Step 4: Novel message — invoke the consumer body via the next filter.
        // Step 6: If next.Send throws, propagate without marking as processed.
        await next.Send(context).ConfigureAwait(false);

        // Step 5: Only reached when next.Send returned successfully.
        await _store.MarkProcessedAsync(messageId, ct).ConfigureAwait(false);
    }
}
