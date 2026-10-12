using SharedKernel.Primitives.Logging;

namespace SharedKernel.Security.Oidc.Diagnostics;

/// <summary>The EventIds of this package: sub-block 1 of the <see cref="LoggingEventIdRanges.Security"/> block (12100-12199).</summary>
internal static class OidcLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Security + 1 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>The token names neither a subject nor a client id.</summary>
    internal const int SubjectMissing = Base + 0;

    /// <summary>The tenant claim is not a valid GUID.</summary>
    internal const int TenantClaimInvalid = Base + 1;

    /// <summary>DPoP validation rejected the request.</summary>
    internal const int DpopRejected = Base + 2;

    /// <summary>The revocation check reported the token as revoked.</summary>
    internal const int TokenRevoked = Base + 3;

    /// <summary>The token's signing algorithm is not allowed.</summary>
    internal const int SigningAlgorithmRejected = Base + 4;

    /// <summary>The certificate binding of the token was rejected.</summary>
    internal const int CertificateBindingRejected = Base + 5;

    /// <summary>The revocation cache failed and the check was called directly.</summary>
    internal const int RevocationCacheFailed = Base + 6;

    /// <summary>The revocation check threw and the token was rejected.</summary>
    internal const int RevocationCheckFailed = Base + 7;
}
