using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.Repositories;
using SharedKernel.Persistence.EfCore.Tests.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Diagnostics;

// ---------------------------------------------------------------------------
// WO-051/P-319 — PersistenceActivitySource / PersistenceTagKeys / RepositoryTracing tracing spans,
// and the automatic TagWith(spec.GetType().Name) query annotation.
//
// Cross-test correlation (discovered during this phase, mirrors 02.Caching's own documented
// OtelTracingTests/OtelMetricsTests hazard, WO-050/P-304): PersistenceActivitySource.Source is a
// single process-wide static ActivitySource, and xUnit runs different test CLASSES in parallel by
// default (only tests WITHIN one class/collection are sequential). StreamingRepositoryTests.cs (a
// different class) also drives TestAggregate.StreamAsync through the identical traced repository,
// so a bare ActivityListener filtering only on OperationName can observe a concurrently-running
// test's span and inflate a count that should be exactly one. Each test below starts a local
// "Test.Root" Activity via the plain System.Diagnostics.Activity API (no ActivitySource/listener
// needed for it to work — Activity.Start() alone sets Activity.Current) BEFORE registering its
// listener; RepositoryTracing.StartActivity calls ActivitySource.StartActivity(name, kind) with no
// explicit parent, so it defaults to Activity.Current as parent. Filtering observed activities to
// ParentId == rootActivity.Id therefore isolates spans this test's own call produced from any
// unrelated concurrently-running test's spans of the identical operation name.
// ---------------------------------------------------------------------------

public sealed class RepositoryTracingTests
{
    [Fact]
    public async Task ListAsync_EmitsActivity_WithAggregateTypeOperationAndSuccessOutcome()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "Traced", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var repo = new TestAggregateReadRepository(ctx);

        using var rootActivity = new Activity("Test.Root").Start();

        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Persistence",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.ParentId == rootActivity.Id)
                    lock (activities)
                        activities.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        var spec = new AllSpecification<TestAggregate>();
        await repo.ListAsync(spec);

        // Assert
        var traced = activities.Should().ContainSingle(a => a.OperationName == "TestAggregate.ListAsync").Subject;
        traced.Tags.Should().Contain(t => t.Key == "persistence.aggregate_type" && t.Value == "TestAggregate");
        traced.Tags.Should().Contain(t => t.Key == "persistence.operation" && t.Value == "ListAsync");
        traced.Tags.Should().Contain(t => t.Key == "persistence.outcome" && t.Value == "success");
        traced.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task AddAsync_EmitsActivity_WithSuccessOutcome()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);

        using var rootActivity = new Activity("Test.Root").Start();

        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Persistence",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.ParentId == rootActivity.Id)
                    lock (activities)
                        activities.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        await repo.AddAsync(new TestAggregate(TestId.New(), "TracedAdd", new SystemClock()));

        // Assert
        var traced = activities.Should().ContainSingle(a => a.OperationName == "TestAggregate.AddAsync").Subject;
        traced.Tags.Should().Contain(t => t.Key == "persistence.outcome" && t.Value == "success");
    }

    [Fact]
    public async Task StreamAsync_WrapsFullEnumeration_SingleActivityCoversAllItems()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.AddRange(
            new TestAggregate(TestId.New(), "One", new SystemClock()),
            new TestAggregate(TestId.New(), "Two", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var repo = new TestAggregateReadRepository(ctx);

        using var rootActivity = new Activity("Test.Root").Start();

        var startedCount = 0;
        var stoppedCount = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Persistence",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                if (activity.OperationName == "TestAggregate.StreamAsync" && activity.ParentId == rootActivity.Id)
                    Interlocked.Increment(ref startedCount);
            },
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "TestAggregate.StreamAsync" && activity.ParentId == rootActivity.Id)
                    Interlocked.Increment(ref stoppedCount);
            },
        };
        ActivitySource.AddActivityListener(listener);

        // Act — enumerate fully.
        var spec = new AllSpecification<TestAggregate>();
        var items = new List<TestAggregate>();
        await foreach (var item in repo.StreamAsync(spec))
            items.Add(item);

        // Assert — exactly one span for the whole enumeration, not one per item.
        items.Should().HaveCount(2);
        startedCount.Should().Be(1);
        stoppedCount.Should().Be(1);
    }

    [Fact]
    public void GetQuery_AutomaticTagWith_AnnotatesGeneratedSqlWithSpecTypeName()
    {
        // Arrange
        var evaluator = new SpecificationEvaluator<TestAggregate>();
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var spec = new NameFilterSpec("Alpha");

        // Act
        var query = evaluator.GetQuery(ctx.TestAggregates, spec);
        var sql = query.ToQueryString();

        // Assert — TagWith renders as a leading SQL comment containing the spec's CLR type name.
        sql.Should().Contain(nameof(NameFilterSpec));
    }
}
