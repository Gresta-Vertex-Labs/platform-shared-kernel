using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Logging;

namespace SharedKernel.Security.Oidc.Authentication;

// Wraps the configured events (Options.Events or EventsType) for every request. Adds DPoP token extraction,
// captures the validated token and logs algorithm rejections; everything else is passed through unchanged.
internal sealed class OidcJwtBearerEvents(JwtBearerEvents inner, bool dpopEnabled, ILogger logger) : JwtBearerEvents
{
    public override async Task MessageReceived(MessageReceivedContext context)
    {
        await inner.MessageReceived(context).ConfigureAwait(false);

        OidcRequestState? state = OidcRequestState.Get(context.HttpContext);
        if (context.Result is null && string.IsNullOrEmpty(context.Token) && dpopEnabled && state?.DpopToken is { } token)
        {
            context.Token = token;
            state.PresentedWithDpop = true;
        }
    }

    public override Task TokenValidated(TokenValidatedContext context)
    {
        if (OidcRequestState.Get(context.HttpContext) is { } state)
        {
            state.ValidatedToken = context.SecurityToken as JsonWebToken;
        }

        return inner.TokenValidated(context);
    }

    public override Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        // IdentityModel reports a disallowed algorithm as a signature failure without the algorithm exception, so read
        // the header of the rejected token instead.
        if (ReadAlgorithm(context) is { } algorithm
            && context.Options.TokenValidationParameters.ValidAlgorithms is { } allowed
            && !allowed.Contains(algorithm, StringComparer.Ordinal))
        {
            OidcLog.SigningAlgorithmRejected(logger, algorithm);
        }

        return inner.AuthenticationFailed(context);
    }

    private static string? ReadAlgorithm(AuthenticationFailedContext context)
    {
        string authorization = context.Request.Headers.Authorization.ToString();
        int space = authorization.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0)
        {
            return null;
        }

        try
        {
            string alg = new JsonWebToken(authorization[(space + 1)..].Trim()).Alg;
            return string.IsNullOrEmpty(alg) ? "none" : alg;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException)
        {
            return null;
        }
    }

    public override Task Challenge(JwtBearerChallengeContext context) => inner.Challenge(context);

    public override Task Forbidden(ForbiddenContext context) => inner.Forbidden(context);
}
