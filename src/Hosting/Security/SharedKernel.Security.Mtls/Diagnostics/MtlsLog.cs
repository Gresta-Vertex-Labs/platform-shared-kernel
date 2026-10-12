using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Mtls.Diagnostics;

// The certificate thumbprint is public information; the certificate itself is never logged.
internal static partial class MtlsLog
{
    [LoggerMessage(
        EventId = MtlsLoggingEventIds.CertificateRejected,
        Level = LogLevel.Warning,
        Message = "The client certificate was rejected with rejection code '{RejectionCode}' for thumbprint '{Thumbprint}'.")]
    public static partial void CertificateRejected(ILogger logger, string rejectionCode, string thumbprint);

    [LoggerMessage(
        EventId = MtlsLoggingEventIds.ValidatorFailed,
        Level = LogLevel.Error,
        Message = "The certificate validator failed, so the client certificate was rejected for thumbprint '{Thumbprint}'.")]
    public static partial void ValidatorFailed(ILogger logger, string thumbprint, Exception exception);
}
