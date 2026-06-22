using System.Diagnostics;

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
/// <see cref="ActivitySource"/> is the only sanctioned static field in this domain.
/// Do not add additional ad-hoc static fields under cover of this exception.
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
    /// <c>"Consumer.Consume"</c> activity and by
    /// <see cref="EventPublisher.MassTransitEventPublisher"/> to start the
    /// <c>"EventPublisher.Publish"</c> activity.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, SourceVersion);
}
