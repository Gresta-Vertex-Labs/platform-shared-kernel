namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// The <see cref="System.Diagnostics.Activity"/> baggage keys the platform writes itself, and the only ones
/// <see cref="BaggageLogRecordProcessor"/> copies onto log records.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a fixed list (P-562 X2).</b> Baggage can arrive from outside: the W3C <c>baggage</c> request header and
/// message headers are copied onto the current <see cref="System.Diagnostics.Activity"/>. Copying every item made
/// whatever a caller sent — <c>TenantId=&lt;another tenant&gt;</c>, <c>SubjectId=admin</c> — a property of every log
/// record the request produced, where dashboards and alerts trust it. These two keys are written by platform
/// middleware, after it has validated or resolved the value.
/// </para>
/// <para>
/// <b>The values are <c>SharedKernel.Primitives.Propagation.WellKnownBaggageKeys</c>'.</b> They are retyped here
/// because this package references no other SharedKernel package (WO-084), and a test pins each one to the registry.
/// A key added to the registry is not logged until it is added here.
/// </para>
/// </remarks>
internal static class PlatformBaggageKeys
{
    /// <summary>
    /// The correlation id (<c>"correlation.id"</c>), set by <c>14.Presentation</c>'s correlation middleware to a
    /// validated caller value or a new id.
    /// </summary>
    public const string CorrelationId = "correlation.id";

    /// <summary>
    /// The resolved tenant id (<c>"TenantId"</c>), set by <c>SharedKernel.MultiTenancy</c>'s tenant resolution
    /// middleware, or <see cref="Guid.Empty"/> when no tenant resolved.
    /// </summary>
    public const string TenantId = "TenantId";

    /// <summary>Every key, in the order the processor copies them.</summary>
    public static IReadOnlyList<string> All { get; } = [CorrelationId, TenantId];
}
