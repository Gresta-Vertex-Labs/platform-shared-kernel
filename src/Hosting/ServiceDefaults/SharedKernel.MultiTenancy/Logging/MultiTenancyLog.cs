using Microsoft.Extensions.Logging;

namespace SharedKernel.MultiTenancy.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> log events for
/// <c>SharedKernel.MultiTenancy</c>.
/// </summary>
/// <remarks>
/// <para>
/// This package's first-ever production logging. Reserves <c>EventId</c> range
/// <c>13100</c>–<c>13199</c> within <c>01.Core</c>'s platform-wide <c>13000</c>–<c>13999</c> domain
/// block (the sibling package, <c>SharedKernel.ServiceDefaults</c>, owns
/// <c>13000</c>–<c>13099</c> via <c>ServiceDefaults.Logging.ServiceDefaultsLog</c>).
/// </para>
/// <para>
/// <b>No tenant id in any message.</b> The resolved tenant reaches every exported log record of the request
/// ambiently, as the <c>TenantId</c> record attribute the base's baggage processor copies from the tenant baggage
/// item, so <see cref="TenantResolved"/> carries only the strategy that resolved it.
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
        Message = "Resolved the tenant with the strategy '{StrategyName}'.")]
    public static partial void TenantResolved(ILogger logger, string strategyName);

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
