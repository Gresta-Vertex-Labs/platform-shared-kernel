using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Security.Oidc.Options;

/// <summary>
/// Configures the claim types <see cref="Mapping.OidcUserContext"/> reads for email, username, roles,
/// and permissions, and that <c>AddSharedKernelSecurity</c>/<c>AddAzureB2CAuthentication</c> wire into
/// <c>TokenValidationParameters.NameClaimType</c>/<c>RoleClaimType</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (WO-057, P-366):</b> ASP.NET Core's <c>JwtBearerHandler</c> has defaulted
/// <c>MapInboundClaims</c> to <see langword="false"/> since .NET 8, meaning claim types arrive on the
/// wire <i>unmapped</i> — a standards-conformant OIDC issuer (Microsoft Entra ID v2.0, Auth0, Okta,
/// Keycloak) emits short claim names such as <c>"email"</c>, <c>"name"</c>, and <c>"roles"</c> directly,
/// not the legacy long-form <see cref="System.Security.Claims.ClaimTypes"/> URIs
/// (<c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress</c>, etc.) that older
/// <c>WsFederation</c>-era code assumed. The defaults below match that modern, unmapped shape — the one
/// every standards-conformant issuer actually emits by default. A consuming service on an identity
/// provider still emitting the legacy long-form shape (or one that has opted into
/// <c>MapInboundClaims = true</c>) overrides these to the <see cref="System.Security.Claims.ClaimTypes"/>
/// equivalents (see <c>SharedKernel.Security.Abstractions.SecurityClaimTypes.Email</c>/<c>.Role</c>,
/// now documented as legacy-shape reference constants for exactly this override scenario).
/// </para>
/// <para>
/// <c>AddSharedKernelSecurity</c> and <c>AddAzureB2CAuthentication</c> both source
/// <c>TokenValidationParameters.NameClaimType</c>/<c>RoleClaimType</c> from these same values —
/// ASP.NET Core's own claims machinery (<c>HttpContext.User.IsInRole(...)</c>,
/// <c>[Authorize(Roles = ...)]</c>) and <see cref="Mapping.OidcUserContext"/> must never read a
/// different claim type for the same concept.
/// </para>
/// </remarks>
public sealed class ClaimMappingOptions
{
    /// <summary>
    /// Gets or sets the claim type <see cref="Mapping.OidcUserContext.Email"/> is resolved from.
    /// </summary>
    /// <remarks>Defaults to <c>"email"</c> — the unmapped OIDC short-name claim.</remarks>
    [Required(AllowEmptyStrings = false)]
    public string EmailClaimType { get; set; } = "email";

    /// <summary>
    /// Gets or sets the claim type <see cref="Mapping.OidcUserContext.Username"/> is resolved from.
    /// </summary>
    /// <remarks>Defaults to <c>"name"</c> — the unmapped OIDC short-name claim.</remarks>
    [Required(AllowEmptyStrings = false)]
    public string NameClaimType { get; set; } = "name";

    /// <summary>
    /// Gets or sets the claim type <see cref="Mapping.OidcUserContext.Roles"/> is resolved from.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"roles"</c> — the unmapped OIDC short-name claim. Resolution is defensive to both
    /// real-world shapes: one <see cref="System.Security.Claims.Claim"/> per role, or a single claim
    /// whose value is a JSON array of roles (WO-057, P-366).
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string RoleClaimType { get; set; } = "roles";

    /// <summary>
    /// Gets or sets the claim type <see cref="Mapping.OidcUserContext.Permissions"/> is resolved from.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"scope"</c> — the standard OAuth2 scope claim. The claim value is a single
    /// space-delimited string, split into individual permission entries (WO-057, P-368).
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string PermissionClaimType { get; set; } = "scope";
}
