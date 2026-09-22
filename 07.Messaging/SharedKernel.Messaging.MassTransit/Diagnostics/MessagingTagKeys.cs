namespace SharedKernel.Messaging.MassTransit.Diagnostics;

/// <summary>
/// OpenTelemetry attribute keys this package sets on its <see cref="System.Diagnostics.Activity"/>
/// spans and metric measurements.
/// </summary>
/// <remarks>
/// <para>
/// Added by P-560 to close four SK0022 violations — <c>Activity.SetTag</c> and metric tags were
/// written with raw literals at every call site. The values are a queryable contract: dashboards
/// and alerts filter on them, so a typo at one site silently splits a series in two, and a rename
/// has to happen everywhere at once.
/// </para>
/// <para>
/// Deliberately domain-local rather than promoted to <c>01.Core</c>'s <c>WellKnownTagKeys</c>.
/// That registry is for attribute names shared <em>across</em> domains — tenant id, correlation id,
/// error code. These describe a message, are read by nothing outside <c>07.Messaging</c>, and the
/// root brain's rule routes exactly this case to "a domain-local static constants class in the
/// owning package", alongside <c>SecurityClaimTypes</c> and <c>WebhookSignatureHeaders</c>. This
/// domain already set the same precedent with <see cref="Logging.MessagingLogScope"/>'s
/// <c>CorrelationIdKey</c>.
/// </para>
/// <para>
/// Test assertions deliberately keep the raw literal: a test that checks the runtime string value
/// independently of the production authoring path still fails if the constant's value changes,
/// which is the point.
/// </para>
/// </remarks>
internal static class MessagingTagKeys
{
    /// <summary>
    /// CLR type name of the message being published, sent or consumed.
    /// </summary>
    /// <remarks>
    /// Used for plain <c>IMessageBus</c> traffic and on the consume path. Integration events use
    /// <see cref="EventType"/> instead, because their declared wire name is the more useful
    /// grouping key than the CLR class name.
    /// </remarks>
    public const string MessageType = "messaging.message_type";

    /// <summary>
    /// The integration event's declared <c>[IntegrationEvent]</c> name — never the CLR class name,
    /// so renaming the class does not break a dashboard.
    /// </summary>
    public const string EventType = "messaging.event_type";

    /// <summary>The destination endpoint path a message was consumed from.</summary>
    public const string Destination = "messaging.destination";
}
