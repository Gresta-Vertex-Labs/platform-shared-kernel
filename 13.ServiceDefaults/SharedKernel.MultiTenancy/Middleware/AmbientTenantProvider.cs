using SharedKernel.Security.Abstractions;

namespace SharedKernel.MultiTenancy.Middleware;

/// <summary>
/// The scoped <see cref="ITenantProvider"/> implementation populated by
/// <see cref="TenantResolutionMiddleware"/>.
/// </summary>
/// <remarks>
/// Scoped per HTTP request. Defaults to <see cref="Guid.Empty"/> until
/// <see cref="TenantResolutionMiddleware"/> runs. <see cref="TenantId"/> is set exactly once per
/// request — only by <see cref="TenantResolutionMiddleware"/>; no other component may set it.
/// It replaces the authentication packages' default <c>UserContextTenantProvider</c>; the credential's tenant
/// is read by <c>ClaimTenantResolutionStrategy</c> instead, so header, claim and database resolution compose in
/// priority order.
/// </remarks>
public sealed class AmbientTenantProvider : ITenantProvider
{
    /// <inheritdoc/>
    public Guid TenantId { get; private set; } = Guid.Empty;

    /// <summary>
    /// Sets <see cref="TenantId"/>. Called exactly once per request, by
    /// <see cref="TenantResolutionMiddleware"/> only.
    /// </summary>
    /// <param name="tenantId">The resolved tenant identifier.</param>
    internal void SetTenantId(Guid tenantId) => TenantId = tenantId;
}
