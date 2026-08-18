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

    /// <summary>
    /// Gets or sets the set of JWS signing algorithms accepted for a DPoP proof JWT's own embedded-<c>jwk</c>
    /// signature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <c>["PS256", "ES256"]</c> — the same FAPI 2.0 Security Profile baseline as
    /// <c>SecurityOptions.JwtOptions.ValidAlgorithms</c>, but configured independently: a DPoP proof's
    /// signing key is CLIENT-generated and need not use the same algorithm as the identity provider's own
    /// token-signing key.
    /// </para>
    /// <para>
    /// Enforced inside <c>DpopProofValidator</c>'s proof-JWT signature verification, checked BEFORE
    /// embedded-<c>jwk</c> signature evaluation proceeds — so an alg-confusion/downgrade attempt,
    /// including a crafted <c>"alg": "none"</c> proof, never reaches signature evaluation at all, and
    /// before any other DPoP binding (<c>typ</c>/<c>htm</c>/<c>htu</c>/<c>iat</c>/<c>jkt</c>/<c>ath</c>) is
    /// checked (WO-060, C-41).
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<string> ValidAlgorithms { get; set; } = ["PS256", "ES256"];
}
