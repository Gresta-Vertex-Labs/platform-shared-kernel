using Microsoft.EntityFrameworkCore;

namespace SharedKernel.Persistence.EfCore.Extensibility;

/// <summary>
/// Extension point letting a sibling package (e.g. <c>SharedKernel.Persistence.EfCore.Auditing</c>)
/// apply extra <c>IEntityTypeConfiguration&lt;T&gt;</c> configurations that live outside the
/// downstream <c>DbContext</c>'s own assembly, so <c>ApplyConfigurationsFromAssembly</c> never finds
/// them on its own.
/// </summary>
/// <remarks>
/// Implementations are resolved from DI as <c>IEnumerable{IPersistenceModelConfigurator}</c>
/// and invoked once per model build, inside <see cref="Context.SharedKernelDbContext.OnModelCreating"/>,
/// after the assembly scan and before <see cref="Context.TenantedDbContext"/>'s tenant filters.
/// Replaces the previous single-purpose <c>AuditTrailFeatureMarker</c> DI-marker-type mechanism with
/// a general-purpose one any future opt-in capability can reuse.
/// </remarks>
internal interface IPersistenceModelConfigurator
{
    /// <summary>Applies this configurator's entity configuration(s) to <paramref name="modelBuilder"/>.</summary>
    /// <param name="modelBuilder">The builder used to construct the model for the current context.</param>
    void Configure(ModelBuilder modelBuilder);
}
