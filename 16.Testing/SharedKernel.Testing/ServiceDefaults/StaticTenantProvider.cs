using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Testing.ServiceDefaults;

/// <summary>
/// Trivial deterministic test double for <see cref="ITenantProvider"/> with a fixed tenant id.
/// </summary>
/// <remarks>
/// Implements a <c>12.Security</c>-owned interface directly — useful for exercising tenant-scoped
/// code without standing up <c>AmbientTenantProvider</c> plus middleware plus an HTTP context.
/// </remarks>
public sealed class StaticTenantProvider : ITenantProvider
{
    /// <summary>
    /// Initialises a new <see cref="StaticTenantProvider"/> with the given fixed
    /// <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="tenantId">
    /// The fixed tenant id. Pass <see cref="Guid.Empty"/> to simulate the no-tenant case.
    /// </param>
    public StaticTenantProvider(Guid tenantId) => TenantId = tenantId;

    /// <inheritdoc />
    public Guid TenantId { get; }
}
