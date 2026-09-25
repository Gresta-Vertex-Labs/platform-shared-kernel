using Microsoft.EntityFrameworkCore;
using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Persistence;

public sealed class PersistenceTestHelpersTests : IDisposable
{
    private readonly TestPersistenceDbContext _context;

    public PersistenceTestHelpersTests()
    {
        var options = TestSharedKernelDbContext.BuildOptions<TestPersistenceDbContext>(
            $"persistence-test-helpers-{Guid.NewGuid():N}");
        _context = new TestPersistenceDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AssertEntityTracked_TrackedEntity_DoesNotThrow()
    {
        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());
        await _context.AddAsync(order);

        PersistenceTestHelpers.AssertEntityTracked(_context, order);
    }

    [Fact]
    public void AssertEntityTracked_DetachedEntity_Throws()
    {
        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());

        Assert.Throws<InvalidOperationException>(() => PersistenceTestHelpers.AssertEntityTracked(_context, order));
    }

    [Fact]
    public void AssertEntityNotTracked_DetachedEntity_DoesNotThrow()
    {
        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());

        PersistenceTestHelpers.AssertEntityNotTracked(_context, order);
    }

    [Fact]
    public async Task AssertEntityNotTracked_TrackedEntity_Throws()
    {
        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());
        await _context.AddAsync(order);

        Assert.Throws<InvalidOperationException>(() => PersistenceTestHelpers.AssertEntityNotTracked(_context, order));
    }

    [Fact]
    public void AssertEntityTracked_NullContext_Throws()
    {
        var order = new TestOrder(Guid.NewGuid(), "Acme", 10, new SharedKernel.Testing.Clocks.FakeClock());
        Assert.Throws<ArgumentNullException>(() => PersistenceTestHelpers.AssertEntityTracked(null!, order));
    }

    public void Dispose() => _context.Dispose();
}
