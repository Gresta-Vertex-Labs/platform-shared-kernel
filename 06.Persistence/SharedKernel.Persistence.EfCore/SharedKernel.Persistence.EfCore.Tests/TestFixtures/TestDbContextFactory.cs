using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

/// <summary>
/// Factory helpers for creating in-memory SQLite test DbContext instances.
/// </summary>
internal static class TestDbContextFactory
{
    public static TestDbContext CreateTestDbContext(
        IUserContext? userContext = null,
        IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        userContext ??= CreateUserContext("test-user");
        clock ??= CreateClock(DateTimeOffset.UtcNow);

        var audit = new AuditInterceptor(userContext, clock);
        var softDelete = new SoftDeleteInterceptor(userContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new TestDbContext(options, audit, softDelete, concurrency);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static TenantedTestDbContext CreateTenantedDbContext(
        ICurrentTenantService? tenantService = null,
        IUserContext? userContext = null,
        IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        userContext ??= CreateUserContext("test-user");
        clock ??= CreateClock(DateTimeOffset.UtcNow);
        tenantService ??= CreateTenantService(null);

        var audit = new AuditInterceptor(userContext, clock);
        var softDelete = new SoftDeleteInterceptor(userContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        var ctx = new TenantedTestDbContext(options, audit, softDelete, concurrency, tenantService);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static IUserContext CreateUserContext(string userId)
    {
        var mock = Substitute.For<IUserContext>();
        mock.UserId.Returns(userId);
        return mock;
    }

    public static IClock CreateClock(DateTimeOffset now)
    {
        var mock = Substitute.For<IClock>();
        mock.UtcNow.Returns(now);
        return mock;
    }

    public static ICurrentTenantService CreateTenantService(Guid? tenantId)
    {
        var mock = Substitute.For<ICurrentTenantService>();
        mock.TenantId.Returns(tenantId);
        return mock;
    }
}
