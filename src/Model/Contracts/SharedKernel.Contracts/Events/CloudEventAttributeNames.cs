namespace SharedKernel.Contracts.Events;

/// <summary>
/// The JSON member names <see cref="EventEnvelope{TEvent}"/> uses on the wire: the CloudEvents 1.0 context
/// attributes, plus this platform's extension attributes.
/// </summary>
/// <remarks>
/// Use these constants when reading an envelope without deserializing it, for example in a dead-letter tool, a
/// broker filter or a log query, instead of retyping the strings.
/// </remarks>
public static class CloudEventAttributeNames
{
    /// <summary>The CloudEvents specification version: <c>specversion</c>.</summary>
    public const string SpecVersion = "specversion";

    /// <summary>The event identifier: <c>id</c>.</summary>
    public const string Id = "id";

    /// <summary>The producing service: <c>source</c>.</summary>
    public const string Source = "source";

    /// <summary>The event's wire name: <c>type</c>.</summary>
    public const string Type = "type";

    /// <summary>The time the fact occurred: <c>time</c>.</summary>
    public const string Time = "time";

    /// <summary>The resource the event is about: <c>subject</c>.</summary>
    public const string Subject = "subject";

    /// <summary>The media type of the data: <c>datacontenttype</c>.</summary>
    public const string DataContentType = "datacontenttype";

    /// <summary>The event payload: <c>data</c>.</summary>
    public const string Data = "data";

    /// <summary>Extension attribute: the schema version of the data, <c>dataversion</c>.</summary>
    public const string DataVersion = "dataversion";

    /// <summary>Extension attribute: the tenant the event belongs to, <c>tenantid</c>.</summary>
    public const string TenantId = "tenantid";

    /// <summary>Extension attribute: the correlation identifier of the flow, <c>correlationid</c>.</summary>
    public const string CorrelationId = "correlationid";

    /// <summary>Extension attribute: the identifier of the message that caused this event, <c>causationid</c>.</summary>
    public const string CausationId = "causationid";
}
