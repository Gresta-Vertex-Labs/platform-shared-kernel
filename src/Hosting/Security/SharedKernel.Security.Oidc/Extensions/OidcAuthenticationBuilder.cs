using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Security.Oidc.Authentication;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Revocation;

namespace SharedKernel.Security.Oidc.Extensions;

/// <summary>Adds optional checks to the OIDC authentication registered by <c>AddOidcAuthentication</c>.</summary>
public sealed class OidcAuthenticationBuilder
{
    internal OidcAuthenticationBuilder(IServiceCollection services) => Services = services;

    /// <summary>Gets the service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Accepts DPoP sender-constrained tokens (RFC 9449), presented as <c>Authorization: DPoP &lt;token&gt;</c> with a
    /// <c>DPoP</c> proof header. Settings come from <c>SharedKernel:Security:Oidc:Dpop</c>.
    /// </summary>
    /// <typeparam name="TReplayCache">The store that rejects a proof used twice; shared by every replica.</typeparam>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// Without this call a DPoP-bound token is always rejected, because its proof cannot be checked. Also registers
    /// ASP.NET Core Data Protection for server nonces.
    /// </remarks>
    public OidcAuthenticationBuilder AddDpop<TReplayCache>()
        where TReplayCache : class, IDpopReplayCache
    {
        Services.AddDataProtection();
        Services.TryAddSingleton<DpopRegistration>();
        Services.TryAddSingleton<DpopNonceService>();
        Services.TryAddScoped<IDpopReplayCache, TReplayCache>();
        Services.TryAddScoped<DpopProofValidator>();
        return this;
    }

    /// <summary>
    /// Checks every validated token with <typeparamref name="TCheck"/> and rejects revoked tokens. An exception from
    /// the check rejects the request.
    /// </summary>
    /// <typeparam name="TCheck">The revocation check, for example an RFC 7662 introspection client.</typeparam>
    /// <returns>The same builder.</returns>
    public OidcAuthenticationBuilder AddTokenRevocation<TCheck>()
        where TCheck : class, ITokenRevocationCheck
    {
        Services.TryAddScoped<ITokenRevocationCheck, TCheck>();
        Services.TryAddScoped<TokenRevocationEnforcer>();
        return this;
    }

    /// <summary>
    /// Caches revocation answers in <typeparamref name="TCache"/>: "revoked" until the token expires, "not revoked"
    /// for <c>Revocation:NotRevokedCacheDuration</c>.
    /// </summary>
    /// <typeparam name="TCache">The cache.</typeparam>
    /// <returns>The same builder.</returns>
    /// <remarks>Has no effect unless <see cref="AddTokenRevocation{TCheck}"/> is called too.</remarks>
    public OidcAuthenticationBuilder AddTokenRevocationCache<TCache>()
        where TCache : class, ITokenRevocationCache
    {
        Services.TryAddScoped<ITokenRevocationCache, TCache>();
        return this;
    }
}
