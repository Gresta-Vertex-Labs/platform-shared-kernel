using Microsoft.EntityFrameworkCore;
using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class EfContextExtensionsTests
{
    [Fact]
    public async Task DetachAll_DetachesEveryTrackedEntity()
    {
        var dbName = $"detach-all-{Guid.NewGuid():N}";
        var options = TestSharedKernelDbContext.BuildOptions<TestPersistenceDbContext>(dbName);

        await using var context = new TestPersistenceDbContext(options);
        await context.EnsureCreatedAsync();

        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());
        await context.AddAsync(order);

        context.DetachAll();

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ReloadAsync_AfterPersist_ReturnsFreshCopy()
    {
        var dbName = $"reload-async-{Guid.NewGuid():N}";
        var options = TestSharedKernelDbContext.BuildOptions<TestPersistenceDbContext>(dbName);

        await using var context = new TestPersistenceDbContext(options);
        await context.EnsureCreatedAsync();

        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());
        await context.AddAsync(order);
        await context.SaveChangesAsync();

        var reloaded = await context.ReloadAsync(order);

        Assert.NotNull(reloaded);
        Assert.Equal(order.Id, reloaded!.Id);
    }

    [Fact]
    public async Task ReloadAsync_NonExistentEntity_ReturnsNull()
    {
        var dbName = $"reload-async-missing-{Guid.NewGuid():N}";
        var options = TestSharedKernelDbContext.BuildOptions<TestPersistenceDbContext>(dbName);

        await using var context = new TestPersistenceDbContext(options);
        await context.EnsureCreatedAsync();

        var transientOrder = new TestOrder(Guid.NewGuid(), "Ghost", 0, new SharedKernel.Testing.Clocks.FakeClock());
        await context.AddAsync(transientOrder);

        // Detach without saving — the row never made it to the database.
        context.DetachAll();

        var reloaded = await context.ReloadAsync(transientOrder);

        Assert.Null(reloaded);
    }

    [Fact]
    public void DetachAll_NullContext_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((DbContext)null!).DetachAll());
}
