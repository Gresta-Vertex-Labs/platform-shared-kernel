using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Integration.Notifications.Abstractions.Tracing;

/// <summary>
/// The instruments of the notification family on the <c>SharedKernel.Integration</c> meter, created through
/// <see cref="IMeterFactory"/>. Registered as a singleton by <c>AddSharedKernelNotifications</c>; the provider
/// senders take it as a constructor parameter and record once per terminal outcome.
/// </summary>
/// <remarks>
/// The only tags are the channel and the outcome (<see cref="WellKnownOutcomes"/>): a delivery id or a recipient is
/// unbounded and never a tag. The webhook family creates its own instruments on the same meter name; the factory
/// returns one cached meter for both.
/// </remarks>
public sealed class NotificationMetrics
{
    /// <summary>The meter name, identical to the activity source name of the whole domain.</summary>
    public const string MeterName = "SharedKernel.Integration";

    /// <summary>The counter of terminal send outcomes (unit <c>{message}</c>).</summary>
    public const string SendsInstrumentName = "sharedkernel.integration.notification.sends";

    /// <summary>The histogram of send durations (unit <c>s</c>).</summary>
    public const string SendDurationInstrumentName = "sharedkernel.integration.notification.send.duration";

    private readonly Counter<long> _sends;
    private readonly Histogram<double> _duration;

    /// <summary>Initializes a new instance of <see cref="NotificationMetrics"/>.</summary>
    /// <param name="meterFactory">Creates the meter.</param>
    public NotificationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(MeterName) { Version = TelemetryVersion.Of(typeof(NotificationMetrics).Assembly) });
        _sends = meter.CreateCounter<long>(
            SendsInstrumentName,
            unit: "{message}",
            description: "Number of notification sends by terminal outcome.");
        _duration = meter.CreateHistogram<double>(
            SendDurationInstrumentName,
            unit: "s",
            description: "Duration, in seconds, of a notification send including its retries.");
    }

    /// <summary>Records one terminal send outcome.</summary>
    /// <param name="channel">The channel the notification went over.</param>
    /// <param name="outcome">One of <see cref="WellKnownOutcomes"/>.</param>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value taken when the send began.</param>
    public void RecordSend(NotificationChannel channel, string outcome, long startTimestamp)
    {
        var tags = new TagList
        {
            { NotificationTagKeys.Channel, channel.ToString() },
            { WellKnownTagKeys.Outcome, outcome },
        };

        _sends.Add(1, tags);
        _duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }
}
