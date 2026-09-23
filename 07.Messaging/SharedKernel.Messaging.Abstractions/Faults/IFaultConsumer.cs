namespace SharedKernel.Messaging.Abstractions.Faults;

/// <summary>
/// Observes a message that has failed every retry it was going to get, so the failure becomes
/// visible instead of only ending up in a dead-letter queue nobody reads.
/// </summary>
/// <typeparam name="TMessage">The message type that faulted.</typeparam>
/// <remarks>
/// <para>
/// <strong>This is an observer, not a recovery mechanism.</strong> By the time it runs, the retry
/// budget is exhausted and MassTransit has already moved the message to the error queue. Nothing a
/// fault consumer does puts the message back. Use it to alert, to record the failure somewhere a
/// human will look, or to compensate work the failed message had already half-completed.
/// </para>
/// <para>
/// <strong>Register it with</strong>
/// <c>MessagingBusBuilder.AddFaultConsumer&lt;TMessage, TConsumer&gt;()</c> — never a bare
/// <c>services.AddScoped</c>. The builder also registers the adapter that consumes
/// <c>Fault&lt;TMessage&gt;</c> and calls this interface; without it the type is registered and
/// never invoked, which looks exactly like a message that never faulted.
/// </para>
/// <para>
/// <strong>Do not swallow exceptions.</strong> A fault consumer that throws is itself faulted and
/// tracked by MassTransit, which is what makes "our alerting is broken" visible. Catching
/// everything to be safe hides the second failure behind the first.
/// </para>
/// </remarks>
public interface IFaultConsumer<TMessage>
    where TMessage : class
{
    /// <summary>
    /// Handles one faulted message.
    /// </summary>
    /// <param name="faultId">
    /// MassTransit's identifier for this fault event. Useful as a deduplication key: a fault, like
    /// any other message, can be delivered more than once.
    /// </param>
    /// <param name="faultTimestamp">When MassTransit recorded the fault (UTC).</param>
    /// <param name="faultedMessage">
    /// The original message. Deserialized from the fault event, so it is a copy — mutating it
    /// affects nothing.
    /// </param>
    /// <param name="exceptions">
    /// One entry per exception recorded across the failed delivery attempts, in the order
    /// MassTransit recorded them, so the first is usually the original cause. Read-only since
    /// P-560: an array in a public contract lets a handler mutate the caller's state, and nothing
    /// downstream benefits from that.
    /// </param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A task that completes when the fault has been handled.</returns>
    Task HandleAsync(
        Guid faultId,
        DateTimeOffset faultTimestamp,
        TMessage faultedMessage,
        IReadOnlyList<FaultExceptionInfo> exceptions,
        CancellationToken ct);
}
