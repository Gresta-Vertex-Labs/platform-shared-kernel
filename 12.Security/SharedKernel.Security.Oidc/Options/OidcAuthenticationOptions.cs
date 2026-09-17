using Microsoft.Extensions.Options;
using SharedKernel.Configuration;

namespace SharedKernel.Security.Oidc.Options;

/// <summary>Settings for validating access tokens from an OpenID Connect provider.</summary>
/// <remarks>Bound from <c>SharedKernel:Security:Oidc</c> and validated at startup.</remarks>
public sealed class OidcAuthenticationOptions : ISectionBoundOptions
{
    /// <inheritdoc/>
    public static string SectionName => "SharedKernel:Security:Oidc";

    /// <summary>
    /// Gets or sets the issuer URL whose <c>/.well-known/openid-configuration</c> supplies the signing keys, for
    /// example <c>https://login.microsoftonline.com/{tenant}/v2.0</c>. Required.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the accepted <c>aud</c> values: this API's identifier (an app id URI or client id). At least
    /// one is required.
    /// </summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>
    /// Gets or sets the accepted <c>iss</c> values. Empty uses the issuer from the discovery document.
    /// </summary>
    public List<string> ValidIssuers { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the discovery document must be fetched over HTTPS. Defaults to
    /// <see langword="true"/>; turn it off only for a local identity provider during development.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets the signing algorithms accepted for access tokens. Empty, the default, accepts <c>RS256</c>,
    /// <c>PS256</c> and <c>ES256</c>. Only asymmetric JWS algorithms (<c>RS</c>, <c>PS</c> and <c>ES</c> 256 to 512)
    /// are allowed; set <c>PS256</c> and <c>ES256</c> for FAPI 2.0.
    /// </summary>
    /// <remarks>A configured list replaces the defaults rather than adding to them.</remarks>
    public List<string> ValidAlgorithms { get; set; } = [];

    /// <summary>
    /// Gets or sets the accepted JWT <c>typ</c> header values. Empty accepts any. Set <c>at+jwt</c> when the
    /// provider issues RFC 9068 access tokens, so an ID token for the same audience is rejected.
    /// </summary>
    public List<string> ValidTokenTypes { get; set; } = [];

    /// <summary>Gets or sets the tolerated clock difference with the provider, from zero to five minutes. Defaults to 30 seconds.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets which claims populate <see cref="Abstractions.IUserContext"/>.</summary>
    [ValidateObjectMembers]
    public OidcClaimOptions Claims { get; set; } = new();

    /// <summary>Gets or sets the DPoP settings, used after <c>AddDpop</c> is called.</summary>
    [ValidateObjectMembers]
    public DpopOptions Dpop { get; set; } = new();

    /// <summary>Gets or sets the token revocation settings, used after <c>AddTokenRevocation</c> is called.</summary>
    [ValidateObjectMembers]
    public TokenRevocationOptions Revocation { get; set; } = new();
}
