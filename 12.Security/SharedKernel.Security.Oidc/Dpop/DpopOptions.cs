namespace SharedKernel.Security.Oidc.Dpop;

/// <summary>
/// Configuration for DPoP (RFC 9449) sender-constrained access-token validation.
/// </summary>
/// <remarks>
/// Only consulted when <c>SecurityAuthenticationBuilder.RequireDpop&lt;TReplayCache&gt;()</c> has been
/// called — DPoP validation is opt-in and disabled by default (WO-058, P-376).
/// </remarks>
public sealed class DpopOptions
{
    /// <summary>
    /// Gets or sets the maximum allowed age, in seconds, of a DPoP proof's <c>iat</c> (issued-at) claim
    /// at the time it is presented.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>60</c> seconds. A proof older than this window is rejected as stale, regardless of
    /// whether its signature and <c>jkt</c> binding are otherwise valid.
    /// </remarks>
    public int ProofFreshnessWindowSeconds { get; set; } = 60;
}
