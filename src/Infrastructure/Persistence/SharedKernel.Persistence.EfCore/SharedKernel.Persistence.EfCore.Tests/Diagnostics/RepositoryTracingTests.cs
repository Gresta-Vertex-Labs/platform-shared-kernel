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
// PersistenceActivitySource / PersistenceTagKeys / RepositoryTracing tracing spans,
// and the automatic TagWith(spec.GetType().Name) query annotation.
//
// Cross-test correlation (mirrors 02.Caching's own documented
// OtelTracingTests/OtelMetricsTests hazard): PersistenceActivitySource.Source is a
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

    // -------------------------------------------------------------------------
    // Failure-path span tagging. Disposing the underlying DbContext before
    // invoking a traced read forces a genuine ObjectDisposedException INSIDE the traced operation
    // delegate (Set<TAggregate>() throws once the context is disposed) — proving RepositoryTracing
    // catches it, tags Outcome=="failure"/ErrorType, sets ActivityStatusCode.Error, and rethrows
    // rather than swallowing it.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountAsync_WhenOperationThrows_EmitsActivity_WithFailureOutcome_AndRethrows()
    {
        // Arrange
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateReadRepository(ctx);
        ctx.Dispose(); // forces a genuine exception the next time the context is touched

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

        var spec = new AllSpecification<TestAggregate>();

        // Act
        Func<Task> act = () => repo.CountAsync(spec);

        // Assert — the exception is genuinely rethrown, never swallowed.
        await act.Should().ThrowAsync<ObjectDisposedException>();

        var traced = activities.Should().ContainSingle(a => a.OperationName == "TestAggregate.CountAsync").Subject;
        traced.Tags.Should().Contain(t => t.Key == "persistence.outcome" && t.Value == "failure");
        traced.Tags.Should().Contain(t => t.Key == "error.type" && t.Value == "ObjectDisposedException");
        traced.Status.Should().Be(ActivityStatusCode.Error);
    }

    // -------------------------------------------------------------------------
    // Negative assertion: no span tag value ever equals a raw SQL parameter,
    // entity property value, or tenant/user identifier from the fixture, across a representative
    // sample of traced operations.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TracedOperations_NeverIncludeRawEntityPropertyOrParameterValues_InSpanTags()
    {
        // Arrange — distinctive "sensitive" values that must never leak into a span tag, mirroring
        // the cache.key_prefix-never-full-key precedent from 02.Caching.
        const string sensitiveName = "Sensitive-Secret-Value-9f3a";
        var sensitiveId = TestId.New();

        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.Add(new TestAggregate(sensitiveId, sensitiveName, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var writeRepo = new TestAggregateRepository(ctx);
        var readRepo = new TestAggregateReadRepository(ctx);

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

        // Act — a representative sample of traced operations touching the sensitive fixture values.
        var nameSpec = new NameFilterSpec(sensitiveName);
        await readRepo.ListAsync(nameSpec);
        await readRepo.CountAsync(nameSpec);
        await readRepo.GetByIdsAsync([sensitiveId]);
        await writeRepo.AddAsync(new TestAggregate(TestId.New(), "AnotherEntity", new SystemClock()));

        // Assert — no tag VALUE anywhere ever equals the raw entity property, ID, or SQL parameter
        // value from the fixture. Only low-cardinality metadata (type name, operation name, outcome,
        // error type) is ever set as a tag.
        activities.Should().NotBeEmpty();
        var allTagValues = activities.SelectMany(a => a.Tags).Select(t => t.Value).ToList();

        allTagValues.Should().NotContain(sensitiveName);
        allTagValues.Should().NotContain(sensitiveId.Value.ToString());
        allTagValues.Should().NotContain(sensitiveId.ToString());
    }

    // -------------------------------------------------------------------------
    // EfRepository.GetByIdAsync/.ExistsAsync were, until this phase, the only
    // two EfRepository public members never wrapped in RepositoryTracing.ExecuteTracedAsync.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_EmitsActivity_WithSuccessOutcome()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "TracedGetById", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

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
        var result = await repo.GetByIdAsync(id);

        // Assert
        result.Should().NotBeNull();
        var traced = activities.Should().ContainSingle(a => a.OperationName == "TestAggregate.GetByIdAsync").Subject;
        traced.Tags.Should().Contain(t => t.Key == "persistence.aggregate_type" && t.Value == "TestAggregate");
        traced.Tags.Should().Contain(t => t.Key == "persistence.operation" && t.Value == "GetByIdAsync");
        traced.Tags.Should().Contain(t => t.Key == "persistence.outcome" && t.Value == "success");
        traced.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task ExistsAsync_EmitsActivity_WithSuccessOutcome()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "TracedExists", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

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
        var exists = await repo.ExistsAsync(id);

        // Assert
        exists.Should().BeTrue();
        var traced = activities.Should().ContainSingle(a => a.OperationName == "TestAggregate.ExistsAsync").Subject;
        traced.Tags.Should().Contain(t => t.Key == "persistence.operation" && t.Value == "ExistsAsync");
        traced.Tags.Should().Contain(t => t.Key == "persistence.outcome" && t.Value == "success");
        traced.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task GetByIdAsync_WhenOperationThrows_EmitsActivity_WithFailureOutcome_AndRethrows()
    {
        // Arrange
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);
        ctx.Dispose(); // forces a genuine exception the next time the context is touched

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
        Func<Task> act = () => repo.GetByIdAsync(TestId.New());

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();

        var traced = activities.Should().ContainSingle(a => a.OperationName == "TestAggregate.GetByIdAsync").Subject;
        traced.Tags.Should().Contain(t => t.Key == "persistence.outcome" && t.Value == "failure");
        traced.Tags.Should().Contain(t => t.Key == "error.type" && t.Value == "ObjectDisposedException");
        traced.Status.Should().Be(ActivityStatusCode.Error);
    }
}
