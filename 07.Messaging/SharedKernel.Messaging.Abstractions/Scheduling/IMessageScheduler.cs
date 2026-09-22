namespace SharedKernel.Messaging.Abstractions.Scheduling;

/// <summary>
/// Asks the broker to hold a message until a chosen time — a reminder, a timeout, a retry after a
/// cool-off — without this process having to stay alive to deliver it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The broker does the waiting, not this process.</strong> RabbitMQ uses the
/// delayed-message exchange and Azure Service Bus uses native scheduled enqueue, so a message
/// scheduled for two hours from now is delivered even if every replica of this service is
/// redeployed in the meantime. That is the whole reason to prefer it over a timer.
/// </para>
/// <para>
/// <strong>RabbitMQ needs a plugin.</strong> The delayed-message exchange is a community plugin
/// (<c>rabbitmq_delayed_message_exchange</c>) that the official <c>rabbitmq</c> image does not
/// ship. Use <c>masstransit/rabbitmq</c>, which has it enabled, or enable it on your own broker.
/// Without it the bus starts normally and the first scheduled message fails when the
/// <c>x-delayed-message</c> exchange cannot be declared.
/// </para>
/// <para>
/// Registered as a scoped service by <c>MessagingBusBuilder.WithDelayedDelivery()</c>. Inject this
/// interface, never <c>MassTransit.IMessageScheduler</c> — the analyzer <c>SK0706</c> enforces
/// that, because a direct injection couples application code to the transport.
/// </para>
/// <para>
/// <strong>Not a job scheduler.</strong> This defers one message once. Recurring and cron work
/// belongs to <c>19.Scheduling</c>, which owns misfire and overlap policy and cross-replica
/// single execution.
/// </para>
/// </remarks>
public interface IMessageScheduler
{
    /// <summary>
    /// Asks the broker to deliver <paramref name="message"/> at <paramref name="deliverAt"/>.
    /// </summary>
    /// <typeparam name="T">The message type to schedule.</typeparam>
    /// <param name="message">The message payload to deliver.</param>
    /// <param name="deliverAt">
    /// When to deliver it. Converted to UTC; a time already in the past is delivered as soon as the
    /// broker sees it rather than rejected.
    /// </param>
    /// <param name="ct">A token to cancel the <em>scheduling call</em> — not the delivery.</param>
    /// <returns>
    /// A token identifying the scheduled message, for <see cref="CancelAsync"/>. Keep it if you may
    /// need to call the delivery off; there is no other way to address it afterwards.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Delivery is at-least-once, like every other delivery on the bus: a scheduled message can
    /// arrive more than once, so its consumer must be idempotent (see
    /// <c>MessagingBusBuilder.WithIdempotency()</c>).
    /// </para>
    /// <para>
    /// <strong>Never use <c>Task.Delay</c> in a consumer as a substitute.</strong> It holds a
    /// thread-pool thread and a broker delivery slot for the whole wait, and it loses the work
    /// entirely when the process exits.
    /// </para>
    /// </remarks>
    Task<Guid> ScheduleAsync<T>(T message, DateTimeOffset deliverAt, CancellationToken ct)
        where T : class;

    /// <summary>
    /// Calls off a scheduled message.
    /// </summary>
    /// <param name="scheduleToken">The token <see cref="ScheduleAsync{T}"/> returned.</param>
    /// <param name="ct">A token to cancel the cancellation call itself.</param>
    /// <returns>A task that completes once the broker has been told.</returns>
    /// <remarks>
    /// A no-op — never an exception — for a token that was already delivered or is unrecognised,
    /// because "cancel something that is already gone" is a normal race, not a caller error.
    /// It follows that a successful return does <strong>not</strong> prove the message was stopped
    /// in time; a consumer must still cope with receiving work that was cancelled.
    /// </remarks>
    Task CancelAsync(Guid scheduleToken, CancellationToken ct);
}
