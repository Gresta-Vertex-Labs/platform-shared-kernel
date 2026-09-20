using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Default <see cref="ICurrentTenantContext"/> registered by
/// <c>EfCorePersistenceBuilder.WithMultiTenancy()</c> when the consuming service has not registered
/// its own — always reports <see langword="null"/> (no tenant resolved), which the tenant global
/// query filter and the tenant write guard both treat as "match/allow nothing".
/// </summary>
/// <remarks>
/// A consuming service bridges this seam to its real tenant source, typically via
/// <c>13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence</c>'s
/// <c>SecurityCurrentTenantContext</c>, which reads <c>12.Security.Abstractions</c>'s
/// <c>ITenantProvider</c> (last-registration-wins).
/// </remarks>
public sealed class NullCurrentTenantContext : ICurrentTenantContext
{
    /// <summary>
    /// A shared, stateless singleton instance — usable anywhere a fail-closed
    /// <see cref="ICurrentTenantContext"/> is needed without a DI resolution (e.g.
    /// <see cref="TenantedDbContext"/>'s own field initializer and its <c>Dispose</c>/
    /// <c>DisposeAsync</c> reset-to-fail-closed logic).
    /// </summary>
    public static readonly NullCurrentTenantContext Instance = new();

    /// <inheritdoc />
    public Guid? TenantId => null;
}
