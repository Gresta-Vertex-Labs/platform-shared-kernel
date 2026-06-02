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
    public const string UserId = "sub";

    /// <summary>
    /// The claim that carries the tenant identifier for multi-tenant requests.
    /// </summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// The email address claim. Delegates to <see cref="ClaimTypes.Email"/> (<c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress</c>).
    /// </summary>
    public const string Email = ClaimTypes.Email;

    /// <summary>
    /// The role claim. Delegates to <see cref="ClaimTypes.Role"/> (<c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>).
    /// </summary>
    public const string Role = ClaimTypes.Role;
}
