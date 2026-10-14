namespace SharedKernel.Primitives.Propagation;

/// <summary>
/// The OpenTelemetry span-attribute key names used with
/// <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/> across more than one domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule:</b> reference the constant rather than retyping the literal. These keys are what
/// dashboards and trace queries filter on, so two domains tagging the same concept under different
/// spellings produces spans that cannot be correlated — and nothing in a build reports it.
/// <c>00.Governance</c>'s <c>SK0022</c> analyzer flags a raw literal at a
/// <c>SetTag</c> call site.
/// </para>
/// <para>
/// <b>Tags are not baggage, and this is the distinction to get right.</b> A tag is a span-local
/// attribute, visible to the current span and its exporter only. Baggage
/// (<see cref="WellKnownBaggageKeys"/>) propagates across process boundaries as part of the trace
/// context. Use the registry matching the call you are making —
/// <c>SetTag</c> here, <c>SetBaggage</c>/<c>AddBaggage</c> there — and do not infer that they are
/// interchangeable from the fact that <see cref="CorrelationId"/> happens to hold the same literal
/// in both.
/// </para>
/// <para>
/// <b>Only cross-domain keys belong here.</b> A key used by a single package stays in that
/// package — <c>06.Persistence.EfCore</c>'s <c>PersistenceTagKeys</c> is the shipped example, and
/// it references <see cref="ErrorType"/> from here for the one key it shares while keeping its own
/// local. A key joins this registry only when two or more domains tag it; promoting a
/// single-package key adds a cross-domain contract nobody needed.
/// </para>
/// <para>
/// Values follow OpenTelemetry semantic-convention style: dotted lowercase.
/// </para>
/// <para>
/// <b>Why <c>01.Core</c> and not <c>04.Contracts</c>:</b> see <see cref="WellKnownHeaders"/>, whose
/// rationale applies identically.
/// </para>
/// </remarks>
public static class WellKnownTagKeys
{
    /// <summary>
    /// Span attribute carrying the tenant id, for per-tenant trace attribution
    /// (<c>"tenant.id"</c>).
    /// </summary>
    /// <remarks>
    /// Note this is NOT the tenant BAGGAGE key, which is <c>"TenantId"</c> — see
    /// <see cref="WellKnownBaggageKeys.TenantId"/> for why the two deliberately differ.
    /// </remarks>
    public const string TenantId = "tenant.id";

    /// <summary>
    /// Span attribute carrying the correlation id that ties one logical operation together
    /// (<c>"correlation.id"</c>).
    /// </summary>
    /// <remarks>
    /// Holds the same literal as <see cref="WellKnownBaggageKeys.CorrelationId"/>, but the two are
    /// separate constants for separate call-site shapes. Do not substitute one for the other on the
    /// strength of the match.
    /// </remarks>
    public const string CorrelationId = "correlation.id";

    /// <summary>
    /// Span attribute carrying a coarse, bounded error classification (<c>"error.type"</c>).
    /// </summary>
    /// <remarks>
    /// Written by <c>Activity.RecordFailure</c>/<c>RecordException</c> (<c>SharedKernel.Primitives.Telemetry</c>):
    /// the <see cref="Errors.ErrorType"/> member name (for example <c>NotFound</c>) for a failed <c>Result</c>, the
    /// exception's full type name for an exception. Both value sets are bounded (an enum or a CLR type), so the tag is
    /// safe as a metric dimension. A dashboard that keyed this tag on exception names for <c>Result</c> failures now
    /// sees <see cref="Errors.ErrorType"/> names.
    /// </remarks>
    public const string ErrorType = "error.type";

    /// <summary>
    /// Span attribute carrying a domain error code: an <see cref="Errors.Error.Code"/> value (<c>"error.code"</c>).
    /// </summary>
    /// <remarks>
    /// Written by the same recorder, from the failed <c>Result</c>'s error or from an exception that implements
    /// <see cref="Errors.IHasError"/>.
    /// </remarks>
    public const string ErrorCode = "error.code";

    /// <summary>
    /// Tag (span attribute and metric dimension) carrying how an operation ended, one of the
    /// <see cref="WellKnownOutcomes"/> values (<c>"outcome"</c>).
    /// </summary>
    public const string Outcome = "outcome";
}
