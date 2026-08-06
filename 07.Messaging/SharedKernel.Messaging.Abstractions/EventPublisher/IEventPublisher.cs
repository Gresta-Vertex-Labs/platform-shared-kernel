namespace SharedKernel.Messaging.Abstractions.EventPublisher;

/// <summary>
/// Abstraction for publishing CloudEvents-compliant integration events across service boundaries.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IEventPublisher"/> is for <strong>integration events only</strong> — events that cross
/// service boundaries. In-process domain events are dispatched by <c>IDomainEventDispatcher</c> (from
/// <c>03.Domain</c>), not by <see cref="IEventPublisher"/>.
/// </para>
/// <para>
/// The correct flow: domain event → <c>IDomainEventDispatcher</c> → application event handler →
/// <see cref="IEventPublisher"/> (to emit the integration event).
/// </para>
/// <para>
/// <strong>Hard violations:</strong>
/// Never call <see cref="IEventPublisher"/> from domain entities, value objects, or aggregate roots.
/// Never register <see cref="IEventPublisher"/> as a singleton — it is a scoped service.
/// Singleton registration breaks MassTransit's per-consume-scope semantics.
/// </para>
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a CloudEvents-compliant integration event.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type. Must implement <c>IDomainEvent</c>.</typeparam>
    /// <param name="integrationEvent">The integration event payload to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the event has been accepted by the transport.</returns>
    /// <remarks>
    /// The MassTransit implementation wraps <typeparamref name="TEvent"/> in <c>EventEnvelope&lt;TEvent&gt;</c>
    /// (from <c>04.Contracts</c>) before sending to the transport, constructed exclusively via
    /// <c>EventEnvelope.Wrap&lt;TEvent&gt;()</c> — <c>04.Contracts</c>'s own mandated factory — never a raw
    /// object-initializer/constructor call (P-340/WO-054).
    /// <c>CorrelationId</c> and <c>CausationId</c> are propagated from <c>Activity.Current?.TraceId</c>
    /// when available. <c>SourceService</c> is sourced from <c>MessagingOptions.ServiceName</c>.
    /// <c>TenantId</c> is sourced from <see cref="PublishContext.TenantId"/> when explicitly set;
    /// otherwise omitted (<c>null</c>), exactly like an unset <c>CorrelationId</c>/<c>CausationId</c>
    /// (P-340/WO-054).
    /// </remarks>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : class;

    /// <summary>
    /// Publishes a CloudEvents-compliant integration event with explicit envelope metadata override.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type. Must implement <c>IDomainEvent</c>.</typeparam>
    /// <param name="integrationEvent">The integration event payload to publish.</param>
    /// <param name="configure">Callback to override <c>CorrelationId</c>, <c>CausationId</c>, or add custom transport headers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the event has been accepted by the transport.</returns>
    Task PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct) where TEvent : class;
}
