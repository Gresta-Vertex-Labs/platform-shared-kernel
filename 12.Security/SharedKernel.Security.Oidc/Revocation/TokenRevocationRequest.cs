namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>An access token to check for revocation.</summary>
public sealed class TokenRevocationRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="token">The encoded access token.</param>
    /// <param name="tokenHash">The Base64url SHA-256 hash of <paramref name="token"/>.</param>
    /// <param name="tokenId">The <c>jti</c> claim, if any.</param>
    /// <param name="subjectId">The <c>sub</c> claim, if any.</param>
    /// <param name="clientId">The client id, if any.</param>
    /// <param name="sessionId">The session id, if any.</param>
    /// <param name="expiresAt">When the token expires.</param>
    public TokenRevocationRequest(
        string token,
        string tokenHash,
        string? tokenId,
        string? subjectId,
        string? clientId,
        string? sessionId,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentException.ThrowIfNullOrEmpty(tokenHash);
        Token = token;
        TokenHash = tokenHash;
        TokenId = tokenId;
        SubjectId = subjectId;
        ClientId = clientId;
        SessionId = sessionId;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Gets the encoded access token, for an introspection call. It is a live credential: never log or store it.
    /// </summary>
    public string Token { get; }

    /// <summary>Gets the Base64url SHA-256 hash of <see cref="Token"/>, safe to use as a storage or cache key.</summary>
    public string TokenHash { get; }

    /// <summary>Gets the <c>jti</c> claim, or <see langword="null"/>.</summary>
    public string? TokenId { get; }

    /// <summary>Gets the <c>sub</c> claim, or <see langword="null"/>.</summary>
    public string? SubjectId { get; }

    /// <summary>Gets the client id, or <see langword="null"/>.</summary>
    public string? ClientId { get; }

    /// <summary>Gets the session id, or <see langword="null"/>.</summary>
    public string? SessionId { get; }

    /// <summary>Gets when the token expires.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Returns a description without the token.</summary>
    /// <returns>The token hash and expiry.</returns>
    public override string ToString() => $"TokenRevocationRequest {{ TokenHash = {TokenHash}, ExpiresAt = {ExpiresAt:O} }}";
}
