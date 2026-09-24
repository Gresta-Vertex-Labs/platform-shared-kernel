using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Copies the platform's own <see cref="Activity"/> baggage — the correlation id and the tenant id — from
/// <see cref="Activity.Current"/> onto <see cref="LogRecord.Attributes"/> when a log record is finalized, so every
/// exported record carries them without any call site passing them as message-template placeholders.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only platform keys (P-562 X2).</b> The processor copies <c>correlation.id</c> and <c>TenantId</c>, the two keys
/// platform middleware writes (<c>14.Presentation</c>'s correlation middleware and <c>SharedKernel.MultiTenancy</c>'s
/// <c>TenantResolutionMiddleware</c>), under their own names. Every other baggage item is ignored, whoever set it:
/// baggage also arrives from outside — the W3C <c>baggage</c> request header, and message headers, which
/// MassTransit copies onto the consuming activity — so copying every item let a caller put any property, a forged
/// <c>SubjectId</c> for one, on every log record of its request. A value to log that is not one of these two belongs
/// in the log statement itself.
/// </para>
/// <para>
/// <b>Log-forging guard.</b> A value containing a control character (C0, DEL or C1, which includes CR, LF and NEL)
/// or a Unicode line or paragraph separator is not copied, because a log viewer would render it as a line break.
/// Platform values are GUIDs and validated correlation ids, which never contain one.
/// </para>
/// <para>
/// <b>What it cannot tell.</b> A baggage item carries no record of who set it. When a caller's <c>TenantId</c> or
/// <c>correlation.id</c> item reaches the activity and nothing overwrites it, it is copied like the platform's own.
/// That is why the HTTP edge drops inbound baggage (<c>14.Presentation</c>'s <c>TrustInboundBaggage</c>, off by
/// default) and why both middlewares <em>replace</em> their key with <see cref="Activity.SetBaggage"/>.
/// </para>
/// <para>
/// An attribute already present on <see cref="LogRecord.Attributes"/> at a given key is never overwritten: an
/// explicit call-site value always wins. When <see cref="Activity.Current"/> is <see langword="null"/>, or carries
/// neither key, the processor does nothing.
/// </para>
/// <para>
/// <c>SharedKernel.ServiceDefaults</c> references neither writer: the key names are retyped here and pinned to
/// <c>SharedKernel.Primitives.Propagation.WellKnownBaggageKeys</c> by a test.
/// </para>
/// </remarks>
public sealed class BaggageLogRecordProcessor : BaseProcessor<LogRecord>
{
    // U+2028 and U+2029 are not control characters, but many log viewers render them as line breaks.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Adds the platform baggage items found on <see cref="Activity.Current"/> or its parents to
    /// <paramref name="data"/>'s <see cref="LogRecord.Attributes"/>, unless a value contains a control character or
    /// an attribute with the same key is already present.
    /// </summary>
    /// <param name="data">The log record being finalized.</param>
    public override void OnEnd(LogRecord data)
    {
        if (Activity.Current is not { } activity)
        {
            return;
        }

        var existingAttributes = data.Attributes;
        List<KeyValuePair<string, object?>>? mergedAttributes = null;

        foreach (var key in PlatformBaggageKeys.All)
        {
            if (activity.GetBaggageItem(key) is not { } value
                || !IsLoggable(value)
                || Contains(existingAttributes, key))
            {
                continue;
            }

            if (mergedAttributes is null)
            {
                mergedAttributes = new List<KeyValuePair<string, object?>>(
                    (existingAttributes?.Count ?? 0) + PlatformBaggageKeys.All.Count);

                if (existingAttributes is not null)
                {
                    mergedAttributes.AddRange(existingAttributes);
                }
            }

            mergedAttributes.Add(new KeyValuePair<string, object?>(key, value));
        }

        if (mergedAttributes is not null)
        {
            data.Attributes = mergedAttributes;
        }
    }

    /// <summary>
    /// Returns <see langword="false"/> when <paramref name="value"/> contains a character a log viewer renders as a
    /// line break or does not print: a control character or a Unicode line or paragraph separator.
    /// </summary>
    internal static bool IsLoggable(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character) || character is LineSeparator or ParagraphSeparator)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(IReadOnlyList<KeyValuePair<string, object?>>? attributes, string key)
    {
        if (attributes is null)
        {
            return false;
        }

        for (var index = 0; index < attributes.Count; index++)
        {
            if (string.Equals(attributes[index].Key, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
