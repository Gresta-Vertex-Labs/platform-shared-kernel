using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>Builds real-PostgreSQL-backed <see cref="PgTestDbContext"/> instances for direct (non-DI) use.</summary>
internal static class PgTestDbContextFactory
{
    public static DbContextOptions<PgTestDbContext> BuildOptions(
        string connectionString,
        int? maxRetryCount = null,
        IEnumerable<IInterceptor>? providerLevelInterceptors = null,
        TimeSpan? maxRetryDelay = null)
    {
        var builder = new DbContextOptionsBuilder<PgTestDbContext>();
        builder.UsePostgreSQL(connectionString, maxRetryCount: maxRetryCount, maxRetryDelay: maxRetryDelay);

        // Provider-level (DbCommandInterceptor etc.) fault-injection interceptors — never
        // an ISaveChangesInterceptor, so they cannot go through PgTestDbContext's own
        // additionalInterceptors parameter (typed exactly to ISaveChangesInterceptor, matching
        // SharedKernelDbContext's real production constructor). Added directly at the options-builder
        // level here, composing alongside (never replacing) whatever SharedKernelDbContext's own
        // OnConfiguring adds later — EF Core supports multiple AddInterceptors calls. TEST-ONLY: no
        // production code path does this.
        if (providerLevelInterceptors is not null)
            builder.AddInterceptors(providerLevelInterceptors);

        return builder.Options;
    }

    public static PgTestDbContext Create(
        string connectionString,
        ICurrentActorContext actorContext,
        ICurrentTenantContext tenantContext,
        IClock? clock = null,
        IEnumerable<ISaveChangesInterceptor>? additionalInterceptors = null,
        IEnumerable<IDbUpdateExceptionClassifier>? exceptionClassifiers = null,
        int? maxRetryCount = null,
        IEnumerable<IInterceptor>? providerLevelInterceptors = null,
        TimeSpan? maxRetryDelay = null)
    {
        var options = BuildOptions(connectionString, maxRetryCount, providerLevelInterceptors, maxRetryDelay);
        clock ??= new SystemClock();

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new PgTestDbContext(
            options,
            new PersistenceContextDependencies(
                audit,
                softDelete,
                concurrency,
                additionalInterceptors,
                exceptionClassifiers: exceptionClassifiers));
        ctx.RefreshTenant(tenantContext);
        return ctx;
    }
}
