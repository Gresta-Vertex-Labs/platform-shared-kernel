using SharedKernel.Contracts.Events;
using SharedKernel.Domain.Events;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Fluent test builder for constructing an <see cref="EventEnvelope{TEvent}"/> without knowing
/// every metadata field.
/// </summary>
/// <typeparam name="TEvent">The wrapped domain event type.</typeparam>
public sealed class EventEnvelopeBuilder<TEvent>
    where TEvent : IDomainEvent
{
    private TEvent? _payload;
    private string _sourceService = "test-service";
    private string _correlationId = Guid.NewGuid().ToString("N");
    private string? _causationId;

    /// <summary>Sets the wrapped domain event.</summary>
    /// <param name="event">The domain event to wrap.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithPayload(TEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _payload = @event;
        return this;
    }

    /// <summary>Sets the source service name. Defaults to <c>"test-service"</c>.</summary>
    /// <param name="name">The source service name.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithSourceService(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _sourceService = name;
        return this;
    }

    /// <summary>Sets the correlation id. Defaults to a new compact GUID.</summary>
    /// <param name="id">The correlation id.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithCorrelationId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _correlationId = id;
        return this;
    }

    /// <summary>Sets the causation id. Defaults to <see langword="null"/>.</summary>
    /// <param name="id">The causation id.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithCausationId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _causationId = id;
        return this;
    }

    /// <summary>Builds the configured <see cref="EventEnvelope{TEvent}"/>.</summary>
    /// <returns>A new <see cref="EventEnvelope{TEvent}"/>.</returns>
    /// <exception cref="InvalidOperationException"><see cref="WithPayload"/> was never called.</exception>
    public EventEnvelope<TEvent> Build()
    {
        if (_payload is null)
            throw new InvalidOperationException("WithPayload(...) must be called before Build().");

        return EventEnvelope.Wrap(_payload, _sourceService, _correlationId, _causationId);
    }
}
