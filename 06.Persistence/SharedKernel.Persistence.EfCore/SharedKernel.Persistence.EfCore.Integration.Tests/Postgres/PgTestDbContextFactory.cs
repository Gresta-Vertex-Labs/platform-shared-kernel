using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Exceptions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

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
        // Retry is on by default in UsePostgres; these direct-construction tests keep it off unless a
        // test asks for a retry count.
        builder.UsePostgres(TestNpgsqlDataSources.Get(connectionString), o =>
        {
            o.MaxRetryCount = maxRetryCount ?? 0;
            if (maxRetryDelay is { } delay)
                o.MaxRetryDelay = delay;
        });

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
        IRequestContext actorContext,
        IRequestContext tenantContext,
        IClock? clock = null,
        IEnumerable<ISaveChangesInterceptor>? additionalInterceptors = null,
        IEnumerable<IDbUpdateExceptionClassifier>? exceptionClassifiers = null,
        int? maxRetryCount = null,
        IEnumerable<IInterceptor>? providerLevelInterceptors = null,
        TimeSpan? maxRetryDelay = null)
    {
        var options = BuildOptions(connectionString, maxRetryCount, providerLevelInterceptors, maxRetryDelay);
        clock ??= new SystemClock();

        var ctx = new PgTestDbContext(
            options,
            new PersistenceContextDependencies(
                clock,
                actorContext,
                serviceName: "system",
                defaultDomainEventDispatcher: null,
                additionalInterceptors,
                modelConventionFactories: null,
                modelConfigurators: null,
                optionsExtensions: null,
                exceptionClassifiers ?? [new PostgresDbUpdateExceptionClassifier()],
                keyGenerator: null,
                loggerFactory: null));
        // Actor identity from actorContext, tenant from tenantContext — the two roles the former
        // ICurrentActorContext/ICurrentTenantContext pair played, now one IRequestContext.
        ctx.RefreshRequestContext(ReferenceEquals(actorContext, tenantContext)
            ? actorContext
            : new ActorWithTenantContext(actorContext, tenantContext));
        return ctx;
    }
}

/// <summary>Combines one context's caller identity with another context's tenant.</summary>
internal sealed class ActorWithTenantContext(IRequestContext actor, IRequestContext tenant) : IRequestContext
{
    public bool IsAuthenticated => actor.IsAuthenticated;
    public string? UserId => actor.UserId;
    public Guid? TenantId => tenant.TenantId;
    public ActorKind ActorKind => actor.ActorKind;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        actor.HasPermissionAsync(permission, cancellationToken);
}
