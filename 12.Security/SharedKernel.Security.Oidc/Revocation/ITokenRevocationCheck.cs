namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>
/// A consumer-supplied post-validation check for whether a bearer access token has been revoked.
/// </summary>
/// <remarks>
/// <para>
/// The sole extensibility point for token revocation, opted into via
/// <c>SecurityAuthenticationBuilder.WithRevocationCheck&lt;TCheck&gt;()</c>. This package never dictates
/// Redis/database/introspection-endpoint specifics — the consumer wires the actual store or RFC 7662
/// introspection HTTP call at its own composition root (e.g. via <c>02.Caching</c>), never a direct
/// <c>12.Security</c> → <c>02.Caching</c> reference (WO-058, P-379).
/// </para>
/// <para>
/// <b>Execution/failure contract:</b> runs only after standard signature/issuer/audience/lifetime
/// validation succeeds. A revoked token rejects with the same generic authentication-failure shape as an
/// expired/malformed token — no distinct, information-leaking error is ever surfaced. An unavailable or
/// throwing implementation FAILS CLOSED (rejects the request) — it never silently fails open.
/// </para>
/// </remarks>
public interface ITokenRevocationCheck
{
    /// <summary>
    /// Determines whether the given bearer access token has been revoked.
    /// </summary>
    /// <param name="tokenIdentifier">
    /// The raw bearer token string — supports both a <c>jti</c>-keyed revocation-list lookup and an
    /// RFC 7662 introspection HTTP call to the identity provider (introspection requires the raw token).
    /// </param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> when the token has been revoked and the request must be rejected;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct);
}
