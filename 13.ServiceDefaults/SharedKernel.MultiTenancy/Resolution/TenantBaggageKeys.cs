namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Well-known <see cref="System.Diagnostics.Activity"/> baggage key names set by
/// <c>SharedKernel.MultiTenancy</c>.
/// </summary>
/// <remarks>
/// Mirrors the <see cref="TenantResolutionStrategyNames"/>/<c>HealthCheckNames</c> constants-class
/// pattern. <c>TenantResolutionMiddleware</c>'s <see cref="System.Diagnostics.Activity.SetBaggage"/>
/// call references this constant — zero bare string literals for the baggage key anywhere in this
/// package.
/// </remarks>
public static class TenantBaggageKeys
{
    /// <summary>
    /// The <see cref="System.Diagnostics.Activity"/> baggage key under which the resolved tenant
    /// identifier (or the <see cref="Guid.Empty"/> no-tenant sentinel) is stored, as its string
    /// form. Surfaced onto every log record produced for the remainder of the request via
    /// <c>SharedKernel.ServiceDefaults</c>'s <c>BaggageLogRecordProcessor</c>.
    /// </summary>
    public const string TenantId = "TenantId";
}
