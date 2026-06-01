namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Provides the current tenant identity for multi-tenant EF Core query filtering.
/// </summary>
/// <remarks>
/// <para>
/// Defined in <c>SharedKernel.Persistence.EfCore</c> (not in Abstractions) because tenant
/// isolation is a DbContext-level concern — <see cref="TenantedDbContext"/> captures a reference
/// to this service and uses it at query execution time, not at startup.
/// </para>
/// <para>
/// The concrete implementation lives in <c>13.ServiceDefaults.MultiTenancy</c>.
/// <c>EfCorePersistenceBuilder.WithMultiTenancy()</c> registers a no-op placeholder that returns
/// <see langword="null"/> — the consuming service must override this with a real implementation
/// that resolves the tenant from an HTTP header, claim, or similar mechanism.
/// </para>
/// </remarks>
public interface ICurrentTenantService
{
    /// <summary>
    /// Gets the unique identifier of the current request's tenant, or <see langword="null"/>
    /// when no tenant context has been established (e.g., during background jobs or migrations).
    /// </summary>
    Guid? TenantId { get; }
}
