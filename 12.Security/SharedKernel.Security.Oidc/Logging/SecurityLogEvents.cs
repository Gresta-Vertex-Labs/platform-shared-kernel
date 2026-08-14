using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.Oidc.Logging;

/// <summary>
/// Structured, source-generated <c>[LoggerMessage]</c> security-audit log events for
/// <c>SharedKernel.Security.Oidc</c>.
/// </summary>
/// <remarks>
/// <para>
/// Reserves <c>EventId</c> sub-range <c>12100</c>–<c>12199</c> within <c>01.Core</c>'s
/// <c>LoggingEventIdRanges.Security</c> (<c>12000</c>–<c>12999</c>) block, per the platform's WO-041
/// logging convention (WO-057, P-371).
/// </para>
/// <para>
/// <b>Never logs a raw claim value, raw token content, or a raw API key</b> — only structured, safe
/// fields (booleans, claim-type names, failure-reason strings).
/// </para>
/// </remarks>
internal static partial class SecurityLogEvents
{
    /// <summary>
    /// Logged when an authenticated OIDC principal carries no parseable human subject (<c>sub</c>) claim
    /// and is therefore recognized as a service-principal (client-credentials/machine-to-machine)
    /// identity. This is an expected, routine outcome for a normal service-to-service call — not an
    /// error.
    /// </summary>
    [LoggerMessage(
        EventId = 12100,
        Level = LogLevel.Debug,
        Message = "OIDC principal authenticated with no human subject claim recognized as a service-principal identity (SubjectClaimPresent={SubjectClaimPresent}).")]
    public static partial void ServicePrincipalRecognized(ILogger logger, bool subjectClaimPresent);

    /// <summary>
    /// Logged when an authenticated OIDC principal's tenant claim is absent or could not be parsed as a
    /// <see cref="Guid"/>, so <c>ITenantProvider.TenantId</c> resolved to <see cref="Guid.Empty"/>.
    /// </summary>
    [LoggerMessage(
        EventId = 12101,
        Level = LogLevel.Warning,
        Message = "Tenant claim '{TenantClaimType}' was absent or could not be parsed as a Guid on an authenticated principal; TenantId resolved to Guid.Empty.")]
    public static partial void TenantClaimResolutionFailed(ILogger logger, string tenantClaimType);

    /// <summary>
    /// Logged when <c>DpopProofValidator</c> rejects a DPoP (RFC 9449) proof presented alongside a
    /// sender-constrained access token.
    /// </summary>
    /// <remarks>
    /// <paramref name="failureReason"/> is a structured value (e.g. <c>"MissingProof"</c>,
    /// <c>"JktMismatch"</c>, <c>"Expired"</c>, <c>"Replayed"</c>) — never the raw proof or token.
    /// Wired into <c>DpopProofValidator</c> in SK.12.Core (C-30/C-37).
    /// </remarks>
    [LoggerMessage(
        EventId = 12102,
        Level = LogLevel.Warning,
        Message = "DPoP proof validation failed (reason: {FailureReason}).")]
    public static partial void DpopProofRejected(ILogger logger, string failureReason);

    /// <summary>
    /// Logged when an access token is rejected because <c>ITokenRevocationCheck</c> reported it as
    /// revoked, or because the check itself was unavailable or threw and therefore failed closed.
    /// </summary>
    /// <remarks>
    /// <paramref name="checkAvailable"/> distinguishes a genuine revocation from a fail-closed outcome
    /// without leaking which — both reject with the same generic authentication-failure shape at the
    /// call site. Wired into the revocation-check seam in SK.12.Core (C-36/C-37).
    /// </remarks>
    [LoggerMessage(
        EventId = 12103,
        Level = LogLevel.Warning,
        Message = "Access token rejected by revocation check (CheckAvailable={CheckAvailable}).")]
    public static partial void TokenRevocationRejected(ILogger logger, bool checkAvailable);
}
