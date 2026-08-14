using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel.Security.Oidc.Logging;

namespace SharedKernel.Security.Oidc.Revocation;

/// <summary>
/// Invokes the request-scoped <see cref="ITokenRevocationCheck"/> against the current access token,
/// failing the request when the token is revoked or when the check itself is unavailable or throws.
/// </summary>
/// <remarks>
/// Runs inside <c>JwtBearerEvents.OnTokenValidated</c>, after standard signature/issuer/audience/lifetime
/// validation succeeds, and only when <c>SecurityAuthenticationBuilder.WithRevocationCheck&lt;TCheck&gt;()</c>
/// was called. Fails CLOSED — an unavailable or throwing <see cref="ITokenRevocationCheck"/> rejects the
/// request; it never silently authenticates (WO-058, P-379).
/// </remarks>
internal static class RevocationCheckRunner
{
    /// <summary>
    /// Runs the revocation check for the current request's access token, failing
    /// <paramref name="context"/> when the token is revoked or the check is unavailable.
    /// </summary>
    /// <param name="context">The <c>OnTokenValidated</c> context for the current request.</param>
    internal static async Task ValidateAsync(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.HttpContext.RequestServices;
        var logger = services.GetService<ILogger<ITokenRevocationCheck>>();
        var revocationCheck = services.GetRequiredService<ITokenRevocationCheck>();
        var rawToken = ResolveRawToken(context);

        bool revoked;
        bool checkAvailable;
        try
        {
            revoked = await revocationCheck.IsRevokedAsync(rawToken, context.HttpContext.RequestAborted).ConfigureAwait(false);
            checkAvailable = true;
        }
        catch (Exception)
        {
            // Fail CLOSED — an unavailable or throwing check rejects the request, never silently
            // authenticates. The specific exception is deliberately not surfaced to the caller — the
            // same generic authentication-failure shape as a revoked token.
            revoked = true;
            checkAvailable = false;
        }

        if (revoked)
        {
            if (logger is not null)
            {
                SecurityLogEvents.TokenRevocationRejected(logger, checkAvailable);
            }

            context.Fail("Access token rejected by revocation check.");
        }
    }

    private static string ResolveRawToken(TokenValidatedContext context)
    {
        if (context.SecurityToken is JsonWebToken jsonWebToken)
        {
            return jsonWebToken.EncodedToken;
        }

        var authorizationHeader = context.HttpContext.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        return authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[bearerPrefix.Length..].Trim()
            : authorizationHeader;
    }
}
