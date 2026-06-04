using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

/// <summary>
/// Factory helpers for creating in-memory SQLite test DbContext instances.
/// </summary>
internal static class TestDbContextFactory
{
    /// <summary>Creates a default IOptions&lt;PersistenceServiceOptions&gt; with ServiceName = "system".</summary>
    public static IOptions<PersistenceServiceOptions> DefaultServiceOptions()
        => MicrosoftOptions.Create(new PersistenceServiceOptions());

    /// <summary>Creates a custom IOptions&lt;PersistenceServiceOptions&gt; with the given service name.</summary>
    public static IOptions<PersistenceServiceOptions> ServiceOptions(string serviceName)
        => MicrosoftOptions.Create(new PersistenceServiceOptions { ServiceName = serviceName });

    public static TestDbContext CreateTestDbContext(
        IUserContext? userContext = null,
        IClock? clock = null)
    {
        userContext ??= CreateAuthenticatedUserContext(Guid.NewGuid());
        clock ??= CreateClock(DateTimeOffset.UtcNow);
        return CreateTestDbContextWithOptions(userContext, clock, DefaultServiceOptions());
    }

    /// <summary>Creates a TestDbContext with explicit PersistenceServiceOptions (for service-name tests).</summary>
    public static TestDbContext CreateTestDbContextWithOptions(
        IUserContext userContext,
        IClock clock,
        IOptions<PersistenceServiceOptions> serviceOptions)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        var audit = new AuditInterceptor(userContext, clock, serviceOptions);
        var softDelete = new SoftDeleteInterceptor(userContext, clock, serviceOptions);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new TestDbContext(options, audit, softDelete, concurrency);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static TenantedTestDbContext CreateTenantedDbContext(
        ITenantProvider? tenantProvider = null,
        IUserContext? userContext = null,
        IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        userContext ??= CreateAuthenticatedUserContext(Guid.NewGuid());
        clock ??= CreateClock(DateTimeOffset.UtcNow);
        tenantProvider ??= CreateTenantProvider(Guid.Empty);

        var audit = new AuditInterceptor(userContext, clock, DefaultServiceOptions());
        var softDelete = new SoftDeleteInterceptor(userContext, clock, DefaultServiceOptions());
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new TenantedTestDbContext(options, audit, softDelete, concurrency, tenantProvider);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static SoftDeletableTenantedDbContext CreateSoftDeletableTenantedDbContext(
        ITenantProvider? tenantProvider = null,
        IUserContext? userContext = null,
        IClock? clock = null)
    {
        // Use an explicit open SqliteConnection so the in-memory database persists
        // for the full lifetime of the test even when EF Core cycles its connections.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SoftDeletableTenantedDbContext>()
            .UseSqlite(connection)
            .Options;

        userContext ??= CreateAuthenticatedUserContext(Guid.NewGuid());
        clock ??= CreateClock(DateTimeOffset.UtcNow);
        tenantProvider ??= CreateTenantProvider(Guid.Empty);

        var audit = new AuditInterceptor(userContext, clock, DefaultServiceOptions());
        var softDelete = new SoftDeleteInterceptor(userContext, clock, DefaultServiceOptions());
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new SoftDeletableTenantedDbContext(options, audit, softDelete, concurrency, tenantProvider);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    /// <summary>Creates an authenticated IUserContext mock with the specified UserId.</summary>
    public static IUserContext CreateAuthenticatedUserContext(Guid userId)
    {
        var mock = Substitute.For<IUserContext>();
        mock.UserId.Returns(userId);
        mock.IsAuthenticated.Returns(true);
        mock.Email.Returns((string?)null);
        mock.Username.Returns((string?)null);
        mock.Roles.Returns([]);
        mock.Claims.Returns(new Dictionary<string, string>());
        mock.HasRole(Arg.Any<string>()).Returns(false);
        return mock;
    }

    /// <summary>Creates an unauthenticated IUserContext mock (produces "system" audit values).</summary>
    public static IUserContext CreateUnauthenticatedUserContext()
    {
        var mock = Substitute.For<IUserContext>();
        mock.UserId.Returns(Guid.Empty);
        mock.IsAuthenticated.Returns(false);
        mock.Email.Returns((string?)null);
        mock.Username.Returns((string?)null);
        mock.Roles.Returns([]);
        mock.Claims.Returns(new Dictionary<string, string>());
        mock.HasRole(Arg.Any<string>()).Returns(false);
        return mock;
    }

    public static IClock CreateClock(DateTimeOffset now)
    {
        var mock = Substitute.For<IClock>();
        mock.UtcNow.Returns(now);
        return mock;
    }

    public static ITenantProvider CreateTenantProvider(Guid tenantId)
    {
        var mock = Substitute.For<ITenantProvider>();
        mock.TenantId.Returns(tenantId);
        return mock;
    }
}
