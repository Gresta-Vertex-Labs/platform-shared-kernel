using System.Collections.Concurrent;
using SharedKernel.Messaging.Abstractions.Scheduling;

// Alias MassTransit's IMessageScheduler to avoid name collision with our abstraction.
using MtScheduler = MassTransit.IMessageScheduler;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// The transport-native implementation of <see cref="IMessageScheduler"/>.
/// </summary>
/// <remarks>
/// <para>
/// Delegates to MassTransit's own scheduler, which in turn uses the broker's scheduling feature —
/// the RabbitMQ delayed-message exchange, or Azure Service Bus scheduled enqueue. Nothing is held
/// in this process, so a scheduled message survives a restart. The returned
/// <c>ScheduledMessage.TokenId</c> becomes the abstraction's opaque token.
/// </para>
/// <para>
/// Registered as a scoped service by <c>MessagingBusBuilder.WithDelayedDelivery()</c>.
/// </para>
/// <para>
/// <strong>Cancellation is best-effort across a restart.</strong> MassTransit's cancel API needs
/// the message type, which the abstraction's opaque token deliberately does not carry, so this
/// class keeps a token-to-type map. That map lives in the DI scope and is not persisted: after a
/// restart the <em>delivery</em> still happens, because the broker holds it, but this process can
/// no longer cancel it. Where cancelling matters across a restart, have the consumer check whether
/// the work is still wanted rather than relying on the token.
/// </para>
/// </remarks>
internal sealed class MassTransitMessageScheduler : IMessageScheduler
{
    private readonly MtScheduler _scheduler;

    // Maps schedule tokens to the message type so CancelScheduledPublish(Type, Guid) can be called.
    // ConcurrentDictionary is used defensively; concurrent access from async consumers is possible.
    private readonly ConcurrentDictionary<Guid, Type> _tokenTypeMap = new();

    public MassTransitMessageScheduler(MtScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public async Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset deliverAt, CancellationToken ct)
        where T : class
    {
        // MassTransit IMessageScheduler.SchedulePublish uses DateTime, not DateTimeOffset.
        var scheduled = await _scheduler
            .SchedulePublish<T>(deliverAt.UtcDateTime, message, ct)
            .ConfigureAwait(false);

        var tokenId = scheduled.TokenId;

        // Record the message type so CancelAsync can invoke the correct generic overload.
        _tokenTypeMap.TryAdd(tokenId, typeof(T));

        return tokenId;
    }

    /// <inheritdoc />
    public async Task CancelAsync(Guid scheduleToken, CancellationToken ct)
    {
        if (!_tokenTypeMap.TryRemove(scheduleToken, out var messageType))
        {
            // Token not found in this scope — already delivered, already cancelled, or from another
            // scope/process. Treat as a no-op per the interface contract.
            return;
        }

        try
        {
            // Use the non-generic CancelScheduledPublish(Type, Guid, CancellationToken) overload
            // so we don't need the message type as a generic type parameter at call time.
            await _scheduler
                .CancelScheduledPublish(messageType, scheduleToken, ct)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cancellation of an already-delivered or unrecognised token is a no-op per contract.
            // MassTransit may throw if the token is no longer tracked — swallow silently.
        }
    }
}
