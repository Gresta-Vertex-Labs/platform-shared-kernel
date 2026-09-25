using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Copies every <see cref="Activity.Baggage"/> entry on <see cref="Activity.Current"/> onto
/// <see cref="LogRecord.Attributes"/> at the moment a log record is finalized, making ambient
/// distributed-trace baggage — such as CorrelationId or TenantId — available on every exported
/// log record without any call site needing to pass it as an explicit message-template placeholder.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>generic</b> mechanism: it carries no hardcoded baggage key names. It works
/// uniformly for any domain that sets <see cref="Activity"/> baggage — for example,
/// <c>14.Presentation</c>'s correlation-id middleware (which owns its own <see cref="Activity"/>
/// baggage key directly against the BCL, per WO-031) and <c>SharedKernel.MultiTenancy</c>'s
/// <c>TenantResolutionMiddleware</c> (which sets <c>WellKnownBaggageKeys.TenantId</c>) — without
/// <c>SharedKernel.ServiceDefaults</c> ever needing a <c>ProjectReference</c> to either domain or
/// knowing either concept by name.
/// </para>
/// <para>
/// An attribute already present on <see cref="LogRecord.Attributes"/> at a given key is never
/// overwritten by an ambient baggage value at the same key — an explicit call-site value always
/// wins. When <see cref="Activity.Current"/> is <see langword="null"/>, or carries no baggage,
/// this processor is a no-op: it does not throw and adds no attributes.
/// </para>
/// <para>
/// Scoped to whatever sets <see cref="Activity"/> baggage during the lifetime of the current
/// <see cref="Activity"/> — in practice, the HTTP-request path. A message-consumption-scope
/// equivalent (e.g. a MassTransit consumer filter propagating message headers into baggage) is not
/// implemented here; it is a future <c>07.Messaging</c>-owned follow-up outside this domain's
/// jurisdiction.
/// </para>
/// </remarks>
public sealed class BaggageLogRecordProcessor : BaseProcessor<LogRecord>
{
    /// <summary>
    /// Appends every <see cref="Activity.Baggage"/> entry from <see cref="Activity.Current"/> to
    /// <paramref name="data"/>'s <see cref="LogRecord.Attributes"/> that is not already present
    /// under the same key.
    /// </summary>
    /// <param name="data">The log record being finalized.</param>
    public override void OnEnd(LogRecord data)
    {
        if (Activity.Current is not { } activity)
        {
            return;
        }

        using var baggageEnumerator = activity.Baggage.GetEnumerator();

        if (!baggageEnumerator.MoveNext())
        {
            return;
        }

        var existingAttributes = data.Attributes;
        var mergedAttributes = new List<KeyValuePair<string, object?>>(existingAttributes?.Count ?? 0);

        if (existingAttributes is not null)
        {
            mergedAttributes.AddRange(existingAttributes);
        }

        var existingKeys = new HashSet<string>(
            mergedAttributes.Select(attribute => attribute.Key),
            StringComparer.Ordinal);

        do
        {
            var (key, value) = baggageEnumerator.Current;

            if (existingKeys.Add(key))
            {
                mergedAttributes.Add(new KeyValuePair<string, object?>(key, value));
            }
        }
        while (baggageEnumerator.MoveNext());

        data.Attributes = mergedAttributes;
    }
}
