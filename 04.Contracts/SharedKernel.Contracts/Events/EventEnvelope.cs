using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Contracts.Events;

/// <summary>
/// The wire format for an integration event: the event as <see cref="Data"/>, with routing and tracing metadata,
/// serialized as a CloudEvents 1.0 structured-mode JSON document.
/// </summary>
/// <typeparam name="TEvent">The concrete integration event type carried as <see cref="Data"/>.</typeparam>
/// <remarks>
/// <para>
/// <b>Construction.</b> Create one with <see cref="EventEnvelope.Wrap{TEvent}"/>. There is no public constructor
/// and no settable property, so an envelope cannot be assembled with missing or inconsistent metadata.
/// </para>
/// <para>
/// <b>Wire shape.</b> Every member has a fixed JSON name from <see cref="CloudEventAttributeNames"/>, so the output
/// does not depend on the serializer's naming policy. Optional members are omitted when absent, as CloudEvents
/// requires. <c>dataversion</c>, <c>tenantid</c>, <c>correlationid</c> and <c>causationid</c> are extension
/// attributes; any CloudEvents-aware tool can read the rest.
/// </para>
/// <para>
/// <b>Deserialization.</b> Reading an envelope applies the same rules as <see cref="EventEnvelope.Wrap{TEvent}"/>,
/// and additionally requires <c>type</c> to match <typeparamref name="TEvent"/>'s declared name and <c>id</c> and
/// <c>time</c> to match the data. A document that breaks a rule throws <see cref="JsonException"/>, so a
/// misrouted or tampered message fails at the edge instead of deep in a handler.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// {
///   "specversion": "1.0",
///   "id": "0199a1b2-...",
///   "source": "orders-service",
///   "type": "orders.order-placed",
///   "dataversion": 1,
///   "time": "2026-09-15T12:00:00+00:00",
///   "subject": "order/0199a1b2-...",
///   "datacontenttype": "application/json",
///   "tenantid": "7c9e6679-...",
///   "correlationid": "4bf92f3577b34da6a3ce929d0e0e4736",
///   "data": { "eventId": "0199a1b2-...", "occurredOn": "2026-09-15T12:00:00+00:00", "orderId": "..." }
/// }
/// </code>
/// </example>
public sealed record EventEnvelope<TEvent>
    where TEvent : class, IIntegrationEvent
{
    [JsonConstructor]
    internal EventEnvelope(
        string specVersion,
        Guid id,
        string source,
        string type,
        int dataVersion,
        DateTimeOffset time,
        string? subject,
        string dataContentType,
        Guid? tenantId,
        string? correlationId,
        string? causationId,
        TEvent data)
    {
        var problem = EventEnvelope.FindProblem(source, subject, tenantId, correlationId, causationId, data)?.Message
            ?? FindWireProblem(specVersion, id, type, dataVersion, time, dataContentType, data);

        if (problem is not null)
            throw new JsonException($"Invalid event envelope for '{typeof(TEvent).FullName}': {problem}");

        SpecVersion = specVersion;
        Id = id;
        Source = source;
        Type = type;
        DataVersion = dataVersion;
        Time = time;
        Subject = subject;
        DataContentType = dataContentType;
        TenantId = tenantId;
        CorrelationId = correlationId;
        CausationId = causationId;
        Data = data;
    }

    private EventEnvelope(
        TEvent data,
        IntegrationEventDescriptor descriptor,
        string source,
        string? subject,
        Guid? tenantId,
        string? correlationId,
        string? causationId)
    {
        SpecVersion = EventEnvelope.CloudEventsSpecVersion;
        Id = data.EventId;
        Source = source;
        Type = descriptor.Name;
        DataVersion = descriptor.Version;
        Time = data.OccurredOn;
        Subject = subject;
        DataContentType = EventEnvelope.JsonContentType;
        TenantId = tenantId;
        CorrelationId = correlationId;
        CausationId = causationId;
        Data = data;
    }

    /// <summary>Gets the CloudEvents specification version, always <c>1.0</c>.</summary>
    [JsonPropertyName(CloudEventAttributeNames.SpecVersion)]
    public string SpecVersion { get; }

    /// <summary>Gets the event identifier, equal to <see cref="IIntegrationEvent.EventId"/> of <see cref="Data"/>.</summary>
    /// <remarks>Consumers deduplicate on this value together with <see cref="Source"/>.</remarks>
    [JsonPropertyName(CloudEventAttributeNames.Id)]
    public Guid Id { get; }

    /// <summary>Gets the logical name of the producing service, such as <c>orders-service</c>.</summary>
    [JsonPropertyName(CloudEventAttributeNames.Source)]
    public string Source { get; }

    /// <summary>
    /// Gets the event's wire name from <see cref="IntegrationEventAttribute.Name"/>, such as
    /// <c>orders.order-placed</c>.
    /// </summary>
    [JsonPropertyName(CloudEventAttributeNames.Type)]
    public string Type { get; }

    /// <summary>
    /// Gets the schema version of <see cref="Data"/> from <see cref="IntegrationEventAttribute.Version"/>, at
    /// least 1.
    /// </summary>
    /// <remarks>
    /// On a received envelope this is the producer's version, which can differ from the version declared by the
    /// consumer's <typeparamref name="TEvent"/> while a schema change rolls out.
    /// </remarks>
    [JsonPropertyName(CloudEventAttributeNames.DataVersion)]
    public int DataVersion { get; }

    /// <summary>Gets the time the fact occurred, equal to <see cref="IIntegrationEvent.OccurredOn"/> of <see cref="Data"/>.</summary>
    [JsonPropertyName(CloudEventAttributeNames.Time)]
    public DateTimeOffset Time { get; }

    /// <summary>
    /// Gets the resource the event is about, such as <c>order/42</c>, or <see langword="null"/> when not set.
    /// </summary>
    /// <remarks>Brokers and subscribers can filter on it without reading <see cref="Data"/>.</remarks>
    [JsonPropertyName(CloudEventAttributeNames.Subject)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subject { get; }

    /// <summary>Gets the media type of <see cref="Data"/>, always <c>application/json</c> for an envelope this package creates.</summary>
    [JsonPropertyName(CloudEventAttributeNames.DataContentType)]
    public string DataContentType { get; }

    /// <summary>Gets the tenant the event belongs to, or <see langword="null"/> for an event with no tenant.</summary>
    /// <remarks>
    /// Lets a consumer, a dead-letter inspector or a replay tool scope the event without reading
    /// <see cref="Data"/>. It is set only from the value passed to <see cref="EventEnvelope.Wrap{TEvent}"/>, never
    /// inferred from the data.
    /// </remarks>
    [JsonPropertyName(CloudEventAttributeNames.TenantId)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? TenantId { get; }

    /// <summary>
    /// Gets the identifier shared by every message in one end-to-end flow, or <see langword="null"/> when not set.
    /// </summary>
    [JsonPropertyName(CloudEventAttributeNames.CorrelationId)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; }

    /// <summary>
    /// Gets the identifier of the command or event that directly caused this event, or <see langword="null"/> for
    /// an event with no recorded cause.
    /// </summary>
    [JsonPropertyName(CloudEventAttributeNames.CausationId)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CausationId { get; }

    /// <summary>Gets the integration event.</summary>
    [JsonPropertyName(CloudEventAttributeNames.Data)]
    public TEvent Data { get; }

    internal static EventEnvelope<TEvent> Create(
        TEvent data,
        IntegrationEventDescriptor descriptor,
        string source,
        string? subject,
        Guid? tenantId,
        string? correlationId,
        string? causationId) =>
        new(data, descriptor, source, subject, tenantId, correlationId, causationId);

    private static string? FindWireProblem(
        string specVersion,
        Guid id,
        string type,
        int dataVersion,
        DateTimeOffset time,
        string dataContentType,
        TEvent data)
    {
        if (specVersion != EventEnvelope.CloudEventsSpecVersion)
            return $"'{CloudEventAttributeNames.SpecVersion}' must be '{EventEnvelope.CloudEventsSpecVersion}'.";

        string expectedType;
        try
        {
            expectedType = IntegrationEventDescriptor.For<TEvent>().Name;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        if (type != expectedType)
            return $"'{CloudEventAttributeNames.Type}' is '{type}' but the target type declares '{expectedType}'.";

        if (dataVersion < 1)
            return $"'{CloudEventAttributeNames.DataVersion}' must be at least 1.";

        if (id != data.EventId)
            return $"'{CloudEventAttributeNames.Id}' does not match the data's event identifier.";

        if (time != data.OccurredOn)
            return $"'{CloudEventAttributeNames.Time}' does not match the data's occurrence time.";

        if (!IsJsonMediaType(dataContentType))
            return $"'{CloudEventAttributeNames.DataContentType}' must be a JSON media type.";

        return null;
    }

    private static bool IsJsonMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return false;

        var essence = mediaType.AsSpan();
        var parameters = essence.IndexOf(';');
        if (parameters >= 0)
            essence = essence[..parameters];

        essence = essence.Trim();
        return essence.Equals(EventEnvelope.JsonContentType, StringComparison.OrdinalIgnoreCase)
            || essence.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Creates <see cref="EventEnvelope{TEvent}"/> instances, the only way to build one.
/// </summary>
public static class EventEnvelope
{
    /// <summary>The CloudEvents specification version every envelope declares: <c>1.0</c>.</summary>
    public const string CloudEventsSpecVersion = "1.0";

    /// <summary>The media type every envelope created by <see cref="Wrap{TEvent}"/> declares: <c>application/json</c>.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>
    /// Wraps an integration event in an envelope, taking <c>id</c> and <c>time</c> from the event and
    /// <c>type</c> and <c>dataversion</c> from its <see cref="IntegrationEventAttribute"/>.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The concrete type of <paramref name="integrationEvent"/>. Must be the runtime type, not a base type or an
    /// interface, so the data serializes with all its members.
    /// </typeparam>
    /// <param name="integrationEvent">The event to wrap. Must not be null.</param>
    /// <param name="source">
    /// The logical name of the producing service, such as <c>orders-service</c>. Must be a non-blank URI reference
    /// of at most 256 characters.
    /// </param>
    /// <param name="subject">
    /// The resource the event is about, such as <c>order/42</c>, or <see langword="null"/>. Must not be blank when
    /// set.
    /// </param>
    /// <param name="tenantId">
    /// The tenant the event belongs to, or <see langword="null"/> for an event with no tenant. Must not be
    /// <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="correlationId">
    /// The flow's correlation identifier, or <see langword="null"/>. Must not be blank when set.
    /// </param>
    /// <param name="causationId">
    /// The identifier of the command or event that caused this one, or <see langword="null"/>. Must not be blank
    /// when set.
    /// </param>
    /// <returns>The envelope.</returns>
    /// <remarks>
    /// <b>Pitfall.</b> <paramref name="subject"/>, <paramref name="correlationId"/> and
    /// <paramref name="causationId"/> are all optional strings. Pass them by name.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="integrationEvent"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TEvent"/> is not the runtime type of <paramref name="integrationEvent"/>; the event's
    /// <see cref="IIntegrationEvent.EventId"/> is empty or its <see cref="IIntegrationEvent.OccurredOn"/> is
    /// <see langword="default"/>; or an argument breaks the rule stated for it.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TEvent"/> has no valid <see cref="IntegrationEventAttribute"/>; see
    /// <see cref="IntegrationEventDescriptor.For{TEvent}"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// var envelope = EventEnvelope.Wrap(
    ///     orderPlaced,
    ///     source: "orders-service",
    ///     subject: $"order/{orderPlaced.OrderId}",
    ///     tenantId: tenantId,
    ///     correlationId: Activity.Current?.TraceId.ToString());
    /// </code>
    /// </example>
    public static EventEnvelope<TEvent> Wrap<TEvent>(
        TEvent integrationEvent,
        string source,
        string? subject = null,
        Guid? tenantId = null,
        string? correlationId = null,
        string? causationId = null)
        where TEvent : class, IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        if (integrationEvent.GetType() != typeof(TEvent))
        {
            throw new ArgumentException(
                $"Wrap the event as its runtime type '{integrationEvent.GetType().FullName}', not as "
                + $"'{typeof(TEvent).FullName}'; otherwise its data would serialize without the runtime type's members.",
                nameof(integrationEvent));
        }

        if (FindProblem(source, subject, tenantId, correlationId, causationId, integrationEvent) is { } problem)
            throw new ArgumentException(problem.Message, problem.Parameter);

        var descriptor = IntegrationEventDescriptor.For<TEvent>();

        return EventEnvelope<TEvent>.Create(
            integrationEvent, descriptor, source, subject, tenantId, correlationId, causationId);
    }

    internal static (string Parameter, string Message)? FindProblem(
        string? source,
        string? subject,
        Guid? tenantId,
        string? correlationId,
        string? causationId,
        IIntegrationEvent? data)
    {
        const int maxSourceLength = 256;

        if (string.IsNullOrWhiteSpace(source))
            return ("source", "Source must not be null or blank.");

        if (source.Length > maxSourceLength || !Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out _))
            return ("source", $"Source must be a URI reference of at most {maxSourceLength} characters.");

        if (subject is not null && string.IsNullOrWhiteSpace(subject))
            return ("subject", "Subject must be null or not blank.");

        if (tenantId == Guid.Empty)
            return ("tenantId", "TenantId must be null or not Guid.Empty.");

        if (correlationId is not null && string.IsNullOrWhiteSpace(correlationId))
            return ("correlationId", "CorrelationId must be null or not blank.");

        if (causationId is not null && string.IsNullOrWhiteSpace(causationId))
            return ("causationId", "CausationId must be null or not blank.");

        if (data is null)
            return ("integrationEvent", "Data must not be null.");

        if (data.EventId == Guid.Empty)
            return ("integrationEvent", "The event's EventId must not be Guid.Empty.");

        if (data.OccurredOn == default)
            return ("integrationEvent", "The event's OccurredOn must not be default.");

        return null;
    }
}
