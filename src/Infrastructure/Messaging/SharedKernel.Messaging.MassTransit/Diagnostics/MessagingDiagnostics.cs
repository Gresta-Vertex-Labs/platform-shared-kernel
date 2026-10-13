using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Messaging.MassTransit.Diagnostics;

/// <summary>
/// Holds the process-lifetime <see cref="ActivitySource"/> and <see cref="Meter"/> (both named
/// <see cref="MessagingTelemetry.SourceName"/>) and the instruments of <c>SharedKernel.Messaging.MassTransit</c>.
/// </summary>
/// <remarks>
/// <para>
/// A static source and meter are the platform-standard .NET diagnostics shape: they carry no mutable business state
/// and are shared by every call site in the assembly. They are the only sanctioned static fields of this domain.
/// </para>
/// <para>
/// The meter is not created through <c>IMeterFactory</c> because the instruments are recorded from the consumer base
/// classes, whose public constructors take only an <c>ILogger</c> (a factory parameter would be a breaking change on
/// types services derive from). Tests therefore filter measurements by a test-unique tag value, as listeners are
/// process-wide.
/// </para>
/// <para>
/// This source is owned here; <c>13.ServiceDefaults</c> only subscribes to the names.
/// </para>
/// </remarks>
internal static class MessagingDiagnostics
{
    private static readonly string Version = TelemetryVersion.Of(typeof(MessagingTelemetry).Assembly);

    /// <summary>The single <see cref="ActivitySource"/> for the whole package.</summary>
    public static readonly ActivitySource ActivitySource = new(MessagingTelemetry.SourceName, Version);

    /// <summary>The single <see cref="Meter"/> for the whole package.</summary>
    public static readonly Meter Meter = new(MessagingTelemetry.MeterName, Version);

    /// <summary>
    /// Counts publish attempts (<c>IMessageBus.PublishAsync</c> and <c>IEventPublisher.PublishAsync</c>), tagged with the
    /// message or event type and the <c>outcome</c>. Filter <c>outcome=success</c> for published messages.
    /// </summary>
    public static readonly Counter<long> PublishCounter = Meter.CreateCounter<long>(
        name: "sharedkernel.messaging.publish.count",
        unit: "{message}",
        description: "Number of publish attempts, tagged with the outcome.");

    /// <summary>Counts <c>IMessageBus.SendAsync</c> attempts, tagged with the message type and the <c>outcome</c>.</summary>
    public static readonly Counter<long> SendCounter = Meter.CreateCounter<long>(
        name: "sharedkernel.messaging.send.count",
        unit: "{message}",
        description: "Number of point-to-point send attempts, tagged with the outcome.");

    /// <summary>
    /// Counts <c>ConsumeAsync</c> invocations of the consumer bases, tagged with the message type and the <c>outcome</c>.
    /// A batch adds its size.
    /// </summary>
    public static readonly Counter<long> ConsumeCounter = Meter.CreateCounter<long>(
        name: "sharedkernel.messaging.consume.count",
        unit: "{message}",
        description: "Number of messages consumed by the consumer bases, tagged with the outcome.");

    /// <summary>
    /// Records the duration, in seconds, of one <c>ConsumeAsync</c> invocation, tagged with the message type and the
    /// <c>outcome</c>.
    /// </summary>
    public static readonly Histogram<double> ConsumeDurationHistogram = Meter.CreateHistogram<double>(
        name: "sharedkernel.messaging.consume.duration",
        unit: "s",
        description: "Duration of a single ConsumeAsync invocation, tagged with the outcome.");

    /// <summary>
    /// Counts retry-filter re-deliveries observed through <c>ConsumeContext.GetRetryAttempt()</c>, tagged with the message
    /// type. MassTransit's <c>IRetryObserver</c> is unreachable from the bus configurators, so this is a per-invocation
    /// observation: 0 on the original delivery, 1, 2, ... on each re-delivery.
    /// </summary>
    public static readonly Counter<long> RetryCounter = Meter.CreateCounter<long>(
        name: "sharedkernel.messaging.retry.count",
        unit: "{retry}",
        description: "Number of retry-filter re-deliveries observed via ConsumeContext.GetRetryAttempt() > 0.");

    /// <summary>
    /// Counts <c>Fault&lt;TMessage&gt;</c> messages observed by <see cref="Consumers.FaultConsumerAdapter{TMessage, TFaultConsumer}"/>,
    /// tagged with the message type, whether or not the registered fault consumer then succeeds.
    /// </summary>
    public static readonly Counter<long> FaultCounter = Meter.CreateCounter<long>(
        name: "sharedkernel.messaging.fault.count",
        unit: "{fault}",
        description: "Number of Fault<TMessage> messages observed by FaultConsumerAdapter.");

    /// <summary>The metric tags of an attempt: the message (or event) type and the outcome.</summary>
    /// <param name="typeTagKey">The tag key holding the type name.</param>
    /// <param name="typeName">The type name.</param>
    /// <param name="outcome">One of the <see cref="WellKnownOutcomes"/> values.</param>
    /// <returns>The tag list.</returns>
    public static TagList OutcomeTags(string typeTagKey, string typeName, string outcome) => new()
    {
        { typeTagKey, typeName },
        { WellKnownTagKeys.Outcome, outcome },
    };
}
