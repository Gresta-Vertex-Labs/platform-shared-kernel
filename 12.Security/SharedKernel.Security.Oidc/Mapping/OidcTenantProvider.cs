using System.Security.Claims;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Logging;

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
/// Callers must handle <see cref="Guid.Empty"/> (unauthenticated or system-level requests). Unaffected
/// by WO-057 (P-366) — <c>"tenant_id"</c> is already a short-name claim.
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
    /// <param name="logger">
    /// An optional logger for structured security-audit events (tenant-claim resolution failure). No raw
    /// claim values are ever logged. <see langword="null"/> is accepted for direct, non-DI construction.
    /// </param>
    public OidcTenantProvider(ClaimsPrincipal principal, ILogger<OidcTenantProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var raw = principal.FindFirstValue(SecurityClaimTypes.TenantId);

        if (raw is not null && Guid.TryParse(raw, out var parsed))
        {
            TenantId = parsed;
        }
        else
        {
            TenantId = Guid.Empty;

            // Only log when the principal is actually authenticated — an unauthenticated/anonymous
            // request resolving to Guid.Empty is completely expected and not a signal worth a log line.
            if (logger is not null && (principal.Identity?.IsAuthenticated ?? false))
            {
                SecurityLogEvents.TenantClaimResolutionFailed(logger, SecurityClaimTypes.TenantId);
            }
        }
    }
}
