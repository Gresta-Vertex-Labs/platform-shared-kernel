using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> log events for mutual-TLS host composition.
/// </summary>
/// <remarks>
/// <para>
/// EventIds <c>13000</c>, <c>13001</c>, <c>13003</c> and <c>13008</c> belong to the ServiceDefaults package
/// family's shared <c>13000</c>–<c>13099</c> sub-block. See <c>SharedKernel.ServiceDefaults.Logging.ServiceDefaultsLog</c>
/// for the full family allocation.
/// </para>
/// <para>
/// <b>Never logs a certificate's raw PEM or DER bytes, its subject, a remote IP address or a validator's free-text
/// reason</b> — only the thumbprint. A forwarded certificate that was never decoded has no thumbprint, so its
/// refusal message has no parameters at all.
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
        Message = "Accepted the client certificate with the thumbprint '{Thumbprint}'.")]
    public static partial void MtlsCertificateAccepted(ILogger logger, string thumbprint);

    /// <summary>
    /// Logged when the registered <c>IMtlsCertificateValidator</c> rejects a mutual-TLS client certificate, whether
    /// Kestrel negotiated it or it was forwarded through <see cref="Security.MtlsForwardedHeaderMiddleware"/>.
    /// </summary>
    [LoggerMessage(
        EventId = 13001,
        Level = LogLevel.Warning,
        Message = "Rejected the client certificate with the thumbprint '{Thumbprint}'.")]
    public static partial void MtlsCertificateRejected(ILogger logger, string thumbprint);

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

    /// <summary>
    /// Logged when a forwarded client certificate is ignored because the request's source is outside the configured
    /// <c>TrustedNetworks</c>. The header is never decoded, so there is no thumbprint, and the remote address is not
    /// logged.
    /// </summary>
    [LoggerMessage(
        EventId = 13008,
        Level = LogLevel.Warning,
        Message = "Ignored a forwarded client certificate because its source is outside the configured trusted networks.")]
    public static partial void ForwardedCertificateSourceUntrusted(ILogger logger);
}
