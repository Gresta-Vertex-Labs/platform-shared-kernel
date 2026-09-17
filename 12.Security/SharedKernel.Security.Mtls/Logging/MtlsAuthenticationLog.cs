using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Mtls.Logging;

// EventIds 12300-12399: SharedKernel.Security.Mtls's block within 12.Security's 12000-12999 range.
// The certificate thumbprint is public information; the certificate itself is never logged.
internal static partial class MtlsAuthenticationLog
{
    [LoggerMessage(
        EventId = 12300,
        Level = LogLevel.Warning,
        Message = "Client certificate rejected by the validator (reason: {Reason}, thumbprint: {Thumbprint}).")]
    public static partial void CertificateRejected(ILogger logger, string reason, string thumbprint);

    [LoggerMessage(
        EventId = 12301,
        Level = LogLevel.Error,
        Message = "The certificate validator failed; the client certificate was rejected (thumbprint: {Thumbprint}).")]
    public static partial void ValidatorFailed(ILogger logger, Exception exception, string thumbprint);
}
