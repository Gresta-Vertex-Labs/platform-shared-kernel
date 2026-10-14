using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.Repositories;
using SharedKernel.Persistence.EfCore.Tests.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Persistence.EfCore.Tests.Diagnostics;

// ---------------------------------------------------------------------------
// The repository spans: name "persistence {operation}", kind Internal, the aggregate and operation as tags, the outcome
// from the shared recorder, and the automatic TagWith(spec.GetType().Name) query annotation.
//
// PersistenceActivitySource.Source is one process-wide static source and xUnit runs different test classes in parallel,
// so a recorder on the source also sees the spans of other classes' repository calls. Each test starts a local root
// activity (Activity.Start() alone sets Activity.Current, and RepositoryTracing starts its span with that parent) and
// looks only at the spans that root parented.
// ---------------------------------------------------------------------------

public sealed class RepositoryTracingTests : IDisposable
{
    private readonly ActivityRecorder _spans = ActivityRecorder.StartRecording(PersistenceTelemetry.Name);
    private readonly Activity _root = new Activity("Test.Root").Start();

    public void Dispose()
    {
        _root.Stop();
        _spans.Dispose();
    }

    private List<Activity> Mine() => [.. _spans.RecordedActivities.Where(a => a.ParentId == _root.Id)];

    [Fact]
    public async Task ListAsync_EmitsAnInternalSpan_WithTheAggregateAndOperationTags_AndASuccessOutcome()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "Traced", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repo = new TestAggregateReadRepository(ctx);

        await repo.ListAsync(new AllSpecification<TestAggregate>());

        Mine().ShouldHaveSingleSpan("persistence list")
            .ShouldHaveSource(PersistenceTelemetry.Name)
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveTag(PersistenceTagKeys.AggregateType, "TestAggregate")
            .ShouldHaveTag(PersistenceTagKeys.Operation, "ListAsync")
            .ShouldHaveTag(PersistenceTagKeys.DbCollectionName, "TestAggregate")
            .ShouldHaveTag(PersistenceTagKeys.DbOperationName, "ListAsync")
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();
    }

    [Fact]
    public async Task AddAsync_EmitsASpanNamedAfterTheOperation_WithASuccessOutcome()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);

        await repo.AddAsync(new TestAggregate(TestId.New(), "TracedAdd", new SystemClock()));

        Mine().ShouldHaveSingleSpan("persistence add")
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task GetByIdAsync_NameKeepsEachWordOfTheOperation_InSnakeCase()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "TracedGetById", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repo = new TestAggregateRepository(ctx);

        var result = await repo.GetByIdAsync(id);

        result.Should().NotBeNull();
        Mine().ShouldHaveSingleSpan("persistence get_by_id")
            .ShouldHaveTag(PersistenceTagKeys.Operation, "GetByIdAsync")
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task ExistsAsync_EmitsASuccessSpan()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "TracedExists", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repo = new TestAggregateRepository(ctx);

        (await repo.ExistsAsync(id)).Should().BeTrue();

        Mine().ShouldHaveSingleSpan("persistence exists")
            .ShouldHaveTag(PersistenceTagKeys.Operation, "ExistsAsync")
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task StreamAsync_WrapsFullEnumeration_SingleSpanCoversAllItems()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.AddRange(
            new TestAggregate(TestId.New(), "One", new SystemClock()),
            new TestAggregate(TestId.New(), "Two", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repo = new TestAggregateReadRepository(ctx);

        var items = new List<TestAggregate>();
        await foreach (var item in repo.StreamAsync(new AllSpecification<TestAggregate>()))
            items.Add(item);

        items.Should().HaveCount(2);
        Mine().ShouldHaveSingleSpan("persistence stream")
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task StreamAsync_StoppedEarly_StillEndsTheSpan_AsASuccess()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.AddRange(
            new TestAggregate(TestId.New(), "One", new SystemClock()),
            new TestAggregate(TestId.New(), "Two", new SystemClock()),
            new TestAggregate(TestId.New(), "Three", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repo = new TestAggregateReadRepository(ctx);

        await foreach (var _ in repo.StreamAsync(new AllSpecification<TestAggregate>()))
            break;

        // The early disposal path must have finalized the span, not left it open without an outcome.
        Mine().ShouldHaveSingleSpan("persistence stream")
            .ShouldHaveOutcome(WellKnownOutcomes.Success)
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public void GetQuery_AutomaticTagWith_AnnotatesGeneratedSqlWithSpecTypeName()
    {
        var evaluator = new SpecificationEvaluator<TestAggregate>();
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var spec = new NameFilterSpec("Alpha");

        var sql = evaluator.GetQuery(ctx.TestAggregates, spec).ToQueryString();

        // TagWith renders as a leading SQL comment containing the spec's CLR type name.
        sql.Should().Contain(nameof(NameFilterSpec));
    }

    // A disposed context raises a genuine ObjectDisposedException inside the traced delegate, so the failure path records
    // status Error, error.type as the exception's full name, the exception event, and still rethrows.

    [Fact]
    public async Task CountAsync_WhenOperationThrows_RecordsTheException_AndRethrows()
    {
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateReadRepository(ctx);
        ctx.Dispose();

        Func<Task> act = () => repo.CountAsync(new AllSpecification<TestAggregate>());

        await act.Should().ThrowAsync<ObjectDisposedException>();
        Mine().ShouldHaveSingleSpan("persistence count")
            .ShouldHaveOutcome(WellKnownOutcomes.Exception)
            .ShouldBeRecordedException(typeof(ObjectDisposedException));
    }

    [Fact]
    public async Task GetByIdAsync_WhenOperationThrows_RecordsTheException_AndRethrows()
    {
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);
        ctx.Dispose();

        Func<Task> act = () => repo.GetByIdAsync(TestId.New());

        await act.Should().ThrowAsync<ObjectDisposedException>();
        Mine().ShouldHaveSingleSpan("persistence get_by_id")
            .ShouldHaveOutcome(WellKnownOutcomes.Exception)
            .ShouldBeRecordedException(typeof(ObjectDisposedException));
    }

    [Fact]
    public async Task ListAsync_WhenTheCallerCancels_RecordsACancellation_NeverAnError()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateReadRepository(ctx);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Func<Task> act = () => repo.ListAsync(new AllSpecification<TestAggregate>(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Mine().ShouldHaveSingleSpan("persistence list")
            .ShouldHaveOutcome(WellKnownOutcomes.Canceled)
            .ShouldBeRecordedCancellation();
    }

    // No tag value ever equals a raw SQL parameter, entity property value or identifier from the fixture, across a
    // representative sample of traced operations.

    [Fact]
    public async Task TracedOperations_NeverIncludeRawEntityPropertyOrParameterValues_InSpanTags()
    {
        const string sensitiveName = "Sensitive-Secret-Value-9f3a";
        var sensitiveId = TestId.New();

        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.Add(new TestAggregate(sensitiveId, sensitiveName, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var writeRepo = new TestAggregateRepository(ctx);
        var readRepo = new TestAggregateReadRepository(ctx);

        var nameSpec = new NameFilterSpec(sensitiveName);
        await readRepo.ListAsync(nameSpec);
        await readRepo.CountAsync(nameSpec);
        await readRepo.GetByIdsAsync([sensitiveId]);
        await writeRepo.AddAsync(new TestAggregate(TestId.New(), "AnotherEntity", new SystemClock()));

        var mine = Mine();
        mine.Should().NotBeEmpty();
        mine.ShouldNotLeak(sensitiveName, sensitiveId.Value.ToString(), sensitiveId.ToString());
        mine.ShouldFollowSpanConvention();
    }

    [Theory]
    [InlineData("GetByIdAsync", "persistence get_by_id")]
    [InlineData("GetByIdsAsync", "persistence get_by_ids")]
    [InlineData("FirstOrDefaultAsync", "persistence first_or_default")]
    [InlineData("ListPagedProjectedAsync", "persistence list_paged_projected")]
    [InlineData("ExecuteUpdateAsync", "persistence execute_update")]
    [InlineData("ListKeysetAsync", "persistence list_keyset")]
    public void SpanName_IsTheLowercaseSnakeCaseOperation_WithoutTheAsyncSuffix(string operation, string expected)
    {
        RepositoryTracing.SpanName(operation).Should().Be(expected);
        RepositoryTracing.SpanName(operation).Should().BeSameAs(RepositoryTracing.SpanName(operation), "the name is derived once");
    }
}
