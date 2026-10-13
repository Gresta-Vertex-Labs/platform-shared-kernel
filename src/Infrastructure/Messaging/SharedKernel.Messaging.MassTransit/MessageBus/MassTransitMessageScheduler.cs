using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.Abstractions.Scheduling;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Internal;

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
/// <para>
/// <strong>Caller cancellation propagates.</strong> <see cref="CancelAsync"/> never reports success to a
/// caller who cancelled it: an <see cref="OperationCanceledException"/> reaches the caller, and the token
/// stays registered so the cancel can be retried. Only a gone or unrecognised token is a silent no-op, and a recognised transport
/// fault is swallowed with a warning; any other exception propagates.
/// </para>
/// </remarks>
internal sealed class MassTransitMessageScheduler : IMessageScheduler
{
    private readonly MtScheduler _scheduler;
    private readonly ILogger<MassTransitMessageScheduler> _logger;

    // Maps schedule tokens to the message type so CancelScheduledPublish(Type, Guid) can be called.
    // ConcurrentDictionary is used defensively; concurrent access from async consumers is possible.
    private readonly ConcurrentDictionary<Guid, Type> _tokenTypeMap = new();

    public MassTransitMessageScheduler(MtScheduler scheduler, ILogger<MassTransitMessageScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _scheduler = scheduler;
        _logger = logger;
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
        catch (OperationCanceledException)
        {
            // The caller cancelled the call, so the schedule was not cancelled. Put the entry back so the
            // same token can still be cancelled with a live token, then let the cancellation propagate.
            _tokenTypeMap.TryAdd(scheduleToken, messageType);
            throw;
        }
        catch (Exception ex)
        {
            // A recognised transport fault (the broker unreachable, a timeout, a refused operation) is the only
            // thing swallowed: the contract makes cancelling a gone token a no-op and this is logged so the cause
            // is not lost. Anything else is a bug and propagates with its stack intact, token restored so the same
            // token can be cancelled again.
            if (MessagingExceptionClassifier.TryClassify(ex, messageType.Name, "cancel") is not null)
            {
                MessagingLog.SchedulerCancelFailed(_logger, messageType.Name, ex);
                return;
            }

            _tokenTypeMap.TryAdd(scheduleToken, messageType);
            throw;
        }
    }
}
