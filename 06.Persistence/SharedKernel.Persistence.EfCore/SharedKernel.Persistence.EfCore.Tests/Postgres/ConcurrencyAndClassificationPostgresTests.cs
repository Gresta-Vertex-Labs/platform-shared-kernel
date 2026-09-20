using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>Test-only <see cref="IDbUpdateExceptionClassifier"/> recognizing PostgreSQL SQLSTATE codes.</summary>
/// <remarks>
/// Proves the classifier HOOK itself is correctly wired end to end
/// against a REAL Npgsql <see cref="PostgresException"/>. The concrete PostgreSQL classifier lives in
/// <c>SharedKernel.Persistence.Npgsql</c>/<c>.PostgreSQL</c> — this fake exists ONLY to prove
/// <see cref="SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext"/>'s
/// <c>SaveChanges</c>/<c>SaveChangesAsync</c> genuinely offers a non-concurrency
/// <see cref="DbUpdateException"/> to every registered classifier and rethrows the first
/// non-<see langword="null"/> translation, using a REAL PostgreSQL unique/foreign-key violation as
/// the input — not a hand-constructed fake exception.
/// </remarks>
internal sealed class TestPostgresSqlStateClassifier : IDbUpdateExceptionClassifier
{
    public const string UniqueViolationSqlState = "23505";
    public const string ForeignKeyViolationSqlState = "23503";

    public Exception? TryClassify(DbUpdateException exception) =>
        exception.InnerException is PostgresException pg
            ? pg.SqlState switch
            {
                UniqueViolationSqlState => new ConflictException(
                    Error.Conflict("test.unique_violation", $"unique_violation:{pg.ConstraintName}")),
                ForeignKeyViolationSqlState => new InvalidOperationException($"fk_violation:{pg.ConstraintName}", exception),
                _ => null,
            }
            : null;
}

/// <summary>
/// Proves, against REAL PostgreSQL:
/// (1) <see cref="SharedKernel.Persistence.EfCore.Interceptors.AggregateRootTouchInterceptor"/>
/// causes a genuine <c>xmin</c>-based optimistic-concurrency conflict when two concurrent edits touch
/// the SAME aggregate — one via the root's own scalar property, the other via a DIFFERENT owned-
/// collection child — and (2) the <see cref="IDbUpdateExceptionClassifier"/> extensibility hook
/// correctly offers a genuine PostgreSQL unique/foreign-key violation to a registered classifier,
/// and correctly leaves one unclassified when none is registered.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class ConcurrencyAndClassificationPostgresTests
{
    private const string DatabaseName = "sk_p557_concurrency_classification";

    private readonly PostgreSqlContainerFixture _fixture;

    public ConcurrencyAndClassificationPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private PgTestDbContext CreateContext(Guid tenantId, IDbUpdateExceptionClassifier? classifier = null) =>
        PgTestDbContextFactory.Create(
            ConnectionString,
            new FakeAuditActorContext("actor"),
            new FakeAuditActorContext("actor", tenantId),
            exceptionClassifiers: classifier is null ? null : [classifier]);

    [Fact]
    public async Task AggregateRootTouchInterceptor_ConcurrentChildOnlyEdit_ConflictsWithRootEdit()
    {
        var tenantId = Guid.NewGuid();
        var orderId = PgOrderId.New();
        Guid lineId;

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctx = CreateContext(tenantId))
        {
            var order = new PgOrderAggregate(
                orderId, tenantId, "Original", $"touch-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
            order.AddLine("OnlyLine", 1);
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
            lineId = order.Lines.Single().LineId;
        }

        // Two INDEPENDENT contexts load the SAME order concurrently (simulating two concurrent
        // requests), each reading the row at the SAME initial xmin.
        await using var ctxRootEditor = CreateContext(tenantId);
        await using var ctxChildEditor = CreateContext(tenantId);

        var rootView = await ctxRootEditor.Orders.FirstAsync(o => o.Id == orderId);
        var childView = await ctxChildEditor.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);

        // Act — the FIRST writer edits the ROOT's own scalar property directly.
        rootView.Rename("RenamedByRootEditor");
        await ctxRootEditor.SaveChangesAsync();

        // The SECOND writer edits only a CHILD (an owned-collection line) — no scalar property on
        // the root itself changes. Without AggregateRootTouchInterceptor this save would succeed
        // (the root's own row/xmin is untouched by a child-table-only UPDATE), silently missing the
        // fact both writers raced on the SAME aggregate's consistency boundary.
        childView.ChangeLineQuantity(lineId, 99);
        var act = () => ctxChildEditor.SaveChangesAsync();

        await act.Should().ThrowAsync<ConflictException>(
            "AggregateRootTouchInterceptor must force the root row to re-UPDATE (and hence re-check "
            + "its xmin) even though only an owned-collection CHILD's own row actually changed, so "
            + "the second writer's stale xmin is correctly detected as a real concurrency conflict");
    }

    [Fact]
    public async Task UniqueViolation_NoClassifierRegistered_PropagatesRawDbUpdateException()
    {
        var tenantId = Guid.NewGuid();
        var sharedCode = $"unique-noclassifier-{Guid.NewGuid():N}";

        await using var setup = CreateContext(tenantId);
        await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId);
        ctx.Orders.Add(new PgOrderAggregate(PgOrderId.New(), tenantId, "First", sharedCode, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
        await ctx.SaveChangesAsync();

        ctx.Orders.Add(new PgOrderAggregate(PgOrderId.New(), tenantId, "Second", sharedCode, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
        var act = () => ctx.SaveChangesAsync();

        var thrown = await act.Should().ThrowAsync<DbUpdateException>(
            "with no IDbUpdateExceptionClassifier registered, a unique violation must propagate as the raw DbUpdateException, unclassified");
        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(TestPostgresSqlStateClassifier.UniqueViolationSqlState);
    }

    [Fact]
    public async Task UniqueViolation_WithClassifierRegistered_TranslatesToTypedException()
    {
        var tenantId = Guid.NewGuid();
        var sharedCode = $"unique-classified-{Guid.NewGuid():N}";

        await using var setup = CreateContext(tenantId);
        await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId, new TestPostgresSqlStateClassifier());
        ctx.Orders.Add(new PgOrderAggregate(PgOrderId.New(), tenantId, "First", sharedCode, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
        await ctx.SaveChangesAsync();

        ctx.Orders.Add(new PgOrderAggregate(PgOrderId.New(), tenantId, "Second", sharedCode, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
        var act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<ConflictException>(
            "a registered IDbUpdateExceptionClassifier must be offered the real PostgresException and its translation must replace the raw DbUpdateException")
                .Where(e => e.Message.Contains("unique_violation"));
    }

    [Fact]
    public async Task ForeignKeyViolation_WithClassifierRegistered_TranslatesToTypedException()
    {
        var tenantId = Guid.NewGuid();

        await using var setup = CreateContext(tenantId);
        await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId, new TestPostgresSqlStateClassifier());

        // References a PgOrderId that was never inserted — a genuine foreign-key violation.
        ctx.Tags.Add(new PgOrderTag(PgOrderTagId.New(), PgOrderId.New(), "OrphanTag"));
        var act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the classifier must translate the real PostgreSQL foreign-key violation")
                .Where(e => e.Message.Contains("fk_violation"));
    }
}
