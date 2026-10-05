using SharedKernel.Persistence;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence;

/// <summary>The audit ledger's seam on <see cref="EfCorePersistenceBuilder{TContext}"/>.</summary>
public static class EfCorePersistenceBuilderAuditingExtensions
{
    /// <summary>
    /// Opts the persistence registration in to the audit ledger (see
    /// <see cref="AuditLedgerServiceCollectionExtensions.AddSharedKernelAuditLedger"/> for what is registered).
    /// <see cref="AuditLedgerOptions"/> binds from <see cref="AuditLedgerOptions.SectionName"/> of the configuration
    /// given to <c>AddSharedKernelPostgres</c>, and is validated at startup.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="builder">The persistence builder.</param>
    /// <returns>The same builder.</returns>
    public static EfCorePersistenceBuilder<TContext> UseAuditTrail<TContext>(this EfCorePersistenceBuilder<TContext> builder)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuration = builder.Configuration
            ?? throw new InvalidOperationException("UseAuditTrail() needs the configuration passed to AddSharedKernelPostgres.");
        builder.Services.AddSharedKernelAuditLedger(configuration);
        return builder;
    }
}
