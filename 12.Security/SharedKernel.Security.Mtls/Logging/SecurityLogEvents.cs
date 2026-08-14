using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Mtls.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> security-audit log events for
/// <c>SharedKernel.Security.Mtls</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reserves <c>EventId</c> sub-range <c>12300</c>–<c>12399</c> within <c>01.Core</c>'s
/// <c>LoggingEventIdRanges.Security</c> (<c>12000</c>–<c>12999</c>) block — the fourth per-package
/// sub-block within the domain's range, per the platform's WO-041 logging convention (WO-058, P-377).
/// </para>
/// <para>
/// <b>Never logs a raw certificate, thumbprint, or claim value</b> — only structured, safe fields
/// (failure-reason strings).
/// </para>
/// </remarks>
internal static partial class SecurityLogEvents
{
    /// <summary>
    /// Logged when <c>IMtlsCertificateValidator</c> rejects a presented client certificate, or the
    /// RFC 8705 <c>cnf.x5t#S256</c> binding check mismatches against the bearer principal's confirmation
    /// claim.
    /// </summary>
    [LoggerMessage(
        EventId = 12300,
        Level = LogLevel.Warning,
        Message = "Mutual-TLS client-certificate authentication failed (reason: {FailureReason}).")]
    public static partial void MtlsCertificateRejected(ILogger logger, string failureReason);
}
