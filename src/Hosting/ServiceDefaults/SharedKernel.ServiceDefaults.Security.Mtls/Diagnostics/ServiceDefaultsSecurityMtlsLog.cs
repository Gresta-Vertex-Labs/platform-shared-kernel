using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Security.Mtls.Diagnostics;

// Never logs a certificate's raw bytes or subject, a remote IP address or a validator's free-text reason: only the
// thumbprint. A forwarded certificate that was never decoded has no thumbprint, so its refusal has no parameters.
internal static partial class ServiceDefaultsSecurityMtlsLog
{
    [LoggerMessage(
        EventId = ServiceDefaultsSecurityMtlsLoggingEventIds.CertificateAccepted,
        Level = LogLevel.Debug,
        Message = "Accepted the client certificate with the thumbprint '{Thumbprint}'.")]
    public static partial void CertificateAccepted(ILogger logger, string thumbprint);

    [LoggerMessage(
        EventId = ServiceDefaultsSecurityMtlsLoggingEventIds.CertificateRejected,
        Level = LogLevel.Warning,
        Message = "Rejected the client certificate with the thumbprint '{Thumbprint}'.")]
    public static partial void CertificateRejected(ILogger logger, string thumbprint);

    [LoggerMessage(
        EventId = ServiceDefaultsSecurityMtlsLoggingEventIds.ForwardedCertificateRefused,
        Level = LogLevel.Warning,
        Message = "Ignored a forwarded client certificate because its source is outside the configured trusted networks.")]
    public static partial void ForwardedCertificateRefused(ILogger logger);

    [LoggerMessage(
        EventId = ServiceDefaultsSecurityMtlsLoggingEventIds.ForwardedHeaderTrustNotConfigured,
        Level = LogLevel.Warning,
        Message = "No trusted networks are configured for the forwarded certificate header '{HeaderName}', so any network path that reaches this host can forge it.")]
    public static partial void ForwardedHeaderTrustNotConfigured(ILogger logger, string headerName);
}
