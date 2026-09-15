using SharedKernel.Contracts.Events;

namespace SharedKernel.Messaging.Abstractions.EventPublisher;

/// <summary>
/// Abstraction for publishing CloudEvents-compliant integration events across service boundaries.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IEventPublisher"/> is for <strong>integration events only</strong> — types implementing
/// <see cref="IIntegrationEvent"/> that cross service boundaries. In-process domain events are dispatched by
/// <c>IDomainEventDispatcher</c> (from <c>03.Domain</c>), not by <see cref="IEventPublisher"/>, and are never put
/// on the wire.
/// </para>
/// <para>
/// The correct flow: domain event → <c>IDomainEventDispatcher</c> → application event handler → map to an
/// <see cref="IIntegrationEvent"/> → <see cref="IEventPublisher"/>.
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
    /// <typeparam name="TEvent">
    /// The concrete integration event type. Must implement <see cref="IIntegrationEvent"/>, be the runtime type of
    /// <paramref name="integrationEvent"/>, and carry a valid <see cref="IntegrationEventAttribute"/>.
    /// </typeparam>
    /// <param name="integrationEvent">The integration event to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the event has been accepted by the transport.</returns>
    /// <remarks>
    /// The MassTransit implementation wraps <typeparamref name="TEvent"/> in <see cref="EventEnvelope{TEvent}"/>
    /// before sending to the transport, constructed exclusively via <see cref="EventEnvelope.Wrap{TEvent}"/> —
    /// never a raw object-initializer/constructor call. <see cref="EventEnvelope{TEvent}.Type"/> and
    /// <see cref="EventEnvelope{TEvent}.DataVersion"/> come from the event's <see cref="IntegrationEventAttribute"/>;
    /// <see cref="EventEnvelope{TEvent}.Id"/> and <see cref="EventEnvelope{TEvent}.Time"/> come from the event
    /// itself. <see cref="EventEnvelope{TEvent}.CorrelationId"/> is taken from
    /// <see cref="PublishContext.CorrelationId"/> when set, otherwise from <c>Activity.Current?.TraceId</c> when
    /// available, otherwise a new identifier. <see cref="EventEnvelope{TEvent}.Source"/> is sourced from
    /// <c>MessagingOptions.ServiceName</c>. <see cref="EventEnvelope{TEvent}.TenantId"/>,
    /// <see cref="EventEnvelope{TEvent}.CausationId"/> and <see cref="EventEnvelope{TEvent}.Subject"/> are sourced
    /// from <see cref="PublishContext"/> when set by a registered header propagator or the <c>configure</c>
    /// callback; otherwise omitted.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TEvent"/> is not the runtime type of <paramref name="integrationEvent"/>, or the event's
    /// <see cref="IIntegrationEvent.EventId"/> or <see cref="IIntegrationEvent.OccurredOn"/> is unset.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TEvent"/> has no valid <see cref="IntegrationEventAttribute"/>.
    /// </exception>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : class, IIntegrationEvent;

    /// <summary>
    /// Publishes a CloudEvents-compliant integration event with explicit envelope metadata override.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The concrete integration event type. Must implement <see cref="IIntegrationEvent"/>, be the runtime type of
    /// <paramref name="integrationEvent"/>, and carry a valid <see cref="IntegrationEventAttribute"/>.
    /// </typeparam>
    /// <param name="integrationEvent">The integration event to publish.</param>
    /// <param name="configure">
    /// Callback to override <c>CorrelationId</c>, <c>CausationId</c>, <c>TenantId</c>, <c>Subject</c> or the
    /// partition key, or to add custom transport headers. Runs after any registered header propagators, so its
    /// values win.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the event has been accepted by the transport.</returns>
    /// <remarks>Envelope fields are populated as described on <see cref="PublishAsync{TEvent}(TEvent, CancellationToken)"/>.</remarks>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TEvent"/> is not the runtime type of <paramref name="integrationEvent"/>, or the event's
    /// <see cref="IIntegrationEvent.EventId"/> or <see cref="IIntegrationEvent.OccurredOn"/> is unset.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TEvent"/> has no valid <see cref="IntegrationEventAttribute"/>.
    /// </exception>
    Task PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct)
        where TEvent : class, IIntegrationEvent;
}
