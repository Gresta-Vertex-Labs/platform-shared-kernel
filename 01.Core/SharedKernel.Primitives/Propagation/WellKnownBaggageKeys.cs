namespace SharedKernel.Primitives.Propagation;

/// <summary>
/// Compile-time constant registry of <see cref="System.Diagnostics.Activity"/> baggage /
/// distributed-trace propagation key names — the single authoritative source for these literals
/// platform-wide.
/// </summary>
/// <remarks>
/// <para>
/// Reconciles a confirmed live mismatch that existed before this registry:
/// <c>14.Presentation.CorrelationIdMiddleware</c> writes <see cref="System.Diagnostics.Activity"/>
/// baggage under the key <c>"correlation.id"</c> (the writer side of the contract), while
/// <c>13.ServiceDefaults.BaggageLogRecordProcessor</c>'s test suite independently hardcoded the
/// literal <c>"CorrelationId"</c> for the same concept (the reader side). Both sides must now
/// reference <see cref="CorrelationId"/> so the two ends of the contract cannot silently drift apart
/// again.
/// </para>
/// <para>
/// <c>04.Contracts</c> was considered and rejected as the home for this registry — see
/// <see cref="WellKnownHeaders"/> for the full rationale, which applies identically here.
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
    /// Written by <c>14.Presentation.CorrelationIdMiddleware</c> and read by
    /// <c>13.ServiceDefaults.BaggageLogRecordProcessor</c> — this constant is the single shared
    /// source of truth reconciling both sides of that contract.
    /// </remarks>
    public const string CorrelationId = "correlation.id";

    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> baggage key carrying the resolved tenant
    /// identifier (or the <see cref="System.Guid.Empty"/> no-tenant sentinel) in its string form
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
    /// <para>
    /// <c>SharedKernel.MultiTenancy</c> still declares its own local <c>TenantBaggageKeys.TenantId</c>
    /// holding this same literal. Re-pointing it at this constant is a pure, behaviour-identical
    /// refactor precisely because the value here matches, but it belongs to that package and has
    /// not been made yet — this registry entry exists so the next writer or reader of tenant
    /// baggage has a single authoritative place to find the value.
    /// </para>
    /// </remarks>
    public const string TenantId = "TenantId";
}
