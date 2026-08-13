using System.Security.Claims;

namespace SharedKernel.Security.Abstractions.Claims;

/// <summary>
/// Well-known JWT/OIDC claim type names used throughout the security domain.
/// </summary>
/// <remarks>
/// All claim type lookups in both implementation and consumer code must reference these
/// constants — never use raw string literals. This ensures a single point of change if
/// a claim name is remapped (e.g. by an identity provider's token customisation policy).
/// </remarks>
public static class SecurityClaimTypes
{
    /// <summary>
    /// The subject claim that uniquely identifies the authenticated user.
    /// Maps to the standard OIDC <c>sub</c> claim.
    /// </summary>
    /// <remarks>Unaffected by WO-057 (P-366) — <c>"sub"</c> is already a short name.</remarks>
    public const string UserId = "sub";

    /// <summary>
    /// The claim that carries the tenant identifier for multi-tenant requests.
    /// </summary>
    /// <remarks>Unaffected by WO-057 (P-366) — <c>"tenant_id"</c> is already a short name.</remarks>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// The legacy, long-form email address claim type: <see cref="ClaimTypes.Email"/>
    /// (<c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress</c>).
    /// </summary>
    /// <remarks>
    /// <b>LEGACY-SHAPE REFERENCE CONSTANT ONLY, as of WO-057 (P-366).</b> The active lookup key
    /// <c>OidcUserContext</c> uses at runtime is <c>SecurityOptions.ClaimMapping.EmailClaimType</c>
    /// (<c>SharedKernel.Security.Oidc</c>), whose short-name default (<c>"email"</c>) matches the
    /// unmapped claim shape every standards-conformant OIDC issuer emits by default against a .NET 8+
    /// <c>JwtBearerHandler</c> (<c>MapInboundClaims = false</c>). This constant remains useful for a
    /// consuming service on an identity provider still emitting the legacy long-form shape (or one that
    /// has opted into <c>MapInboundClaims = true</c>) to configure as an explicit
    /// <c>ClaimMapping.EmailClaimType</c> override.
    /// </remarks>
    public const string Email = ClaimTypes.Email;

    /// <summary>
    /// The legacy, long-form role claim type: <see cref="ClaimTypes.Role"/>
    /// (<c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>).
    /// </summary>
    /// <remarks>
    /// <b>LEGACY-SHAPE REFERENCE CONSTANT ONLY, as of WO-057 (P-366).</b> The active lookup key
    /// <c>OidcUserContext</c> uses at runtime is <c>SecurityOptions.ClaimMapping.RoleClaimType</c>
    /// (<c>SharedKernel.Security.Oidc</c>), whose short-name default (<c>"roles"</c>) matches the
    /// unmapped claim shape every standards-conformant OIDC issuer emits by default. See <see cref="Email"/>
    /// for the full rationale — the same reasoning applies here.
    /// </remarks>
    public const string Role = ClaimTypes.Role;
}
