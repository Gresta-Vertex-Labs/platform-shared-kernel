using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using System.Linq.Expressions;

namespace SharedKernel.Persistence.EfCore.Tests.Specifications;

// ---------------------------------------------------------------------------
// T-24 — ISpecificationEvaluator<T> contract shape + downcast elimination
// ---------------------------------------------------------------------------

/// <summary>
/// Stub evaluator that does NOT extend <see cref="SpecificationEvaluator{T}"/>.
/// Verifies that <see cref="EfReadRepository{TAggregate,TId}"/> no longer downcasts
/// <see cref="ISpecificationEvaluator{T}"/> to the concrete type.
/// </summary>
internal sealed class StubSpecificationEvaluator<T> : ISpecificationEvaluator<T>
{
    public bool GetQueryCalled { get; private set; }
    public bool GetProjectedQueryCalled { get; private set; }

    public IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec)
    {
        GetQueryCalled = true;
        // Apply criteria only (minimal valid implementation for tests).
        if (spec.Criteria is not null)
            return inputQuery.Where(spec.Criteria);
        return inputQuery;
    }

    public IQueryable<TResult> GetProjectedQuery<TResult>(
        IQueryable<T> inputQuery,
        IProjectionSpecification<T, TResult> spec)
    {
        GetProjectedQueryCalled = true;
        var filtered = spec.Criteria is not null
            ? inputQuery.Where(spec.Criteria)
            : inputQuery;
        return filtered.Select(spec.Selector);
    }
}

/// <summary>
/// Concrete read repository that accepts any <see cref="ISpecificationEvaluator{TestAggregate}"/>
/// (including the stub above — not just the concrete <see cref="SpecificationEvaluator{T}"/>).
/// </summary>
internal sealed class StubBackedReadRepository(
    TestDbContext ctx,
    StubSpecificationEvaluator<TestAggregate> evaluator)
    : EfReadRepository<TestAggregate, TestId>(ctx, evaluator)
{
    public StubSpecificationEvaluator<TestAggregate> Evaluator { get; } = evaluator;
}

/// <summary>Minimal projection spec for interface-level tests.</summary>
internal sealed class TestNameProjectionSpec : Specification<TestAggregate>,
    IProjectionSpecification<TestAggregate, string>
{
    public Expression<Func<TestAggregate, string>> Selector => e => e.Name;

    public TestNameProjectionSpec()
    {
        ApplyNoTracking();
    }
}

public sealed class SpecificationEvaluatorInterfaceTests
{
    // -----------------------------------------------------------------------
    // Contract shape — interface declares GetProjectedQuery (P-097)
    // -----------------------------------------------------------------------

    [Fact]
    public void ISpecificationEvaluator_Declares_GetProjectedQuery()
    {
        var method = typeof(ISpecificationEvaluator<>).GetMethod("GetProjectedQuery");
        method.Should().NotBeNull(
            "GetProjectedQuery<TResult> must be declared on ISpecificationEvaluator<T> (P-097 promotion)");
    }

    [Fact]
    public void SpecificationEvaluator_Implements_ISpecificationEvaluator_GetProjectedQuery()
    {
        // Verifies the concrete implementation satisfies the interface.
        var iface = typeof(ISpecificationEvaluator<TestAggregate>);
        var concrete = typeof(SpecificationEvaluator<TestAggregate>);
        iface.IsAssignableFrom(concrete).Should().BeTrue();
        concrete.GetMethod("GetProjectedQuery").Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // No downcast — stub evaluator (not extending concrete type) works
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EfReadRepository_ListProjectedAsync_WorksWith_StubEvaluator_NoDowncast()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var stub = new StubSpecificationEvaluator<TestAggregate>();
        var repo = new StubBackedReadRepository(ctx, stub);

        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "StubTest", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new TestNameProjectionSpec();

        // Act — should NOT throw InvalidCastException (that would indicate a downcast is present)
        var result = await repo.ListProjectedAsync<string>(spec);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("StubTest");
        stub.GetProjectedQueryCalled.Should().BeTrue("the stub's GetProjectedQuery must have been called");
    }

    [Fact]
    public async Task EfReadRepository_GetBySpecProjectedAsync_WorksWith_StubEvaluator_NoDowncast()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var stub = new StubSpecificationEvaluator<TestAggregate>();
        var repo = new StubBackedReadRepository(ctx, stub);

        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "StubSingle", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new TestNameProjectionSpec();

        // Act
        var result = await repo.GetBySpecProjectedAsync<string>(spec);

        // Assert
        result.Should().Be("StubSingle");
        stub.GetProjectedQueryCalled.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Existing projection tests still pass with the interface-typed evaluator
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EfReadRepository_ListProjectedAsync_WithRealEvaluator_ProjectsCorrectly()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new SharedKernel.Persistence.EfCore.Tests.Repositories.TestAggregateReadRepository(ctx);

        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "Alpha", new SystemClock()));
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "Beta", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new TestNameProjectionSpec();
        var result = await repo.ListProjectedAsync<string>(spec);

        result.Should().HaveCount(2);
        result.Should().Contain("Alpha");
        result.Should().Contain("Beta");
    }
}
