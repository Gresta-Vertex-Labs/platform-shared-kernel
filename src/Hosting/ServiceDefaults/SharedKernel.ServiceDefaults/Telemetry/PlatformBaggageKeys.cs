using SharedKernel.Primitives.Propagation;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// The <see cref="System.Diagnostics.Activity"/> baggage keys the platform writes itself, and the only ones
/// <see cref="BaggageLogRecordProcessor"/> copies onto log records.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a fixed list.</b> Baggage can arrive from outside: the W3C <c>baggage</c> request header and
/// message headers are copied onto the current <see cref="System.Diagnostics.Activity"/>. Copying every item made
/// whatever a caller sent — <c>TenantId=&lt;another tenant&gt;</c>, <c>SubjectId=admin</c> — a property of every log
/// record the request produced, where dashboards and alerts trust it. These two keys are written by platform
/// middleware, after it has validated or resolved the value.
/// </para>
/// <para>
/// A key joins this list only if platform middleware writes and replaces it. A key added to
/// <see cref="WellKnownBaggageKeys"/> is therefore not logged until it is added here; a test pins the list to exactly
/// these two.
/// </para>
/// </remarks>
internal static class PlatformBaggageKeys
{
    /// <summary>
    /// The correlation id, set by <c>RequestContextMiddleware</c> in <c>SharedKernel.ServiceDefaults.Security</c> to a
    /// validated caller value, otherwise a newly created one.
    /// </summary>
    public const string CorrelationId = WellKnownBaggageKeys.CorrelationId;

    /// <summary>
    /// The resolved tenant id, set by <c>SharedKernel.MultiTenancy</c>'s tenant resolution middleware and removed
    /// when no tenant resolved.
    /// </summary>
    public const string TenantId = WellKnownBaggageKeys.TenantId;

    /// <summary>Every key, in the order the processor copies them.</summary>
    public static IReadOnlyList<string> All { get; } = [CorrelationId, TenantId];
}
