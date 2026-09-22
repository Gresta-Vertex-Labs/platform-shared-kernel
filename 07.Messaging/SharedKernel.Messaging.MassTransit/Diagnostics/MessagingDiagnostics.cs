using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SharedKernel.Messaging.MassTransit.Diagnostics;

/// <summary>
/// Holds the platform-standard <see cref="ActivitySource"/> for
/// <c>SharedKernel.Messaging.MassTransit</c>. Consumed internally by
/// <see cref="Consumers.ConsumerBase{TMessage}"/> and
/// <see cref="EventPublisher.MassTransitEventPublisher"/> to emit consume and publish
/// spans, and externally by <c>13.ServiceDefaults.WithMessagingTelemetry()</c>, which
/// registers the <see cref="ActivitySource.Name"/> with the host's
/// <c>TracerProvider</c>/<c>MeterProvider</c> via <c>.AddSource(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not a "static mutable state" violation.</strong> A single static
/// <see cref="ActivitySource"/> instance per assembly is the platform-standard .NET
/// diagnostics pattern — the same shape as a static logger category name or a
/// process-lifetime <c>Meter</c> instance. <see cref="ActivitySource"/> carries no
/// mutable business state; the BCL diagnostics API
/// (<c>System.Diagnostics.ActivitySource</c>) is explicitly designed around
/// process-lifetime static instrument instances that are registered once and shared
/// by every call site in the assembly.
/// </para>
/// <para>
/// <see cref="ActivitySource"/> and, as of P-348/WO-054, <see cref="System.Diagnostics.Metrics.Meter"/> are
/// the only sanctioned static fields in this domain (the <c>Meter</c>'s instrument fields
/// below fall under the same exception — they are process-lifetime instrument handles,
/// not mutable business state). Do not add additional ad-hoc static fields under cover of
/// this exception.
/// </para>
/// <para>
/// Ownership boundary: this source is owned and constructed here, in
/// <c>07.Messaging</c>. <c>13.ServiceDefaults</c> must never construct an
/// <see cref="ActivitySource"/> or custom <c>Meter</c> on behalf of this domain — it
/// only registers the already-existing source name with the host's tracer/meter
/// providers.
/// </para>
/// </remarks>
internal static class MessagingDiagnostics
{
    /// <summary>
    /// The name of the <see cref="ActivitySource"/>. Pass this value to
    /// <c>TracerProviderBuilder.AddSource(...)</c> when wiring the host's
    /// <c>TracerProvider</c>.
    /// </summary>
    public const string SourceName = "SharedKernel.Messaging";

    /// <summary>
    /// The version stamped on every <see cref="Activity"/> created by
    /// <see cref="ActivitySource"/>.
    /// </summary>
    public const string SourceVersion = "1.0.0";

    /// <summary>
    /// The single, process-lifetime <see cref="ActivitySource"/> instance for the
    /// whole <c>SharedKernel.Messaging.MassTransit</c> package. Used by
    /// <see cref="Consumers.ConsumerBase{TMessage}.Consume"/> to start the
    /// <c>"Consumer.Consume"</c> activity, by
    /// <see cref="EventPublisher.MassTransitEventPublisher"/> to start the
    /// <c>"EventPublisher.Publish"</c> activity, and by <c>MassTransitMessageBus.PublishAsync</c>
    /// and <c>.SendAsync</c> for <c>"MessageBus.Publish"</c> and <c>"MessageBus.Send"</c>.
    /// Every dispatch verb is covered: P-348 gave <c>SendAsync</c> its activity, and P-560 gave
    /// <c>PublishAsync</c> one — the platform's most-used verb was the last with none.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, SourceVersion);

    /// <summary>
    /// The single, process-lifetime <see cref="System.Diagnostics.Metrics.Meter"/> instance for the whole
    /// <c>SharedKernel.Messaging.MassTransit</c> package (P-348/WO-054). Hosts the five
    /// instruments below. Consumed by <c>13.ServiceDefaults.WithMessagingTelemetry()</c>,
    /// which registers <see cref="SourceName"/> with the host's <c>MeterProvider</c> via
    /// <c>.AddMeter(...)</c> — mirroring the <see cref="ActivitySource"/> registration split.
    /// </summary>
    public static readonly Meter Meter = new(SourceName, SourceVersion);

    /// <summary>
    /// Counts messages/integration events successfully published, tagged
    /// <c>messaging.event_type</c> (from <see cref="EventPublisher.MassTransitEventPublisher"/>)
    /// or <c>messaging.message_type</c> (from <c>MassTransitMessageBus.PublishAsync</c>).
    /// Incremented only after the underlying MassTransit publish call completes without
    /// throwing — a publish that faults is never counted as published.
    /// </summary>
    public static readonly Counter<long> PublishCounter = Meter.CreateCounter<long>(
        name: "messaging.publish.count",
        unit: "{message}",
        description: "Number of messages/integration events successfully published.");

    /// <summary>
    /// Counts successful <see cref="Consumers.ConsumerBase{TMessage}.ConsumeAsync"/>
    /// completions, tagged <c>messaging.message_type</c>. Incremented only when
    /// <c>ConsumeAsync</c> returns without throwing — a faulted consume attempt is
    /// never counted here (see <see cref="RetryCounter"/> and <see cref="FaultCounter"/>).
    /// </summary>
    /// <summary>
    /// Number of commands successfully sent point-to-point via <c>IMessageBus.SendAsync</c>.
    /// </summary>
    /// <remarks>
    /// Added by P-560. Until then only publishes were counted, so a service whose traffic was
    /// predominantly <c>SendAsync</c> appeared idle on the messaging dashboards.
    /// </remarks>
    public static readonly Counter<long> SendCounter = Meter.CreateCounter<long>(
        name: "messaging.send.count",
        unit: "{message}",
        description: "Number of commands successfully sent to a point-to-point endpoint.");

    public static readonly Counter<long> ConsumeCounter = Meter.CreateCounter<long>(
        name: "messaging.consume.count",
        unit: "{message}",
        description: "Number of messages successfully consumed by a ConsumerBase<TMessage> subclass.");

    /// <summary>
    /// Records the wall-clock duration, in milliseconds, of a single
    /// <see cref="Consumers.ConsumerBase{TMessage}.ConsumeAsync"/> invocation, tagged
    /// <c>messaging.message_type</c>. Recorded unconditionally — on both success and
    /// failure — so the histogram reflects true end-to-end consume latency, including
    /// attempts that ultimately faulted.
    /// </summary>
    public static readonly Histogram<double> ConsumeDurationHistogram = Meter.CreateHistogram<double>(
        name: "messaging.consume.duration",
        unit: "ms",
        description: "Duration, in milliseconds, of a single ConsumeAsync invocation (success or failure).");

    /// <summary>
    /// Counts retry-filter re-deliveries observed by
    /// <see cref="Consumers.ConsumerBase{TMessage}.Consume"/>, tagged
    /// <c>messaging.message_type</c>. MassTransit ships <c>IRetryObserver</c> /
    /// <c>IRetryObserverConnector</c> in its public API, but no reachable configurator
    /// surface (<c>IBusFactoryConfigurator</c>, <c>IReceiveEndpointConfigurator</c>,
    /// <c>IBus</c>, <c>IBusControl</c>) implements <c>IRetryObserverConnector</c> in the
    /// shipped build — confirmed by reflection over the shipped assembly, not
    /// documented in its XML doc comments — so <c>ConnectRetryObserver</c> is unreachable
    /// from <c>MessagingBusBuilder</c>'s configuration-time API. The verified, working
    /// alternative is <c>ConsumeContext.GetRetryAttempt()</c>
    /// (<c>MassTransit.RetryContextExtensions</c>): confirmed via a live
    /// <c>TestHarness</c> run to return <c>0</c> on the original delivery and <c>1, 2, ...</c>
    /// on each subsequent retry-filter re-delivery, and to return <c>0</c> safely (never
    /// throw) when no retry middleware is configured at all. This is a per-invocation
    /// context inspection, not a subscribed observer callback — it is the best available
    /// observation point in 9.1.2, not the originally-anticipated <c>IRetryObserver</c> hook.
    /// </summary>
    public static readonly Counter<long> RetryCounter = Meter.CreateCounter<long>(
        name: "messaging.retry.count",
        unit: "{retry}",
        description: "Number of retry-filter re-deliveries observed via ConsumeContext.GetRetryAttempt() > 0.");

    /// <summary>
    /// Counts <c>Fault&lt;TMessage&gt;</c> messages observed by
    /// <see cref="Consumers.FaultConsumerAdapter{TMessage, TFaultConsumer}"/>, tagged
    /// <c>messaging.message_type</c>. Incremented unconditionally for every delivered
    /// fault, regardless of whether the registered <c>IFaultConsumer&lt;TMessage&gt;</c>
    /// itself then succeeds or throws while handling it.
    /// </summary>
    public static readonly Counter<long> FaultCounter = Meter.CreateCounter<long>(
        name: "messaging.fault.count",
        unit: "{fault}",
        description: "Number of Fault<TMessage> messages observed by FaultConsumerAdapter.");
}
