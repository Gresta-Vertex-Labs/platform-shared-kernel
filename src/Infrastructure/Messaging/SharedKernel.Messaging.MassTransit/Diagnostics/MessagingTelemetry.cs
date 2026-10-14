namespace SharedKernel.Messaging.MassTransit.Diagnostics;

/// <summary>
/// The names under which <c>SharedKernel.Messaging.MassTransit</c> emits traces and metrics.
/// </summary>
/// <remarks>
/// <para>
/// A host subscribes to <see cref="SourceName"/> and <see cref="MeterName"/> (or to the <c>SharedKernel.*</c> wildcard
/// that <c>AddServiceDefaults()</c> registers). Spans are named <c>messaging consume</c>, <c>messaging consume_batch</c>
/// (kind <c>Consumer</c>), <c>messaging publish</c>, <c>messaging publish_event</c> and <c>messaging send</c> (kind
/// <c>Producer</c>). Instruments are <c>sharedkernel.messaging.publish.count</c>, <c>.send.count</c>,
/// <c>.consume.count</c> (<c>{message}</c>), <c>.retry.count</c> (<c>{retry}</c>), <c>.fault.count</c> (<c>{fault}</c>)
/// and <c>.consume.duration</c> (<c>s</c>); the publish, send and consume instruments carry the <c>outcome</c> tag.
/// </para>
/// <para>
/// The <see cref="System.Diagnostics.Metrics.Meter"/> is a process-wide static rather than one created through
/// <c>IMeterFactory</c>: the instruments are recorded from <c>ConsumerBase&lt;T&gt;</c> and <c>BatchConsumerBase&lt;T&gt;</c>,
/// whose public constructors take only an <c>ILogger</c>.
/// </para>
/// </remarks>
public static class MessagingTelemetry
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> (<c>"SharedKernel.Messaging"</c>).</summary>
    public const string SourceName = "SharedKernel.Messaging";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> (<c>"SharedKernel.Messaging"</c>).</summary>
    public const string MeterName = "SharedKernel.Messaging";
}
