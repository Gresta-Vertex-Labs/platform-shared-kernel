using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

/// <summary>
/// Factory helpers for creating in-memory SQLite test DbContext instances.
/// </summary>
/// <remarks>
/// <see cref="AuditInterceptor"/>/<see cref="SoftDeleteInterceptor"/> take
/// <see cref="ICurrentActorContext"/> + <see cref="IClock"/>, and
/// <see cref="TenantedDbContext"/> takes a separate <see cref="ICurrentTenantContext"/> — the former
/// combined <c>IAuditActorContext</c> is retired. <see cref="FakeAuditActorContext"/>
/// (<c>16.Testing</c>) implements both interfaces on one object, so every factory method below still
/// takes/returns a single fake instance for both roles, exactly as before the seam split.
/// </remarks>
internal static class TestDbContextFactory
{
    /// <summary>Creates a default IOptions&lt;PersistenceServiceOptions&gt; with ServiceName = "system".</summary>
    public static IOptions<PersistenceServiceOptions> DefaultServiceOptions()
        => MicrosoftOptions.Create(new PersistenceServiceOptions());

    /// <summary>Creates a custom IOptions&lt;PersistenceServiceOptions&gt; with the given service name.</summary>
    public static IOptions<PersistenceServiceOptions> ServiceOptions(string serviceName)
        => MicrosoftOptions.Create(new PersistenceServiceOptions { ServiceName = serviceName });

    public static TestDbContext CreateTestDbContext(
        ICurrentActorContext? actorContext = null,
        IClock? clock = null)
    {
        actorContext ??= CreateAuthenticatedActorContext(Guid.NewGuid());
        clock ??= CreateClock(DateTimeOffset.UtcNow);
        return CreateTestDbContextWithActor(actorContext, clock);
    }

    /// <summary>Creates a TestDbContext with an explicit actor context (for audit/service-name tests).</summary>
    public static TestDbContext CreateTestDbContextWithActor(
        ICurrentActorContext actorContext,
        IClock clock)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options;

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new TestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static TenantedTestDbContext CreateTenantedDbContext(
        Guid? tenantId = null,
        FakeAuditActorContext? actorContext = null,
        IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options;

        clock ??= CreateClock(DateTimeOffset.UtcNow);
        actorContext ??= CreateAuthenticatedActorContext(Guid.NewGuid(), tenantId ?? Guid.NewGuid());

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new TenantedTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
        ctx.RefreshTenant(actorContext);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static SoftDeletableTenantedDbContext CreateSoftDeletableTenantedDbContext(
        Guid? tenantId = null,
        FakeAuditActorContext? actorContext = null,
        IClock? clock = null)
    {
        // Use an explicit open SqliteConnection so the in-memory database persists
        // for the full lifetime of the test even when EF Core cycles its connections.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SoftDeletableTenantedDbContext>()
            .UseSqlite(connection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options;

        clock ??= CreateClock(DateTimeOffset.UtcNow);
        actorContext ??= CreateAuthenticatedActorContext(Guid.NewGuid(), tenantId ?? Guid.NewGuid());

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new SoftDeletableTenantedDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
        ctx.RefreshTenant(actorContext);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    /// <summary>
    /// Creates an authenticated <see cref="FakeAuditActorContext"/> —
    /// <see cref="ICurrentActorContext.ActorId"/> mirrors the old <c>IUserContext.SubjectId</c>
    /// format ("D"-formatted GUID string). <paramref name="tenantId"/> defaults to
    /// <see langword="null"/> (no tenant resolved) — pass one explicitly for multi-tenant fixtures.
    /// </summary>
    public static FakeAuditActorContext CreateAuthenticatedActorContext(Guid userId, Guid? tenantId = null)
        => new(userId.ToString("D"), tenantId);

    /// <summary>
    /// Creates the unauthenticated-fallback <see cref="ICurrentActorContext"/> — the SAME production
    /// type (<see cref="AnonymousActorContext"/>) <c>EfCorePersistenceBuilder.Build()</c> registers by
    /// default, so its <see cref="ICurrentActorContext.ActorId"/> genuinely falls back to
    /// <see cref="PersistenceServiceOptions.ServiceName"/> exactly as production does.
    /// </summary>
    public static ICurrentActorContext CreateUnauthenticatedActorContext(string? serviceName = null)
        => new AnonymousActorContext(serviceName is null ? DefaultServiceOptions() : ServiceOptions(serviceName));

    public static IClock CreateClock(DateTimeOffset now) => new FakeClock(now);
}
