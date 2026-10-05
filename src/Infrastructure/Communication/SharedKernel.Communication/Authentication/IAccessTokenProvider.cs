using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication;

/// <summary>
/// Supplies the access token a client sends as <c>Authorization</c>: implement it for a credential the built-in modes
/// do not cover (a cloud managed identity, a token exchange, a workload identity), and attach it to a client with
/// <c>UseAccessTokenProvider&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
/// Called for every request attempt, so cache the token yourself and return it until it is close to expiry. A failed
/// result fails the request with <see cref="CommunicationErrorCodes.AccessTokenUnavailable"/>.
/// </remarks>
public interface IAccessTokenProvider
{
    /// <summary>Returns the token for the next request.</summary>
    /// <param name="context">The client the token is for, and whether the last token was just refused.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The token, or the error that prevented getting one.</returns>
    ValueTask<Result<AccessToken>> GetAccessTokenAsync(AccessTokenContext context, CancellationToken cancellationToken);
}

/// <summary>An access token and the scheme it is sent with.</summary>
/// <param name="Value">The token.</param>
/// <param name="ExpiresAt">When it expires, if known.</param>
/// <param name="Scheme">The <c>Authorization</c> scheme. <c>Bearer</c> by default.</param>
public sealed record AccessToken(string Value, DateTimeOffset? ExpiresAt = null, string Scheme = "Bearer")
{
    /// <summary>Returns the scheme only, so a token never reaches a log through <see cref="object.ToString"/>.</summary>
    /// <returns>The scheme and a redaction marker.</returns>
    public override string ToString() => $"{Scheme} [redacted]";
}

/// <summary>What an <see cref="IAccessTokenProvider"/> is asked for.</summary>
/// <param name="ClientName">The name the client was registered under.</param>
/// <param name="ForceRefresh">
/// <see langword="true"/> when the service answered 401 to the last token: return a new one rather than a cached one.
/// </param>
public readonly record struct AccessTokenContext(string ClientName, bool ForceRefresh);
