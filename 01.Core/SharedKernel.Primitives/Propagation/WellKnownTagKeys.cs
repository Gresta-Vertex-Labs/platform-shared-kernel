namespace SharedKernel.Primitives.Propagation;

/// <summary>
/// Compile-time constant registry of <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/>
/// attribute-key names expected to appear identically across every domain emitting OpenTelemetry
/// spans — the single authoritative source for these literals platform-wide.
/// </summary>
/// <remarks>
/// <para>
/// This registry pre-emptively covers <c>Activity.SetTag(...)</c> call sites platform-wide. Unlike
/// <see cref="WellKnownHeaders"/> and <see cref="WellKnownBaggageKeys"/> — both of which were introduced
/// only after a confirmed live mismatch had already occurred between independently-redeclared literals —
/// no domain has adopted <see cref="WellKnownTagKeys"/> yet as of its introduction. `00.Governance`'s
/// SK0022 analyzer already recognizes <c>Activity.SetTag(...)</c> as a regulated magic-string call-site
/// shape; this registry exists so that analyzer has a shared constant to point at before the first
/// domain ships a conflicting tag-key literal, rather than reactively after the fact.
/// </para>
/// <para>
/// <c>Activity.SetTag(...)</c> is a distinct call-site shape from <c>Activity.SetBaggage(...)</c> /
/// <c>Activity.AddBaggage(...)</c> — tags are span-local attributes visible only to the current span
/// and its exporters, while baggage propagates across process boundaries as part of the distributed
/// trace context. This distinction holds even where a literal value coincides between the two registries
/// (compare <see cref="CorrelationId"/> here, <c>"correlation.id"</c>, against
/// <see cref="WellKnownBaggageKeys.CorrelationId"/>, the identical string) — the two registries are
/// never interchangeable and a domain must reference the one matching its actual call-site shape.
/// </para>
/// <para>
/// Dotted-lowercase values (e.g. <c>"tenant.id"</c>, <c>"error.type"</c>) are chosen to match
/// OpenTelemetry semantic-convention naming style for span attributes.
/// </para>
/// <para>
/// <c>04.Contracts</c> was considered and rejected as the home for this registry — see
/// <see cref="WellKnownHeaders"/> for the full rationale, which applies identically here.
/// </para>
/// <para>
/// Retrofitting an existing domain's <c>Activity.SetTag(...)</c> call sites to reference these
/// constants is that consuming domain's own follow-up responsibility, exactly as documented for
/// <see cref="WellKnownHeaders"/> and <see cref="WellKnownBaggageKeys"/> — this registry changes no
/// existing call site by itself.
/// </para>
/// </remarks>
public static class WellKnownTagKeys
{
    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> tag key carrying the tenant id for multi-tenant
    /// span attribution (<c>"tenant.id"</c>).
    /// </summary>
    public const string TenantId = "tenant.id";

    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> tag key carrying the correlation id used to tie
    /// a single logical operation together within span attributes (<c>"correlation.id"</c>).
    /// </summary>
    /// <remarks>
    /// This literal value coincides with <see cref="WellKnownBaggageKeys.CorrelationId"/>, but the two
    /// constants are declared independently and serve distinct call-site shapes
    /// (<c>Activity.SetTag(...)</c> here vs. <c>Activity.SetBaggage(...)</c>/<c>AddBaggage(...)</c>
    /// there) — never assume interchangeability from the coincidental literal match alone.
    /// </remarks>
    public const string CorrelationId = "correlation.id";

    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> tag key carrying a coarse error classification
    /// for span-level error attribution (<c>"error.type"</c>).
    /// </summary>
    public const string ErrorType = "error.type";

    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> tag key carrying a domain-specific error code for
    /// span-level error attribution (<c>"error.code"</c>).
    /// </summary>
    public const string ErrorCode = "error.code";
}
