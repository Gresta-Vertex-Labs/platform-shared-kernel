namespace SharedKernel.Primitives.Propagation;

/// <summary>
/// The <see cref="System.Diagnostics.Activity"/> baggage key names used to propagate ambient
/// identity across process boundaries.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule:</b> reference the constant on both sides — the middleware writing the baggage and
/// anything reading it. A writer and a reader are usually in different packages that cannot
/// reference each other, so a mismatch is not a compile error; it is ambient context that silently
/// stops arriving.
/// </para>
/// <para>
/// That happened here: <c>14.Presentation</c>'s correlation middleware wrote baggage under
/// <c>"correlation.id"</c> while <c>13.ServiceDefaults</c>' reader independently hardcoded
/// <c>"CorrelationId"</c> for the same concept. Both sides now reference
/// <see cref="CorrelationId"/>.
/// </para>
/// <para>
/// <b>Baggage is not a span tag.</b> Baggage propagates across process boundaries as part of the
/// trace context and is comparatively expensive — it rides on every outbound call. A span-local
/// attribute belongs in <see cref="WellKnownTagKeys"/> instead. Keep this registry small: each
/// entry is a value every service carries on every hop.
/// </para>
/// <para>
/// <b>These keys surface as log property names.</b> <c>13.ServiceDefaults</c>'
/// <c>BaggageLogRecordProcessor</c> copies baggage onto log records generically, by enumeration
/// rather than by known key, so whatever string is used here becomes the property name operators
/// query on. That is why changing a value is an operational breaking change and not a tidy-up, and
/// it is the reason <see cref="TenantId"/> looks inconsistent with its tag-key counterpart.
/// </para>
/// <para>
/// <b>Why <c>01.Core</c> and not <c>04.Contracts</c>:</b> see <see cref="WellKnownHeaders"/>, whose
/// rationale applies identically.
/// </para>
/// </remarks>
public static class WellKnownBaggageKeys
{
    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> baggage key carrying the correlation id used to
    /// tie a single logical operation together across the distributed-trace propagation pipeline
    /// (<c>"correlation.id"</c>).
    /// </summary>
    /// <remarks>
    /// Written by the inbound adapters (<c>13.ServiceDefaults.Security</c>'s request-context middleware, the gRPC server
    /// interceptor) and read by
    /// <c>13.ServiceDefaults.BaggageLogRecordProcessor</c> — this constant is the single shared
    /// source of truth reconciling both sides of that contract.
    /// </remarks>
    public const string CorrelationId = "correlation.id";

    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> baggage key carrying the resolved tenant
    /// identifier in its <c>TenantId.ToString()</c> string form
    /// (<c>"TenantId"</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written by <c>13.ServiceDefaults.MultiTenancy.TenantResolutionMiddleware</c> and surfaced
    /// onto every subsequent log record by <c>13.ServiceDefaults.BaggageLogRecordProcessor</c>,
    /// which copies baggage generically rather than by known key.
    /// </para>
    /// <para>
    /// <b>Why this one is PascalCase while <see cref="CorrelationId"/> is dotted-lowercase.</b>
    /// It is not an oversight, and it must not be "corrected" to <c>"tenant.id"</c> for symmetry
    /// with <see cref="WellKnownTagKeys.TenantId"/>. Because the baggage processor copies baggage
    /// generically, the key string becomes the emitted log property name verbatim — so renaming it
    /// silently renames a field that deployed dashboards, saved searches, and alert rules filter
    /// on. That is an operational breaking change for consumers, decided by whoever owns their log
    /// pipeline, not something to fold into a constants registry. This constant therefore records
    /// the value already on the wire.
    /// </para>
    /// </remarks>
    public const string TenantId = "TenantId";
}
