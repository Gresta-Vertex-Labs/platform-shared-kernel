namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Well-known EF Core 10 NAMED global query filter keys this domain installs, so distinct filters on
/// the same entity type can never silently replace one another.
/// </summary>
public static class PersistenceFilterNames
{
    /// <summary>The soft-delete filter installed by <c>SoftDeleteQueryFilterConvention</c>.</summary>
    public const string SoftDelete = "SoftDelete";

    /// <summary>The tenant-isolation filter installed by <c>MultiTenancy.TenantedDbContext</c>.</summary>
    public const string Tenant = "Tenant";
}
