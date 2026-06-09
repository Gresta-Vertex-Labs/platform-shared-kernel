namespace SharedKernel.Messaging.Abstractions.Scheduling;

/// <summary>
/// Transport-agnostic abstraction for scheduling messages for deferred delivery.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a scoped service by <c>MessagingBusBuilder.WithInMemoryScheduler()</c> or
/// <c>MessagingBusBuilder.WithQuartzScheduler()</c>. Inject <see cref="IMessageScheduler"/>
/// in application handlers — never inject <c>MassTransit.IMessageScheduler</c> directly.
/// </para>
/// </remarks>
public interface IMessageScheduler
{
    /// <summary>
    /// Schedules message <typeparamref name="T"/> for delivery at <paramref name="deliverAt"/> (UTC).
    /// </summary>
    /// <typeparam name="T">The message type to schedule.</typeparam>
    /// <param name="message">The message payload to deliver.</param>
    /// <param name="deliverAt">The UTC date and time at which the message should be delivered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A schedule token (<see cref="Guid"/>) that can be passed to
    /// <see cref="CancelAsync"/> to cancel delivery before <paramref name="deliverAt"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Durability caution:</strong> In-memory tokens do not survive process restarts.
    /// If the process restarts before <paramref name="deliverAt"/>, the scheduled message is lost.
    /// For durable scheduling that survives restarts, configure
    /// <c>MessagingBusBuilder.WithQuartzScheduler()</c> instead of <c>WithInMemoryScheduler()</c>.
    /// </para>
    /// <para>
    /// Never use <c>Task.Delay</c> inside consumers as a substitute — it blocks thread-pool threads
    /// and cannot survive process restarts.
    /// </para>
    /// </remarks>
    Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset deliverAt, CancellationToken ct)
        where T : class;

    /// <summary>
    /// Cancels a previously scheduled message identified by <paramref name="scheduleToken"/>.
    /// </summary>
    /// <param name="scheduleToken">
    /// The token returned by a prior call to <see cref="ScheduleAsync{T}"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the cancellation request has been processed.</returns>
    /// <remarks>
    /// This is a no-op if the message has already been delivered or if the token is unrecognized.
    /// No exception is thrown for an unrecognized or already-delivered token.
    /// </remarks>
    Task CancelAsync(Guid scheduleToken, CancellationToken ct);
}
