using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Oidc.Logging;

// EventIds 12100-12199: SharedKernel.Security.Oidc's block within 12.Security's 12000-12999 range.
// Never logs a token, a proof, a claim value or a certificate.
internal static partial class OidcLog
{
    [LoggerMessage(
        EventId = 12100,
        Level = LogLevel.Warning,
        Message = "An authenticated token carries neither a subject nor a client id; the caller is treated as anonymous.")]
    public static partial void SubjectMissing(ILogger logger);

    [LoggerMessage(
        EventId = 12101,
        Level = LogLevel.Warning,
        Message = "The tenant claim '{TenantClaimType}' is not a GUID; the caller has no tenant.")]
    public static partial void TenantClaimInvalid(ILogger logger, string tenantClaimType);

    [LoggerMessage(
        EventId = 12102,
        Level = LogLevel.Warning,
        Message = "DPoP validation failed (reason: {Reason}).")]
    public static partial void DpopRejected(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 12103,
        Level = LogLevel.Warning,
        Message = "Access token rejected by the revocation check (CheckAvailable: {CheckAvailable}).")]
    public static partial void TokenRevocationRejected(ILogger logger, bool checkAvailable);

    [LoggerMessage(
        EventId = 12104,
        Level = LogLevel.Warning,
        Message = "Access token rejected: signing algorithm '{Algorithm}' is not allowed.")]
    public static partial void SigningAlgorithmRejected(ILogger logger, string algorithm);

    [LoggerMessage(
        EventId = 12105,
        Level = LogLevel.Warning,
        Message = "Certificate-bound access token rejected (reason: {Reason}).")]
    public static partial void CertificateBindingRejected(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 12106,
        Level = LogLevel.Warning,
        Message = "The revocation cache failed; the revocation check is called directly.")]
    public static partial void RevocationCacheFailed(ILogger logger, Exception exception);
}
