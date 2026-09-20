using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Persistence.Security;

/// <summary>
/// Default <see cref="ICurrentTenantContext"/> that bridges <see cref="ITenantProvider"/>
/// (<c>12.Security.Abstractions</c>) into <c>06.Persistence</c>'s local tenant seam.
/// </summary>
/// <remarks>
/// Split from the former combined <c>IAuditActorContext</c>/<c>SecurityCurrentActorContext</c>
/// bridge — see <see cref="SecurityCurrentActorContext"/> for the actor half. Maps
/// <see cref="ITenantProvider.TenantId"/>'s own <see cref="Guid.Empty"/> "no tenant" sentinel onto
/// <see cref="ICurrentTenantContext.TenantId"/>'s <see langword="null"/> — both mean the identical
/// thing (no tenant resolved), just expressed in each layer's own idiom.
/// </remarks>
public sealed class SecurityCurrentTenantContext : ICurrentTenantContext
{
    private readonly ITenantProvider _tenantProvider;

    /// <summary>Initialises a new <see cref="SecurityCurrentTenantContext"/>.</summary>
    /// <param name="tenantProvider">The current scoped tenant identity.</param>
    public SecurityCurrentTenantContext(ITenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    /// <inheritdoc />
    public Guid? TenantId => _tenantProvider.TenantId == Guid.Empty ? null : _tenantProvider.TenantId;
}
