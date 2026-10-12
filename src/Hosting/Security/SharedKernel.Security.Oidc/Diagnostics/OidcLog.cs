using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Oidc.Diagnostics;

// Never logs a token, a proof, a claim value or a certificate.
internal static partial class OidcLog
{
    [LoggerMessage(
        EventId = OidcLoggingEventIds.SubjectMissing,
        Level = LogLevel.Warning,
        Message = "The token names neither a subject nor a client id, so the caller is anonymous.")]
    public static partial void SubjectMissing(ILogger logger);

    [LoggerMessage(
        EventId = OidcLoggingEventIds.TenantClaimInvalid,
        Level = LogLevel.Warning,
        Message = "The tenant claim '{TenantClaimType}' is not a valid GUID, so the caller has no tenant.")]
    public static partial void TenantClaimInvalid(ILogger logger, string tenantClaimType);

    [LoggerMessage(
        EventId = OidcLoggingEventIds.DpopRejected,
        Level = LogLevel.Warning,
        Message = "DPoP validation failed with rejection code '{RejectionCode}'.")]
    public static partial void DpopRejected(ILogger logger, string rejectionCode);

    [LoggerMessage(
        EventId = OidcLoggingEventIds.TokenRevoked,
        Level = LogLevel.Warning,
        Message = "The access token was rejected because it is revoked.")]
    public static partial void TokenRevocationRejected(ILogger logger);

    [LoggerMessage(
        EventId = OidcLoggingEventIds.SigningAlgorithmRejected,
        Level = LogLevel.Warning,
        Message = "The signing algorithm '{Algorithm}' is not allowed.")]
    public static partial void SigningAlgorithmRejected(ILogger logger, string algorithm);

    [LoggerMessage(
        EventId = OidcLoggingEventIds.CertificateBindingRejected,
        Level = LogLevel.Warning,
        Message = "The certificate binding was rejected with rejection code '{RejectionCode}'.")]
    public static partial void CertificateBindingRejected(ILogger logger, string rejectionCode);

    [LoggerMessage(
        EventId = OidcLoggingEventIds.RevocationCacheFailed,
        Level = LogLevel.Warning,
        Message = "The revocation cache failed, so the revocation check is called directly.")]
    public static partial void RevocationCacheFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = OidcLoggingEventIds.RevocationCheckFailed,
        Level = LogLevel.Error,
        Message = "The revocation check failed, so the access token was rejected.")]
    public static partial void RevocationCheckFailed(ILogger logger, Exception exception);
}
