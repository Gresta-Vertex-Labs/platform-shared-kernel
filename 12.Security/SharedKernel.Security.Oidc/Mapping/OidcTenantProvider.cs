using System.Security.Claims;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Abstractions.Claims;

namespace SharedKernel.Security.Oidc.Mapping;

/// <summary>
/// Resolves <see cref="ITenantProvider.TenantId"/> from the <c>tenant_id</c> claim on the current
/// <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// <para>
/// Constructed per-request from <c>IHttpContextAccessor.HttpContext?.User</c>.
/// Never cached across requests.
/// </para>
/// <para>
/// Returns <see cref="Guid.Empty"/> when the claim is absent or cannot be parsed — never throws.
/// Callers must handle <see cref="Guid.Empty"/> (unauthenticated or system-level requests).
/// </para>
/// <para>
/// AOT note: <c>Guid.TryParse</c> is AOT-safe (BCL).
/// </para>
/// </remarks>
public sealed class OidcTenantProvider : ITenantProvider
{
    /// <inheritdoc/>
    public Guid TenantId { get; }

    /// <summary>
    /// Initialises a new <see cref="OidcTenantProvider"/> from the supplied <paramref name="principal"/>.
    /// </summary>
    /// <param name="principal">
    /// The <see cref="ClaimsPrincipal"/> representing the current request's identity.
    /// Must not be <see langword="null"/>.
    /// </param>
    public OidcTenantProvider(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var raw = principal.FindFirstValue(SecurityClaimTypes.TenantId);

        TenantId = raw is not null && Guid.TryParse(raw, out var parsed)
            ? parsed
            : Guid.Empty;
    }
}
