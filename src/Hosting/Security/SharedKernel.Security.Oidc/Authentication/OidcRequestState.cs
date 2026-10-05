using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;

namespace SharedKernel.Security.Oidc.Authentication;

// Per-request state shared between OidcJwtBearerHandler and OidcJwtBearerEvents through HttpContext.Items.
internal sealed class OidcRequestState
{
    private static readonly object ItemsKey = new();

    private OidcRequestState(string? dpopToken) => DpopToken = dpopToken;

    // The token from an "Authorization: DPoP <token>" header, or null.
    public string? DpopToken { get; }

    // True when the handler authenticated the token from the DPoP authorization scheme.
    public bool PresentedWithDpop { get; set; }

    // The validated access token, captured in OnTokenValidated.
    public JsonWebToken? ValidatedToken { get; set; }

    // The RFC 6750 / RFC 9449 error code for the DPoP challenge, when DPoP validation failed.
    public string? DpopError { get; set; }

    public static OidcRequestState Begin(HttpContext context)
    {
        var state = new OidcRequestState(ReadDpopToken(context.Request.Headers.Authorization));
        context.Items[ItemsKey] = state;
        return state;
    }

    public static OidcRequestState? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemsKey, out object? value) ? value as OidcRequestState : null;

    private static string? ReadDpopToken(StringValues authorization)
    {
        if (authorization.Count != 1 || authorization[0] is not { } value)
        {
            return null;
        }

        string prefix = OidcAuthenticationDefaults.DpopScheme + " ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string token = value[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
