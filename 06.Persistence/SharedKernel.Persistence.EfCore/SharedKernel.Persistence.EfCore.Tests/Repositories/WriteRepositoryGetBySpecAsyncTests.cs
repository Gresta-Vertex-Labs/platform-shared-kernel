using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

/// <summary>
/// IRepository.FirstOrDefaultAsync write-side tracked fetch tests.
/// </summary>
public sealed class WriteRepositoryGetBySpecAsyncTests
{
    private sealed class TestWriteRepo : EfRepository<TestAggregate, TestId>
    {
        public TestWriteRepo(TestDbContext ctx) : base(ctx, new SpecificationEvaluator<TestAggregate>()) { }
    }

    private sealed class ByNameSpec : Specification<TestAggregate>
    {
        public ByNameSpec(string name)
        {
            AddCriteria(e => e.Name == name);
        }
    }

    private sealed class ByNameNoTrackSpec : Specification<TestAggregate>
    {
        public ByNameNoTrackSpec(string name)
        {
            AddCriteria(e => e.Name == name);
        }
    }

    [Fact]
    public async Task GetBySpecAsync_Returns_Tracked_Entity()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "TrackedTest", new SystemClock()));
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var repo = new TestWriteRepo(ctx);
        var spec = new ByNameSpec("TrackedTest");

        // Act
        var entity = await repo.FirstOrDefaultAsync(spec);

        // Assert — entity is tracked
        entity.Should().NotBeNull();
        ctx.Entry(entity!).State.Should().NotBe(EntityState.Detached,
            "write-side FirstOrDefaultAsync must return a tracked entity");
    }

    [Fact]
    public async Task GetBySpecAsync_NoMatch_Returns_Null()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestWriteRepo(ctx);
        var spec = new ByNameSpec("DoesNotExist");

        // Act
        var entity = await repo.FirstOrDefaultAsync(spec);

        // Assert
        entity.Should().BeNull();
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ThroughReadContract_Returns_Detached_Entity()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "NoTrackTest", new SystemClock()));
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var repo = new TestWriteRepo(ctx);
        var spec = new ByNameNoTrackSpec("NoTrackTest");

        // Act — the same object answers untracked through IReadRepository.
        var entity = await ((SharedKernel.Persistence.Abstractions.Repositories.IReadRepository<TestAggregate, TestId>)repo)
            .FirstOrDefaultAsync(spec);

        // Assert
        entity.Should().NotBeNull();
        ctx.Entry(entity!).State.Should().Be(EntityState.Detached);
    }

    [Fact]
    public void IRepository_Declares_GetBySpecAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("FirstOrDefaultAsync");
        method.Should().NotBeNull("IRepository<TAggregate,TId> must declare FirstOrDefaultAsync");
    }
}
