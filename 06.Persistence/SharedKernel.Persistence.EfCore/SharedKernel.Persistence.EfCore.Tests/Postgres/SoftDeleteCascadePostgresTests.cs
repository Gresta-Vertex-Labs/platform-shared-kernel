using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>
/// Proves <c>SoftDeleteInterceptor</c>'s cascade-rescue algorithm
/// against REAL PostgreSQL: soft-deleting <see cref="PgOrderAggregate"/> must not hard-delete its
/// required owned-collection children (<see cref="PgOrderAggregate.Lines"/>, its own table) despite
/// EF Core's default cascade-delete fixup marking them <c>Deleted</c> too, and the same-table owned
/// value object (<see cref="PgOrderAggregate.Address"/>) must survive untouched since it shares the
/// root's own row.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class SoftDeleteCascadePostgresTests
{
    private const string DatabaseName = "sk_p557_softdelete_cascade";

    private readonly PostgreSqlContainerFixture _fixture;

    public SoftDeleteCascadePostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private PgTestDbContext CreateContext(Guid tenantId) =>
        PgTestDbContextFactory.Create(
            ConnectionString,
            new FakeAuditActorContext("actor"),
            new FakeAuditActorContext("actor", tenantId));

    [Fact]
    public async Task SoftDelete_RequiredOwnedChildrenSurvive_AndOwnedVOColumnsIntact()
    {
        var tenantId = Guid.NewGuid();
        var orderId = PgOrderId.New();

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctx = CreateContext(tenantId))
        {
            var order = new PgOrderAggregate(
                orderId, tenantId, "Order With Lines", $"cascade-{Guid.NewGuid():N}", "42 Elm St", "Springfield",
                new SharedKernel.Primitives.Clocks.SystemClock());
            order.AddLine("Widget", 3);
            order.AddLine("Gadget", 7);
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
        }

        // Act — soft-delete the root through the normal repository path (Remove() -> Deleted ->
        // SoftDeleteInterceptor converts to Modified and rescues the cascaded owned children EF's
        // own fixup marked Deleted alongside it).
        await using (var deleteCtx = CreateContext(tenantId))
        {
            var order = await deleteCtx.Orders
                .Include(o => o.Lines) // load the owned collection so EF's cascade-delete fixup has something to cascade
                    .FirstAsync(o => o.Id == orderId);
            deleteCtx.Orders.Remove(order);
            await deleteCtx.SaveChangesAsync();
        }

        // Assert — the root row is soft-deleted (excluded by the default query filter)...
        await using var verifyCtx = CreateContext(tenantId);
        var stillFiltered = await verifyCtx.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        stillFiltered.Should().BeNull("the soft-deleted root must be excluded by the default query filter");

        //...but the root row itself, its Lines (required owned collection, its own table), and the
        // same-table owned VO all still physically exist, fully intact.
        var rescued = await verifyCtx.Orders
            .IgnoreQueryFilters([SharedKernel.Persistence.EfCore.Diagnostics.PersistenceFilterNames.SoftDelete])
                .Include(o => o.Lines)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

        rescued.Should().NotBeNull("the soft-deleted root row must still physically exist");
        rescued!.IsDeleted.Should().BeTrue();
        rescued.Lines.Should().HaveCount(2,
            "the required owned-collection children must be RESCUED from EF's cascade-delete fixup, never hard-deleted alongside the soft-deleted root");
        rescued.Lines.Select(l => l.Description).Should().BeEquivalentTo(["Widget", "Gadget"]);
        rescued.Address.Street.Should().Be("42 Elm St", "the same-table owned VO's columns must survive the soft-delete completely untouched");
        rescued.Address.City.Should().Be("Springfield");
    }

    [Fact]
    public async Task SoftDelete_TouchingOneOwnedLine_DoesNotResurrectOthers_AndRootStaysDeleted()
    {
        // A second, independent proof that the rescue is scoped correctly: after the cascade-rescue
        // above, editing ONE rescued line on a FRESH load must not somehow "undelete" the root or
        // duplicate/lose the sibling line.
        var tenantId = Guid.NewGuid();
        var orderId = PgOrderId.New();

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        Guid lineIdToKeep;
        await using (var ctx = CreateContext(tenantId))
        {
            var order = new PgOrderAggregate(
                orderId, tenantId, "Order", $"cascade2-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
            order.AddLine("Keep", 1);
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
            lineIdToKeep = order.Lines.Single().LineId;
        }

        await using (var deleteCtx = CreateContext(tenantId))
        {
            var order = await deleteCtx.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            deleteCtx.Orders.Remove(order);
            await deleteCtx.SaveChangesAsync();
        }

        await using var verifyCtx = CreateContext(tenantId);
        var rescued = await verifyCtx.Orders
            .IgnoreQueryFilters([SharedKernel.Persistence.EfCore.Diagnostics.PersistenceFilterNames.SoftDelete])
                .Include(o => o.Lines)
                    .FirstAsync(o => o.Id == orderId);

        rescued.IsDeleted.Should().BeTrue();
        rescued.Lines.Should().ContainSingle(l => l.LineId == lineIdToKeep);
    }
}
