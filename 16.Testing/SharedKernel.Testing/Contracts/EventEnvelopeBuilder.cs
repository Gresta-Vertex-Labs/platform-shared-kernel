using SharedKernel.Contracts.Events;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Fluent test builder for an <see cref="EventEnvelope{TEvent}"/>, so a test supplies only the metadata it
/// cares about.
/// </summary>
/// <typeparam name="TEvent">
/// The wrapped integration event type. Must carry a valid <see cref="IntegrationEventAttribute"/>, because
/// <see cref="Build"/> goes through <see cref="EventEnvelope.Wrap{TEvent}"/>, the only way to create an envelope.
/// </typeparam>
/// <remarks>
/// <para>
/// Every setter is validated by <see cref="EventEnvelope.Wrap{TEvent}"/> at <see cref="Build"/> time, not when the
/// setter is called, so an invalid value surfaces as the same <see cref="ArgumentException"/> production code
/// would see.
/// </para>
/// <para>
/// Defaults: source <c>test-service</c>, a new compact-GUID correlation id, and no subject, tenant or causation id.
/// </para>
/// </remarks>
public sealed class EventEnvelopeBuilder<TEvent>
    where TEvent : class, IIntegrationEvent
{
    private TEvent? _data;
    private string _source = "test-service";
    private string? _subject;
    private Guid? _tenantId;
    private string? _correlationId = Guid.NewGuid().ToString("N");
    private string? _causationId;

    /// <summary>Sets the wrapped integration event.</summary>
    /// <param name="integrationEvent">The event to wrap. Must not be null.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="integrationEvent"/> is <see langword="null"/>.</exception>
    public EventEnvelopeBuilder<TEvent> WithData(TEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _data = integrationEvent;
        return this;
    }

    /// <summary>Sets the logical name of the producing service. Defaults to <c>test-service</c>.</summary>
    /// <param name="source">The source, such as <c>orders-service</c>.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithSource(string source)
    {
        _source = source;
        return this;
    }

    /// <summary>Sets the resource the event is about, such as <c>order/42</c>. Defaults to <see langword="null"/>.</summary>
    /// <param name="subject">The subject, or <see langword="null"/> for none.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithSubject(string? subject)
    {
        _subject = subject;
        return this;
    }

    /// <summary>Sets the tenant the event belongs to. Defaults to <see langword="null"/>.</summary>
    /// <param name="tenantId">The tenant id, or <see langword="null"/> for an event with no tenant.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithTenantId(Guid? tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Sets the correlation id. Defaults to a new compact GUID.</summary>
    /// <param name="correlationId">The correlation id, or <see langword="null"/> for none.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Sets the causation id. Defaults to <see langword="null"/>.</summary>
    /// <param name="causationId">The causation id, or <see langword="null"/> for none.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EventEnvelopeBuilder<TEvent> WithCausationId(string? causationId)
    {
        _causationId = causationId;
        return this;
    }

    /// <summary>Builds the envelope through <see cref="EventEnvelope.Wrap{TEvent}"/>.</summary>
    /// <returns>A new <see cref="EventEnvelope{TEvent}"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="WithData"/> was never called, or <typeparamref name="TEvent"/> has no valid
    /// <see cref="IntegrationEventAttribute"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A configured value breaks a rule <see cref="EventEnvelope.Wrap{TEvent}"/> enforces.</exception>
    public EventEnvelope<TEvent> Build()
    {
        if (_data is null)
            throw new InvalidOperationException("WithData(...) must be called before Build().");

        return EventEnvelope.Wrap(
            _data,
            _source,
            subject: _subject,
            tenantId: _tenantId,
            correlationId: _correlationId,
            causationId: _causationId);
    }
}
