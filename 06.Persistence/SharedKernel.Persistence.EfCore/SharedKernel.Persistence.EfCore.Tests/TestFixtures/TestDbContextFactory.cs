using SharedKernel.Application.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
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
/// <see cref="AuditInterceptor"/> takes the caller (<see cref="IRequestContext"/>), the clock and an
/// optional service name; a context carries that caller as its <c>RequestContext</c>, which also
/// supplies the tenant for <see cref="TenantedDbContext"/>. <see cref="FakeAuditActorContext"/>
/// (<c>16.Testing</c>) is the default caller.
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
        IRequestContext? actorContext = null,
        IClock? clock = null,
        string? serviceName = null)
    {
        actorContext ??= CreateAuthenticatedActorContext(Guid.NewGuid());
        clock ??= CreateClock(DateTimeOffset.UtcNow);
        return CreateTestDbContextWithActor(actorContext, clock, serviceName);
    }

    /// <summary>Creates a TestDbContext with an explicit actor context (for audit/service-name tests).</summary>
    public static TestDbContext CreateTestDbContextWithActor(
        IRequestContext actorContext,
        IClock clock,
        string? serviceName = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var audit = PersistenceContextDependencies.Create(actorContext, clock, serviceName: serviceName ?? "system");

        var ctx = new TestDbContext(options, audit);
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

        var audit = PersistenceContextDependencies.Create(actorContext, clock);

        var ctx = new TenantedTestDbContext(options, audit);
        ctx.RefreshRequestContext(actorContext);
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

        var audit = PersistenceContextDependencies.Create(actorContext, clock);

        var ctx = new SoftDeletableTenantedDbContext(options, audit);
        ctx.RefreshRequestContext(actorContext);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    /// <summary>
    /// Creates an authenticated <see cref="FakeAuditActorContext"/> —
    /// <see cref="IRequestContext.UserId"/> mirrors the old <c>IUserContext.SubjectId</c>
    /// format ("D"-formatted GUID string). <paramref name="tenantId"/> defaults to
    /// <see langword="null"/> (no tenant resolved) — pass one explicitly for multi-tenant fixtures.
    /// </summary>
    public static FakeAuditActorContext CreateAuthenticatedActorContext(Guid userId, Guid? tenantId = null)
        => new(userId.ToString("D"), tenantId);

    /// Returns the unauthenticated-fallback <see cref="IRequestContext"/> — the SAME instance
    /// (<see cref="AnonymousRequestContext.Instance"/>) <c>EfCorePersistenceBuilder.Build()</c> registers
    /// by default. Its <see cref="IRequestContext.UserId"/> is <see langword="null"/>, so audit columns
    /// fall back to the service name passed to <see cref="CreateTestDbContext"/>.
    /// </summary>
    public static IRequestContext CreateUnauthenticatedActorContext() => AnonymousRequestContext.Instance;

    public static IClock CreateClock(DateTimeOffset now) => new FakeClock(now);
}
