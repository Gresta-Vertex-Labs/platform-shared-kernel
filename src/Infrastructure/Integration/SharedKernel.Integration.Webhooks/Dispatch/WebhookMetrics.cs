using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// The instruments of the webhook family on the <c>SharedKernel.Integration</c> meter, created through
/// <see cref="IMeterFactory"/>. Registered as a singleton by <c>AddSharedKernelWebhooks</c>; the dispatcher records
/// once per terminal delivery outcome (a retried delivery counts once).
/// </summary>
/// <remarks>
/// The only tags are the event type and the outcome (<see cref="WellKnownOutcomes"/>): a subscription id or a delivery
/// id is unbounded and never a tag. The notification family creates its own instruments on the same meter name; the
/// factory returns one cached meter for both.
/// </remarks>
public sealed class WebhookMetrics
{
    /// <summary>The meter name, identical to the activity source name of the whole domain.</summary>
    public const string MeterName = "SharedKernel.Integration";

    /// <summary>The counter of terminal delivery outcomes (unit <c>{delivery}</c>).</summary>
    public const string DeliveriesInstrumentName = "sharedkernel.integration.webhook.deliveries";

    /// <summary>The histogram of delivery durations (unit <c>s</c>).</summary>
    public const string DeliveryDurationInstrumentName = "sharedkernel.integration.webhook.delivery.duration";

    private readonly Counter<long> _deliveries;
    private readonly Histogram<double> _duration;

    /// <summary>Initializes a new instance of <see cref="WebhookMetrics"/>.</summary>
    /// <param name="meterFactory">Creates the meter.</param>
    public WebhookMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(MeterName) { Version = TelemetryVersion.Of(typeof(WebhookMetrics).Assembly) });
        _deliveries = meter.CreateCounter<long>(
            DeliveriesInstrumentName,
            unit: "{delivery}",
            description: "Number of webhook deliveries by terminal outcome.");
        _duration = meter.CreateHistogram<double>(
            DeliveryDurationInstrumentName,
            unit: "s",
            description: "Duration, in seconds, of a webhook delivery including its retries.");
    }

    /// <summary>Records one terminal delivery outcome.</summary>
    /// <param name="eventType">The <c>[IntegrationEvent]</c> name of the delivered event.</param>
    /// <param name="outcome">One of <see cref="WellKnownOutcomes"/>.</param>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value taken when the delivery began.</param>
    public void RecordDelivery(string eventType, string outcome, long startTimestamp)
    {
        var tags = new TagList
        {
            { WebhookTagKeys.EventType, eventType },
            { WellKnownTagKeys.Outcome, outcome },
        };

        _deliveries.Add(1, tags);
        _duration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }
}
