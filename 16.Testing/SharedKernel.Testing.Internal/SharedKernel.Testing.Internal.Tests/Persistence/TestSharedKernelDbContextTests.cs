using Microsoft.EntityFrameworkCore;
using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class TestSharedKernelDbContextTests
{
    [Fact]
    public async Task EnsureCreatedAsync_CreatesSchema_WithoutMigrations()
    {
        var options = TestSharedKernelDbContext.BuildOptions<TestPersistenceDbContext>(
            $"ensure-created-{Guid.NewGuid():N}");

        await using var context = new TestPersistenceDbContext(options);
        await context.EnsureCreatedAsync();

        var canConnect = await context.Database.CanConnectAsync();
        Assert.True(canConnect);
    }

    [Fact]
    public async Task SaveChangesAsync_PopulatesAuditFields_ViaInterceptor()
    {
        var options = TestSharedKernelDbContext.BuildOptions<TestPersistenceDbContext>(
            $"audit-interceptor-{Guid.NewGuid():N}");

        await using var context = new TestPersistenceDbContext(options);
        await context.EnsureCreatedAsync();

        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());
        await context.AddAsync(order);
        await context.SaveChangesAsync();

        var entry = context.Entry(order);
        Assert.Equal(EntityState.Unchanged, entry.State);
    }

    [Fact]
    public void BuildOptions_NullOrWhitespaceDatabaseName_Throws() =>
        Assert.Throws<ArgumentException>(() => TestSharedKernelDbContext.BuildOptions<TestPersistenceDbContext>(" "));
}
