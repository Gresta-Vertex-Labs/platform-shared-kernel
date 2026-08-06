namespace SharedKernel.Messaging.MassTransit.Options;

/// <summary>
/// Configuration options for RabbitMQ dead-letter / poison-message delivery.
/// Bound from the <c>"SharedKernel:Messaging:DeadLetter"</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Consumed by <c>MessagingBusBuilder.WithDeadLetterPolicy()</c>. RabbitMQ-only — Azure Service Bus
/// dead-lettering is entirely transport-native (driven by the queue/subscription
/// <c>MaxDeliveryCount</c> configured at the Azure resource level) and is unaffected by this options
/// class. Calling <c>WithDeadLetterPolicy()</c> while the Azure Service Bus transport is configured
/// logs an advisory <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/> at host startup
/// rather than throwing, mirroring the <c>WithVersionTranslator</c> advisory pattern.
/// </para>
/// <para>
/// A message becomes "poison" and is routed to the dead-letter/fault destination only after the
/// endpoint-level <c>ConsumerDefinitionBase&lt;TConsumer&gt;.NonRetryableExceptions</c> filter
/// classifies the exception as fatal, or the global <c>RetryOptions</c> attempt budget
/// (configured via <c>WithRetry()</c>) is exhausted. See the "Dead-letter and poison-message
/// policy" section of the domain's <c>CLAUDE.md</c> for the full retry/circuit-breaker interaction.
/// </para>
/// </remarks>
public sealed class DeadLetterOptions
{
    /// <summary>The configuration section key for <see cref="DeadLetterOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Messaging:DeadLetter";

    /// <summary>
    /// Gets or sets the suffix appended to the receive endpoint's queue name to derive the
    /// dead-letter/error destination name.
    /// Default is <c>"_error"</c>, matching MassTransit's own default RabbitMQ error-queue naming
    /// convention — the platform default changes nothing until explicitly overridden.
    /// </summary>
    /// <remarks>
    /// <strong>MassTransit 9.1.2 capability note:</strong> MassTransit's public RabbitMQ transport
    /// API (<c>IRabbitMqSendTopologyConfigurator.ConfigureErrorSettings</c> /
    /// <c>.ConfigureDeadLetterSettings</c>) lets a consuming service configure the <em>arguments</em>
    /// of the automatically-derived fault/dead-letter queue (e.g. <see cref="MessageTimeToLive"/> as
    /// <c>x-message-ttl</c>) but exposes no public hook to rename the queue itself — the <c>"_error"</c>
    /// / <c>"_skipped"</c> suffixes are a fixed internal convention in the installed MassTransit
    /// version (confirmed by reflection and IL-string inspection against
    /// <c>MassTransit.RabbitMqTransport</c> 9.1.2; there is no supported, non-reflection way to
    /// override them). A non-default value is accepted here for forward compatibility and is
    /// surfaced via an advisory <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/> at host
    /// startup explaining that only <see cref="MessageTimeToLive"/> currently has an observable
    /// effect — see the "Dead-letter and poison-message policy" section of this domain's
    /// <c>CLAUDE.md</c>.
    /// </remarks>
    public string QueueNameSuffix { get; set; } = "_error";

    /// <summary>
    /// Gets or sets the message time-to-live applied to the dead-letter/error queue as the RabbitMQ
    /// <c>x-message-ttl</c> queue argument.
    /// Default is <see langword="null"/> — no expiry, unbounded retention.
    /// </summary>
    public TimeSpan? MessageTimeToLive { get; set; }
}
