namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>Decides whether a valid access token was revoked before it expired.</summary>
/// <remarks>
/// Implemented by the consuming service, for example with an RFC 7662 introspection call or a lookup in a list of
/// revoked token ids or sessions. Runs after signature, issuer, audience, lifetime and sender-constraint checks
/// pass. An exception rejects the request.
/// </remarks>
public interface ITokenRevocationCheck
{
    /// <summary>Returns whether the token was revoked.</summary>
    /// <param name="request">The token and its identifiers.</param>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns><see langword="true"/> to reject the request.</returns>
    ValueTask<bool> IsRevokedAsync(TokenRevocationRequest request, CancellationToken cancellationToken);
}
