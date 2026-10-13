namespace SharedKernel.Messaging.MassTransit.Diagnostics;

/// <summary>
/// OpenTelemetry attribute keys this package sets on its <see cref="System.Diagnostics.Activity"/> spans and metric
/// measurements. The outcome tag is <c>SharedKernel.Primitives.Propagation.WellKnownTagKeys.Outcome</c>.
/// </summary>
/// <remarks>
/// These values are a queryable contract: dashboards and alerts filter on them, so a rename is a breaking change.
/// Keys that describe a message and are read by nothing outside messaging live here rather than in
/// <c>WellKnownTagKeys</c>; where OpenTelemetry's messaging conventions define a key, that key is used.
/// </remarks>
public static class MessagingTagKeys
{
    /// <summary>
    /// CLR type name of the message being published, sent or consumed. Integration events use
    /// <see cref="EventType"/> on the publish side instead.
    /// </summary>
    public const string MessageType = "messaging.message_type";

    /// <summary>
    /// The integration event's declared <c>[IntegrationEvent]</c> name, never the CLR class name, so renaming the class
    /// does not break a dashboard.
    /// </summary>
    public const string EventType = "messaging.event_type";

    /// <summary>The destination endpoint path a message was consumed from (OpenTelemetry <c>messaging.destination.name</c>).</summary>
    public const string Destination = "messaging.destination.name";

    /// <summary>The number of messages in a consumed batch (OpenTelemetry <c>messaging.batch.message_count</c>).</summary>
    public const string BatchMessageCount = "messaging.batch.message_count";
}
