using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Execution.Context;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Platform;

// ---------------------------------------------------------------------------------------------------------
// Fixtures: an aggregate root with a NON-owned child (required FK), and a tenanted twin — own contexts, own
// configuration (ShouldApplyConfiguration filters the rest of this test assembly out).
// ---------------------------------------------------------------------------------------------------------

public sealed record PipelineOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static PipelineOrderId New() => new(Guid.NewGuid());
}

public sealed class PipelineOrder : AuditableSoftDeletableAggregateRoot<PipelineOrderId>
{
    private readonly List<PipelineLine> _lines = [];

    public PipelineOrder(PipelineOrderId id, string name, IClock clock) : base(id, clock) => Name = name;

    private PipelineOrder() { }

    public string Name { get; set; } = string.Empty;

    public IReadOnlyCollection<PipelineLine> Lines => _lines;

    public PipelineLine AddLine(int quantity)
    {
        var line = new PipelineLine(Guid.NewGuid(), Id, quantity);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(PipelineLine line) => _lines.Remove(line);

    public void Ship() => RaiseDomainEvent(ts => new PipelineShipped { OccurredOn = ts });

    protected override void OnDelete() { }
}

/// <summary>A plain entity (not owned, not an aggregate) with a required foreign key to its aggregate root.</summary>
public sealed class PipelineLine
{
    public PipelineLine(Guid id, PipelineOrderId orderId, int quantity)
    {
        Id = id;
        OrderId = orderId;
        Quantity = quantity;
    }

    private PipelineLine() { OrderId = null!; }

    public Guid Id { get; private set; }

    public PipelineOrderId OrderId { get; private set; }

    public int Quantity { get; set; }
}

public sealed record PipelineShipped : DomainEvent;

public sealed class PipelineTenantedOrder : AggregateRoot<PipelineOrderId>, IHasTenant
{
    public PipelineTenantedOrder(PipelineOrderId id, Guid tenantId, IClock clock) : base(id, clock) => TenantId = tenantId;

    private PipelineTenantedOrder() { }

    public Guid TenantId { get; private set; }

    public string Note { get; set; } = string.Empty;
}

public sealed class PipelineDbContext(DbContextOptions<PipelineDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<PipelineOrder> Orders => Set<PipelineOrder>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PipelineOrder>(order =>
        {
            order.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).IsRequired();
            order.Navigation(o => o.Lines).HasField("_lines");
        });
    }
}

public sealed class PipelineTenantedDbContext(DbContextOptions<PipelineTenantedDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<PipelineTenantedOrder> Orders => Set<PipelineTenantedOrder>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;
}

/// <summary>
/// The collapsed save pipeline (A29) and the fixes that rode along: A4 (every concurrency failure is a
/// conflict), A6 (creation provenance is never overwritten), A24 (domain events on every save path) and M5
/// (a non-owned child change touches its aggregate root).
/// </summary>
public sealed class SaveChangesPipelineTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared");

    public SaveChangesPipelineTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private PipelineDbContext Orders(IRequestContext? caller = null, IClock? clock = null, IDomainEventDispatcher? dispatcher = null, InMemoryLoggerFactory? logs = null)
    {
        var context = new PipelineDbContext(
            new DbContextOptionsBuilder<PipelineDbContext>()
                .UseSqlite(_connection)
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options,
            PersistenceContextDependencies.Create(caller ?? new FakeAuditActorContext("alice"), clock ?? new FakeClock(T0), dispatcher, loggerFactory: logs));
        context.Database.EnsureCreated();
        return context;
    }

    private PipelineTenantedDbContext Tenanted(Guid? tenantId)
    {
        var context = new PipelineTenantedDbContext(
            new DbContextOptionsBuilder<PipelineTenantedDbContext>()
                .UseSqlite(_connection)
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options,
            PersistenceContextDependencies.Create(new FakeAuditActorContext("alice", tenantId)));
        context.Database.EnsureCreated();
        return context;
    }

    // ----- A6 -----

    [Fact]
    public async Task UpdateOfADetachedAggregate_NeverOverwritesCreatedByOrCreatedOn()
    {
        var id = PipelineOrderId.New();
        await using (var alice = Orders(new FakeAuditActorContext("alice"), new FakeClock(T0)))
        {
            alice.Orders.Add(new PipelineOrder(id, "original", new FakeClock(T0)));
            await alice.SaveChangesAsync();
        }

        await using (var mallory = Orders(new FakeAuditActorContext("mallory"), new FakeClock(T0.AddDays(1))))
        {
            // A detached instance: CreatedBy/CreatedOn are defaults, and Update marks every property modified.
            mallory.Orders.Update(new PipelineOrder(id, "renamed", new FakeClock(T0.AddDays(1))));
            await mallory.SaveChangesAsync();
        }

        await using var check = Orders();
        var stored = await check.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
        stored.Name.Should().Be("renamed");
        stored.CreatedBy.Should().Be("alice");
        stored.CreatedOn.Should().Be(T0);
        stored.ModifiedBy.Should().Be("mallory");
    }

    // ----- A4 -----

    [Fact]
    public async Task ConcurrentDelete_OfAnEntityWithoutConcurrencyToken_IsAConflict_NotARaw500()
    {
        var id = PipelineOrderId.New();
        await using var context = Orders();
        context.Orders.Add(new PipelineOrder(id, "x", new FakeClock(T0)));
        await context.SaveChangesAsync();

        await using (var other = Orders())
            await other.Database.ExecuteSqlRawAsync("DELETE FROM \"Orders\"");

        context.ChangeTracker.Entries<PipelineOrder>().Single().Entity.Name = "changed";
        var act = () => context.SaveChangesAsync();

        var thrown = await act.Should().ThrowAsync<ConflictException>();
        thrown.Which.Error.Code.Should().Be(ConcurrencyVersion.ConflictErrorCode);
        ConcurrencyVersion.TryGetCurrentVersion(thrown.Which, out _).Should().BeFalse("the row no longer exists");
    }

    [Fact]
    public async Task ConcurrentDelete_OfATenantedEntity_IsAConflict_NotATenantViolation()
    {
        // A4: the former translator reported ANY concurrency failure on a tenanted entity as a 403 tenant
        // violation. A plain race is a conflict; a violation is reported only when the row provably belongs to
        // another tenant (covered by the PostgreSQL tenant-isolation tests).
        var tenant = Guid.NewGuid();
        var id = PipelineOrderId.New();
        await using var context = Tenanted(tenant);
        context.Orders.Add(new PipelineTenantedOrder(id, tenant, new FakeClock(T0)));
        await context.SaveChangesAsync();

        await using (var other = Tenanted(tenant))
            await other.Database.ExecuteSqlRawAsync("DELETE FROM \"Orders\"");

        context.ChangeTracker.Entries<PipelineTenantedOrder>().Single().Entity.Note = "changed";
        var act = () => context.SaveChangesAsync();

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SynchronousSave_ConcurrencyFailure_IsAlsoAConflict()
    {
        var id = PipelineOrderId.New();
        using var context = Orders();
        context.Orders.Add(new PipelineOrder(id, "x", new FakeClock(T0)));
        context.SaveChanges();

        using (var other = Orders())
            other.Database.ExecuteSqlRaw("DELETE FROM \"Orders\"");

        context.ChangeTracker.Entries<PipelineOrder>().Single().Entity.Name = "changed";
        var act = () => context.SaveChanges();

        act.Should().Throw<ConflictException>();
    }

    // ----- M5 -----

    [Fact]
    public async Task ChangingANonOwnedChild_TouchesItsAggregateRoot()
    {
        var id = PipelineOrderId.New();
        await using (var alice = Orders(new FakeAuditActorContext("alice"), new FakeClock(T0)))
        {
            var order = new PipelineOrder(id, "o", new FakeClock(T0));
            order.AddLine(1);
            alice.Orders.Add(order);
            await alice.SaveChangesAsync();
        }

        await using (var bob = Orders(new FakeAuditActorContext("bob"), new FakeClock(T0.AddHours(1))))
        {
            var order = await bob.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
            order.Lines.Single().Quantity = 5;

            bob.Entry(order).State.Should().Be(EntityState.Unchanged, "only the child changed");
            await bob.SaveChangesAsync();
        }

        await using var check = Orders();
        var stored = await check.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
        stored.ModifiedBy.Should().Be("bob", "the root was touched, so its row (and on PostgreSQL its xmin) advanced");
        stored.ModifiedOn.Should().Be(T0.AddHours(1));
    }

    [Fact]
    public async Task RemovingANonOwnedChild_TouchesItsAggregateRoot()
    {
        var id = PipelineOrderId.New();
        await using (var alice = Orders())
        {
            var order = new PipelineOrder(id, "o", new FakeClock(T0));
            order.AddLine(1);
            order.AddLine(2);
            alice.Orders.Add(order);
            await alice.SaveChangesAsync();
        }

        await using (var bob = Orders(new FakeAuditActorContext("bob")))
        {
            var order = await bob.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
            order.RemoveLine(order.Lines.First());
            await bob.SaveChangesAsync();
        }

        await using var check = Orders();
        (await check.Orders.AsNoTracking().SingleAsync(o => o.Id == id)).ModifiedBy.Should().Be("bob");
    }

    // ----- Soft delete + cascade (behavior preserved by the collapse) -----

    [Fact]
    public async Task SoftDeletingARoot_KeepsItsNonSoftDeletableChildren()
    {
        var id = PipelineOrderId.New();
        await using (var alice = Orders())
        {
            var order = new PipelineOrder(id, "o", new FakeClock(T0));
            order.AddLine(1);
            alice.Orders.Add(order);
            await alice.SaveChangesAsync();
        }

        await using (var bob = Orders(new FakeAuditActorContext("bob")))
        {
            var order = await bob.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);
            bob.Orders.Remove(order);
            await bob.SaveChangesAsync();
        }

        await using var check = Orders();
        var stored = await check.Orders.IgnoreQueryFilters().Include(o => o.Lines).AsNoTracking().SingleAsync(o => o.Id == id);
        stored.IsDeleted.Should().BeTrue();
        stored.DeletedBy.Should().Be("bob");
        stored.Lines.Should().ContainSingle("a cascade-deleted child of a soft-deleted root is restored, not hard-deleted");
    }

    // ----- A29 -----

    [Fact]
    public async Task SoftDeletingManyRootsWithChildren_ScalesLinearly()
    {
        // A29: the former SoftDelete/Touch interceptors enumerated ChangeTracker.Entries() (which re-runs change
        // detection) once per visited entry — quadratic. 1500 roots x 2 children took tens of seconds.
        await using (var seed = Orders())
        {
            for (var i = 0; i < 1500; i++)
            {
                var order = new PipelineOrder(PipelineOrderId.New(), $"o{i}", new FakeClock(T0));
                order.AddLine(1);
                order.AddLine(2);
                seed.Orders.Add(order);
            }

            await seed.SaveChangesAsync();
        }

        await using var context = Orders();
        var all = await context.Orders.Include(o => o.Lines).ToListAsync();
        context.Orders.RemoveRange(all);

        var watch = Stopwatch.StartNew();
        await context.SaveChangesAsync();
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.Orders.IgnoreQueryFilters().CountAsync(o => o.IsDeleted)).Should().Be(1500);
    }

    // ----- A24 -----

    [Fact]
    public async Task DirectSaveChangesAsync_DispatchesDomainEvents()
    {
        var dispatched = new List<IDomainEvent>();
        await using var context = Orders(dispatcher: new CapturingDispatcher(dispatched));
        var order = new PipelineOrder(PipelineOrderId.New(), "o", new FakeClock(T0));
        order.Ship();
        context.Orders.Add(order);

        // Not through IUnitOfWork: seeders, factory users, outbox integrations call the context directly.
        await context.SaveChangesAsync();

        dispatched.Should().ContainSingle().Which.Should().BeOfType<PipelineShipped>();
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SynchronousSaveChanges_WithPendingEventsAndADispatcher_Throws()
    {
        using var context = Orders(dispatcher: new CapturingDispatcher([]));
        var order = new PipelineOrder(PipelineOrderId.New(), "o", new FakeClock(T0));
        order.Ship();
        context.Orders.Add(order);

        var act = () => context.SaveChanges();

        act.Should().Throw<InvalidOperationException>().WithMessage("*SaveChangesAsync*");
        order.DomainEvents.Should().ContainSingle("nothing was dispatched or discarded");
    }

    [Fact]
    public async Task PendingEventsWithoutADispatcher_AreDiscardedWithAWarning()
    {
        var logs = new InMemoryLoggerFactory();
        await using var context = Orders(logs: logs);
        var order = new PipelineOrder(PipelineOrderId.New(), "o", new FakeClock(T0));
        order.Ship();
        context.Orders.Add(order);

        await context.SaveChangesAsync();

        order.DomainEvents.Should().BeEmpty();
        logs.Loggers.Values.SelectMany(l => l.Records)
            .Should().Contain(r => r.EventId.Id == 6014 && r.Message.Contains(nameof(PipelineShipped)));
    }

    [Fact]
    public async Task EventRaisedByAHandler_IsDispatchedInTheSameSave()
    {
        var dispatched = new List<IDomainEvent>();
        await using var context = Orders();
        var second = new PipelineOrder(PipelineOrderId.New(), "second", new FakeClock(T0));
        context.AttachLease(context.RequestContext, new CascadingDispatcher(dispatched, second), context.CrossTenantScope);

        var first = new PipelineOrder(PipelineOrderId.New(), "first", new FakeClock(T0));
        first.Ship();
        context.Orders.Add(first);
        context.Orders.Add(second);

        await context.SaveChangesAsync();

        dispatched.Should().HaveCount(2);
    }

    private sealed class CapturingDispatcher(List<IDomainEvent> into) : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            into.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private sealed class CascadingDispatcher(List<IDomainEvent> into, PipelineOrder next) : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            into.AddRange(events);
            if (into.Count == 1)
                next.Ship();
            return Task.CompletedTask;
        }
    }
}
