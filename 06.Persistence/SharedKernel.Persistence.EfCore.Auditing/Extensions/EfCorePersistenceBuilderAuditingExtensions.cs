using Microsoft.Extensions.Configuration;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;

namespace SharedKernel.Persistence.EfCore.Auditing.Extensions;

/// <summary>The audit ledger's seam on <see cref="EfCorePersistenceBuilder{TContext}"/>.</summary>
public static class EfCorePersistenceBuilderAuditingExtensions
{
    /// <summary>
    /// Opts the persistence registration in to the audit ledger (see
    /// <see cref="AuditLedgerServiceCollectionExtensions.AddSharedKernelAuditLedger"/> for what is registered).
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="configuration">The root configuration; <see cref="AuditLedgerOptions"/> binds from <see cref="AuditLedgerOptions.SectionName"/>.</param>
    /// <returns>The same builder.</returns>
    public static EfCorePersistenceBuilder<TContext> WithAuditTrail<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        IConfiguration configuration)
        where TContext : SharedKernelDbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSharedKernelAuditLedger(configuration);
        return builder;
    }

    /// <summary>Alias of <see cref="WithAuditTrail{TContext}"/> for the one-line <c>Use…</c> setup style.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="builder">The persistence builder.</param>
    /// <param name="configuration">The root configuration.</param>
    /// <returns>The same builder.</returns>
    public static EfCorePersistenceBuilder<TContext> UseAuditTrail<TContext>(
        this EfCorePersistenceBuilder<TContext> builder,
        IConfiguration configuration)
        where TContext : SharedKernelDbContext =>
        builder.WithAuditTrail(configuration);
}
