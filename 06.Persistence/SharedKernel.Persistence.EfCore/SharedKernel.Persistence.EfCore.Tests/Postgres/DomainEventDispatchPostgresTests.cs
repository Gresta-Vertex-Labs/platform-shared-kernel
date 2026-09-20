using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>
/// Proves domain events raised on an aggregate that is about to be
/// HARD-deleted are still dispatched, against REAL PostgreSQL. Pre-commit dispatch
/// (<see cref="DomainEventDispatchLoop"/>) reads and clears an entity's events BEFORE the physical
/// <c>DELETE</c> executes, closing the pre-existing "hard delete loses events" defect
/// (<see cref="EfUnitOfWork"/>'s own remarks).
/// </summary>
[Collection("EfCorePostgres")]
public sealed class DomainEventDispatchPostgresTests
{
    private const string DatabaseName = "sk_p557_domain_events";

    private readonly PostgreSqlContainerFixture _fixture;

    public DomainEventDispatchPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private PgTestDbContext CreateContext(Guid tenantId) =>
        PgTestDbContextFactory.Create(
            ConnectionString,
            new FakeAuditActorContext("actor"),
            new FakeAuditActorContext("actor", tenantId));

    [Fact]
    public async Task HardDelete_EventsRaisedBeforeRemove_AreDispatched()
    {
        var tenantId = Guid.NewGuid();
        var id = PgHardDeleteId.New();
        var dispatcher = Substitute.For<IDomainEventDispatcher>();

        await using var setup = CreateContext(tenantId);
        await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId);
        var uow = new EfUnitOfWork(ctx, dispatcher);

        var entity = new PgHardDeleteAggregate(id, tenantId, "ToHardDelete", new SharedKernel.Primitives.Clocks.SystemClock());
        ctx.HardDeleteAggregates.Add(entity);
        await uow.SaveChangesAsync();

        // Act — raise a domain event on the entity, THEN hard-delete it in the SAME SaveChanges
        // call. Under the old post-commit dispatch design this event was lost: SaveChanges clears
        // the ChangeTracker's Deleted entries before anything could read their events back off them.
        entity.RaiseRemovedEvent();
        ctx.HardDeleteAggregates.Remove(entity);
        await uow.SaveChangesAsync();

        // Assert — dispatched exactly once, despite the entity's row being physically gone.
        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<IReadOnlyList<IDomainEvent>>(events => events.Any(e => e is PgHardDeleteAggregateRemovedEvent)),
            Arg.Any<CancellationToken>());

        await using var verifyCtx = CreateContext(tenantId);
        (await verifyCtx.HardDeleteAggregates.CountAsync(e => e.Id == id)).Should().Be(0,
            "the row must be genuinely, physically gone — this is a hard delete, not soft-delete");
    }

    [Fact]
    public async Task HardDelete_NoDispatcherRegistered_EventsStillCleared_DoesNotThrow()
    {
        var tenantId = Guid.NewGuid();
        var id = PgHardDeleteId.New();

        await using var setup = CreateContext(tenantId);
        await setup.Database.EnsureCreatedAsync();

        await using var ctx = CreateContext(tenantId);
        var uow = new EfUnitOfWork(ctx, dispatcher: null);

        var entity = new PgHardDeleteAggregate(id, tenantId, "NoDispatcher", new SharedKernel.Primitives.Clocks.SystemClock());
        ctx.HardDeleteAggregates.Add(entity);
        await uow.SaveChangesAsync();

        entity.RaiseRemovedEvent();
        ctx.HardDeleteAggregates.Remove(entity);

        var act = () => uow.SaveChangesAsync();
        await act.Should().NotThrowAsync("an unregistered dispatcher must not fail the save — events are simply cleared, not dispatched");

        entity.DomainEvents.Should().BeEmpty();
    }
}
