using System.Collections.Concurrent;
using SharedKernel.Messaging.Abstractions.Scheduling;

// Alias MassTransit's IMessageScheduler to avoid name collision with our abstraction.
using MtScheduler = MassTransit.IMessageScheduler;

namespace SharedKernel.Messaging.MassTransit.MessageBus;

/// <summary>
/// MassTransit implementation of <see cref="IMessageScheduler"/>.
/// Delegates scheduling to MassTransit's <c>IMessageScheduler</c> and maps the returned
/// <c>ScheduledMessage.TokenId</c> to the opaque <see cref="Guid"/> token exposed by
/// the abstraction.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a scoped service by <c>MessagingBusBuilder.WithInMemoryScheduler()</c> or
/// <c>MessagingBusBuilder.WithQuartzScheduler()</c>.
/// </para>
/// <para>
/// Maintains an internal token-to-message-type mapping to support
/// <see cref="CancelAsync"/> without exposing the message type at call time.
/// The mapping is per-scope and is not persisted across process restarts.
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
