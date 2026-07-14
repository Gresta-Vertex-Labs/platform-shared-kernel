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
}
