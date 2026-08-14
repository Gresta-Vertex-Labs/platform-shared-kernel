namespace SharedKernel.Security.Oidc.Dpop;

// Internal, package-local claim type used to signal a successful DPoP (RFC 9449) proof validation from
// DpopProofValidator (running inside JwtBearerEvents.OnTokenValidated) to OidcUserContext (constructed
// later, from the same validated ClaimsPrincipal, by the DI-registered IUserContext factory). This claim
// is never emitted by an identity provider — it is stamped onto the principal's identity by this package
// itself and is never a genuine part of the token's own claim set.
internal static class DpopClaimTypes
{
    // Presence + "true" value signals IsSenderConstrained = true. Never a standard OIDC/OAuth2 claim name
    // — deliberately namespaced so it can never collide with a real claim emitted by an identity provider.
    internal const string SenderConstrained = "sk_dpop_bound";
}
