using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// <see cref="IPersistenceModelConfigurator"/> that applies <see cref="AuditRecordEntityConfiguration"/>
/// to the model.
/// </summary>
/// <remarks>
/// Registered as a singleton by <c>EfCorePersistenceBuilder.WithAuditTrail()</c>, resolved
/// by <c>SharedKernel.Persistence.EfCore</c>'s <c>SharedKernelDbContext</c> via
/// <c>IEnumerable&lt;IPersistenceModelConfigurator&gt;</c> — <c>AuditRecordEntityConfiguration</c>
/// lives in THIS assembly, not the downstream concrete context's, so
/// <c>ApplyConfigurationsFromAssembly</c> never discovers it on its own. Replaces the former
/// <c>AuditTrailFeatureMarker</c> DI-marker-type mechanism.
/// </remarks>
public sealed class AuditRecordModelConfigurator : IPersistenceModelConfigurator
{
    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new AuditRecordEntityConfiguration());
}
