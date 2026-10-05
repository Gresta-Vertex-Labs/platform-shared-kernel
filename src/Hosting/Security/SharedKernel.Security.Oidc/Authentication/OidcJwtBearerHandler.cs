using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Internal;
using SharedKernel.Security.Oidc.Logging;
using SharedKernel.Security.Oidc.Options;
using SharedKernel.Security.Oidc.Revocation;

namespace SharedKernel.Security.Oidc.Authentication;

// The JWT bearer handler with sender-constraint and revocation checks after the framework's own validation. The
// checks live here, not in events, so no event or EventsType configuration can skip them.
internal sealed class OidcJwtBearerHandler(
    IOptionsMonitor<JwtBearerOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptionsMonitor<OidcAuthenticationOptions> oidcOptions,
    IClock clock)
    : JwtBearerHandler(options, loggerFactory, encoder)
{
    private bool DpopEnabled => Context.RequestServices.GetService<DpopRegistration>() is not null;

    protected override async Task InitializeEventsAsync()
    {
        await base.InitializeEventsAsync().ConfigureAwait(false);
        Events = new OidcJwtBearerEvents(Events, DpopEnabled, Logger);
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        OidcRequestState state = OidcRequestState.Begin(Context);

        AuthenticateResult result = await base.HandleAuthenticateAsync().ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return result;
        }

        if (state.ValidatedToken is not { } token
            || result.Principal.Identities.FirstOrDefault(i => i.AuthenticationType == Scheme.Name) is not { } identity)
        {
            return AuthenticateResult.Fail("The validated access token could not be inspected.");
        }

        // A signed token naming no subject and no client maps to an anonymous caller. Authenticating it would let
        // RequireAuthorization() pass for a request that IUserContext reports as anonymous.
        IUserContext user = UserContextResolver.Resolve(
            new ClaimsPrincipal(identity),
            Context.RequestServices.GetServices<IUserContextMapper>());
        if (!user.IsAuthenticated)
        {
            return AuthenticateResult.Fail("The access token identifies no subject or client.");
        }

        OidcAuthenticationOptions settings = oidcOptions.CurrentValue;
        DateTimeOffset now = clock.UtcNow;

        if (await ValidateSenderConstraintAsync(state, token, settings, now).ConfigureAwait(false) is { } failure)
        {
            return AuthenticateResult.Fail(failure);
        }

        if (Context.RequestServices.GetService<TokenRevocationEnforcer>() is { } revocation)
        {
            if (await revocation.IsRevokedAsync(token, user, settings.Revocation, now, Logger, Context.RequestAborted).ConfigureAwait(false))
            {
                return AuthenticateResult.Fail("The access token was revoked.");
            }
        }

        return result;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        await base.HandleChallengeAsync(properties).ConfigureAwait(false);

        if (!DpopEnabled || Response.HasStarted || Response.StatusCode != StatusCodes.Status401Unauthorized)
        {
            return;
        }

        OidcAuthenticationOptions settings = oidcOptions.CurrentValue;
        string? error = OidcRequestState.Get(Context)?.DpopError;
        string challenge = $"{OidcAuthenticationDefaults.DpopScheme} algs=\"{string.Join(' ', OidcDefaults.ValidAlgorithms(settings.Dpop))}\"";
        if (error is not null)
        {
            challenge += $", error=\"{error}\"";
        }

        Response.Headers.Append(HeaderNames.WWWAuthenticate, challenge);

        if (settings.Dpop.RequireNonce && error is DpopProofValidator.UseNonce or null)
        {
            Response.Headers[OidcAuthenticationDefaults.DpopNonceHeader] =
                Context.RequestServices.GetRequiredService<DpopNonceService>().Create();
        }
    }

    // RFC 9449 section 7 and RFC 8705 section 3. Returns a failure message, or null when the token may be used.
    // The confirmation is read from the validated token, not the principal, which application events may rebuild.
    private async Task<string?> ValidateSenderConstraintAsync(
        OidcRequestState state,
        Microsoft.IdentityModel.JsonWebTokens.JsonWebToken token,
        OidcAuthenticationOptions settings,
        DateTimeOffset now)
    {
        string accessToken = token.EncodedToken;
        TokenConfirmation confirmation = TokenConfirmation.Read(token.Claims);
        if (confirmation.IsMalformed)
        {
            return DpopFailure(state, "invalid_token", "MalformedConfirmation");
        }

        if (confirmation.JwkThumbprint is { } jkt)
        {
            if (!DpopEnabled)
            {
                return DpopFailure(state, error: null, "DpopNotEnabled");
            }

            if (!state.PresentedWithDpop)
            {
                return DpopFailure(state, "invalid_token", "BoundTokenPresentedAsBearer");
            }

            DpopResult proof = await Context.RequestServices
                .GetRequiredService<DpopProofValidator>()
                .ValidateAsync(Context, accessToken, jkt, settings.Dpop, now)
                .ConfigureAwait(false);

            if (!proof.IsValid)
            {
                return DpopFailure(state, proof.Error, proof.Reason!);
            }
        }
        else if (state.PresentedWithDpop)
        {
            return DpopFailure(state, "invalid_token", "UnboundTokenPresentedAsDpop");
        }
        else if (DpopEnabled && settings.Dpop.Mode == DpopMode.Required)
        {
            return DpopFailure(state, "invalid_token", "DpopRequired");
        }

        if (confirmation.CertificateThumbprint is { } x5t
            && await CertificateBinding.ValidateAsync(Context, x5t).ConfigureAwait(false) is { } reason)
        {
            OidcLog.CertificateBindingRejected(Logger, reason);
            return "The access token is bound to a different client certificate.";
        }

        return null;
    }

    private string DpopFailure(OidcRequestState state, string? error, string reason)
    {
        state.DpopError = error;
        OidcLog.DpopRejected(Logger, reason);
        return "The access token's proof of possession is invalid.";
    }
}

// Registered by AddDpop; its presence turns DPoP on.
internal sealed class DpopRegistration;
