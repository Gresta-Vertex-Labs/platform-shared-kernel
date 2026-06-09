namespace SharedKernel.Messaging.Abstractions.Faults;

/// <summary>
/// Contract for handling dead-lettered messages delivered as <c>Fault&lt;TMessage&gt;</c> events
/// by the MassTransit fault pipeline.
/// </summary>
/// <typeparam name="TMessage">The original message type that faulted.</typeparam>
/// <remarks>
/// <para>
/// Implement this interface to perform compensating actions, alert operations teams, or triage
/// dead-lettered messages. The adapter in the MassTransit package invokes
/// <see cref="HandleAsync"/> when a <c>Fault&lt;TMessage&gt;</c> is delivered after the retry
/// budget is exhausted.
/// </para>
/// <para>
/// <strong>Registration rule:</strong> Implementations must be registered via
/// <c>MessagingBusBuilder.AddFaultConsumer&lt;TMessage, TConsumer&gt;()</c>.
/// Never register directly via <c>services.AddScoped</c> — the adapter wiring will be missing,
/// and fault messages will not be routed to the handler.
/// </para>
/// <para>
/// <strong>Exception policy:</strong> Exceptions must not be swallowed — rethrow to allow
/// MassTransit fault tracking and enable alerting on repeated failures.
/// </para>
/// </remarks>
public interface IFaultConsumer<TMessage>
    where TMessage : class
{
    /// <summary>
    /// Invoked when a <c>Fault&lt;TMessage&gt;</c> is delivered (dead-lettered message).
    /// </summary>
    /// <param name="faultId">The unique identifier of the fault event assigned by MassTransit.</param>
    /// <param name="faultTimestamp">The UTC timestamp when the fault was recorded by MassTransit.</param>
    /// <param name="faultedMessage">The original message payload that caused the fault.</param>
    /// <param name="exceptions">
    /// Array of <see cref="FaultExceptionInfo"/> records, one per exception recorded during the
    /// failed delivery attempts. Populated from <c>Fault&lt;TMessage&gt;.Exceptions</c> by the
    /// <c>FaultConsumerAdapter</c> in the MassTransit package.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous fault-handling operation.</returns>
    Task HandleAsync(
        Guid faultId,
        DateTimeOffset faultTimestamp,
        TMessage faultedMessage,
        FaultExceptionInfo[] exceptions,
        CancellationToken ct);
}
