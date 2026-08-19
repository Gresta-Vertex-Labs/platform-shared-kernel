using Microsoft.Extensions.Logging;

namespace SharedKernel.MultiTenancy.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> log events for
/// <c>SharedKernel.MultiTenancy</c>.
/// </summary>
/// <remarks>
/// <para>
/// This package's first-ever production logging (WO-061/P-395). Reserves <c>EventId</c> range
/// <c>13100</c>–<c>13199</c> within <c>01.Core</c>'s platform-wide <c>13000</c>–<c>13999</c> domain
/// block (the sibling package, <c>SharedKernel.ServiceDefaults</c>, owns
/// <c>13000</c>–<c>13099</c> via <c>ServiceDefaults.Logging.ServiceDefaultsLog</c>).
/// </para>
/// <para>
/// <see cref="TenantResolved"/>'s <c>{TenantId}</c> placeholder is a deliberate exception to the
/// platform's "CorrelationId/TraceId/TenantId never as an explicit template placeholder" logging
/// convention: here <c>TenantId</c> IS the message's own payload (which tenant was resolved, and
/// by which strategy), not context repeated across unrelated log statements.
/// </para>
/// </remarks>
internal static partial class MultiTenancyLog
{
    /// <summary>
    /// Logged when a <see cref="Resolution.ITenantResolutionStrategy"/> resolves a tenant for the
    /// current request.
    /// </summary>
    [LoggerMessage(
        EventId = 13100,
        Level = LogLevel.Debug,
        Message = "Tenant {TenantId} resolved via strategy '{StrategyName}'.")]
    public static partial void TenantResolved(ILogger logger, Guid tenantId, string strategyName);

    /// <summary>
    /// Logged when no configured strategy resolves a tenant for the current request — including
    /// when an <see cref="Resolution.ITenantStatusValidator"/> rejects an otherwise-resolved tenant
    /// as inactive (that case reuses this same log call site rather than a distinct message, so an
    /// inactive/suspended tenant is deliberately indistinguishable from an absent one in logs).
    /// </summary>
    /// <remarks>
    /// <see cref="LogLevel.Trace"/>, not <see cref="LogLevel.Debug"/>, deliberately — this fires on
    /// every unresolved request including anonymous/health-check traffic and would be excessively
    /// noisy at <see cref="LogLevel.Debug"/>.
    /// </remarks>
    [LoggerMessage(
        EventId = 13101,
        Level = LogLevel.Trace,
        Message = "No tenant resolved for the current request.")]
    public static partial void TenantNotResolved(ILogger logger);
}
