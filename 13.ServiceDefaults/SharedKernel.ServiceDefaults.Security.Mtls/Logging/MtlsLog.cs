using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> log events for mutual-TLS host composition.
/// </summary>
/// <remarks>
/// <para>
/// EventIds <c>13000</c>, <c>13001</c>, and <c>13003</c> belong to the ServiceDefaults package
/// family's shared <c>13000</c>–<c>13099</c> sub-block. They moved here unchanged from the
/// composition base when WO-084 split it, so existing dashboards and alert rules keep matching.
/// See <c>SharedKernel.ServiceDefaults.Logging.ServiceDefaultsLog</c> for the full family
/// allocation.
/// </para>
/// <para>
/// <b>Never logs a certificate's raw PEM or DER bytes</b> — only its thumbprint, subject, and a
/// rejection reason.
/// </para>
/// </remarks>
internal static partial class MtlsLog
{
    /// <summary>
    /// Logged when a mutual-TLS client certificate — negotiated directly by Kestrel, or forwarded
    /// through <see cref="Security.MtlsForwardedHeaderMiddleware"/> — is accepted by the registered
    /// <c>IMtlsCertificateValidator</c>.
    /// </summary>
    [LoggerMessage(
        EventId = 13000,
        Level = LogLevel.Information,
        Message = "Mutual-TLS client certificate accepted (thumbprint: {Thumbprint}, subject: {Subject}).")]
    public static partial void MtlsCertificateAccepted(ILogger logger, string thumbprint, string subject);

    /// <summary>
    /// Logged when a mutual-TLS client certificate is rejected — either by the registered
    /// <c>IMtlsCertificateValidator</c>, or by <see cref="Security.MtlsForwardedHeaderMiddleware"/>'s
    /// <c>TrustedNetworks</c> allowlist before the certificate was decoded, in which case
    /// <paramref name="thumbprint"/> and <paramref name="subject"/> are placeholders and
    /// <paramref name="reason"/> carries the actual cause.
    /// </summary>
    [LoggerMessage(
        EventId = 13001,
        Level = LogLevel.Warning,
        Message = "Mutual-TLS client certificate rejected (thumbprint: {Thumbprint}, subject: {Subject}, reason: {Reason}).")]
    public static partial void MtlsCertificateRejected(ILogger logger, string thumbprint, string subject, string reason);

    /// <summary>
    /// Logged once at startup when
    /// <see cref="Security.MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate"/> is
    /// configured with an empty <c>TrustedNetworks</c> allowlist — any network path reaching this
    /// host directly (a misconfigured <c>NetworkPolicy</c>, a multi-hop mesh, a debug port, a
    /// compromised sidecar) can forge the forwarded-certificate header exactly as the real ingress
    /// would.
    /// </summary>
    [LoggerMessage(
        EventId = 13003,
        Level = LogLevel.Warning,
        Message = "MtlsForwardedHeaderMiddleware has no configured TrustedNetworks allowlist for header '{HeaderName}' — any network path reaching this host directly can forge this header.")]
    public static partial void ForwardedHeaderTrustBoundaryUnconfigured(ILogger logger, string headerName);
}
