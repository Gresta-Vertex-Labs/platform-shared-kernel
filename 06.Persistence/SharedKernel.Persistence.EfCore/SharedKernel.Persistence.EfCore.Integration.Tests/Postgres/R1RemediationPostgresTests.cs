using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Entities;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

// Wave-3b R1 regression tests against real PostgreSQL: final review findings C1-C5 and S4/F6.

public sealed class R1AThing : AggregateRoot<Guid>
{
    public R1AThing(Guid id, IClock clock) : base(id, clock) { }

    private R1AThing() { }

    public string Name { get; set; } = string.Empty;
}

public sealed class R1BThing : AggregateRoot<Guid>
{
    public R1BThing(Guid id, IClock clock) : base(id, clock) { }

    private R1BThing() { }

    public string Name { get; set; } = string.Empty;
}

public sealed class R1AContext(DbContextOptions<R1AContext> o, PersistenceContextDependencies d) : SharedKernelDbContext(o, d)
{
    public DbSet<R1AThing> As => Set<R1AThing>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<R1AThing>().ToTable("r1_a_things");
    }
}

public sealed class R1BContext(DbContextOptions<R1BContext> o, PersistenceContextDependencies d) : SharedKernelDbContext(o, d)
{
    public DbSet<R1BThing> Bs => Set<R1BThing>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<R1BThing>().ToTable("r1_b_things");
    }
}

// ---- C4: one aggregate per base class ----

public sealed class R1Base : AggregateRoot<Guid>
{
    public R1Base(Guid id, IClock clock) : base(id, clock) { }

    private R1Base() { }

    public string Name { get; set; } = "n";
}

public sealed class R1Auditable : AuditableAggregateRoot<Guid>
{
    public R1Auditable(Guid id, IClock clock) : base(id, clock) { }

    private R1Auditable() { }

    public string Name { get; set; } = "n";
}

public sealed class R1Soft : SoftDeletableAggregateRoot<Guid>
{
    public R1Soft(Guid id, IClock clock) : base(id, clock) { }

    private R1Soft() { }

    public string Name { get; set; } = "n";
}

public sealed class R1AuditSoft : AuditableSoftDeletableAggregateRoot<Guid>
{
    public R1AuditSoft(Guid id, IClock clock) : base(id, clock) { }

    private R1AuditSoft() { }

    public string Name { get; set; } = "n";
}

public sealed class R1Full : FullAuditableAggregateRoot<Guid>
{
    public R1Full(Guid id, IClock clock) : base(id, clock) { }

    private R1Full() { }

    public string Name { get; set; } = "n";
}

public sealed class R1TBase : TenantedAggregateRoot<Guid>
{
    public R1TBase(Guid id, Guid tenant, IClock clock) : base(id, tenant, clock) { }

    private R1TBase() { }

    public string Name { get; set; } = "n";
}

public sealed class R1TAuditable : TenantedAuditableAggregateRoot<Guid>
{
    public R1TAuditable(Guid id, Guid tenant, IClock clock) : base(id, tenant, clock) { }

    private R1TAuditable() { }

    public string Name { get; set; } = "n";
}

public sealed class R1TSoft : TenantedSoftDeletableAggregateRoot<Guid>
{
    public R1TSoft(Guid id, Guid tenant, IClock clock) : base(id, tenant, clock) { }

    private R1TSoft() { }

    public string Name { get; set; } = "n";
}

public sealed class R1TAuditSoft : TenantedAuditableSoftDeletableAggregateRoot<Guid>
{
    public R1TAuditSoft(Guid id, Guid tenant, IClock clock) : base(id, tenant, clock) { }

    private R1TAuditSoft() { }

    public string Name { get; set; } = "n";
}

public sealed class R1TFull : TenantedFullAuditableAggregateRoot<Guid>
{
    public R1TFull(Guid id, Guid tenant, IClock clock) : base(id, tenant, clock) { }

    private R1TFull() { }

    public string Name { get; set; } = "n";
}

public sealed class R1BasesContext(DbContextOptions<R1BasesContext> o, PersistenceContextDependencies d) : SharedKernelDbContext(o, d)
{
    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<R1Base>();
        modelBuilder.Entity<R1Auditable>();
        modelBuilder.Entity<R1Soft>();
        modelBuilder.Entity<R1AuditSoft>();
        modelBuilder.Entity<R1Full>();
    }
}

public sealed class R1TenantedBasesContext(DbContextOptions<R1TenantedBasesContext> o, PersistenceContextDependencies d) : TenantedDbContext(o, d)
{
    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<R1TBase>();
        modelBuilder.Entity<R1TAuditable>();
        modelBuilder.Entity<R1TSoft>();
        modelBuilder.Entity<R1TAuditSoft>();
        modelBuilder.Entity<R1TFull>();
    }
}

// ---- C5: order → line → discount ----

public sealed class R1Order5 : AggregateRoot<Guid>
{
    public R1Order5(Guid id, IClock clock) : base(id, clock) { }

    private R1Order5() { }

    public List<R1Line5> Lines { get; } = [];
}

public sealed class R1Line5 : Entity<Guid>
{
    public R1Line5(Guid id) : base(id) { }

    private R1Line5() { }

    public Guid R1Order5Id { get; set; }

    public List<R1Discount5> Discounts { get; } = [];
}

public sealed class R1Discount5 : Entity<Guid>
{
    public R1Discount5(Guid id) : base(id) { }

    private R1Discount5() { }

    public Guid R1Line5Id { get; set; }

    public decimal Percent { get; set; }
}

public sealed class R1GraphContext(DbContextOptions<R1GraphContext> o, PersistenceContextDependencies d) : SharedKernelDbContext(o, d)
{
    public DbSet<R1Order5> Orders => Set<R1Order5>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<R1Order5>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.R1Order5Id).IsRequired();
        modelBuilder.Entity<R1Line5>().HasMany(l => l.Discounts).WithOne().HasForeignKey(x => x.R1Line5Id).IsRequired();
        modelBuilder.Entity<R1Discount5>().HasKey(x => x.Id);
    }
}

// ---- S4/F6: tenant tables ----

public sealed class R1RlsOrder : TenantedAggregateRoot<Guid>
{
    public R1RlsOrder(Guid id, Guid tenant, IClock clock) : base(id, tenant, clock) { }

    private R1RlsOrder() { }

    public List<R1RlsLine> Lines { get; } = [];
}

public sealed class R1RlsLine : Entity<Guid>, SharedKernel.Domain.Abstractions.IHasTenant
{
    public R1RlsLine(Guid id) : base(id) { }

    private R1RlsLine() { }

    public Guid R1RlsOrderId { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class R1RlsContext(DbContextOptions<R1RlsContext> o, PersistenceContextDependencies d) : TenantedDbContext(o, d)
{
    public DbSet<R1RlsOrder> Orders => Set<R1RlsOrder>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<R1RlsOrder>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.R1RlsOrderId).IsRequired();
    }
}

[Collection("EfCorePostgres")]
public sealed class R1RemediationPostgresTests(PostgreSqlContainerFixture fixture)
{
    private static readonly IClock Clock = new SystemClock();

    private string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"sk_r1_{Guid.NewGuid():N}" }.ConnectionString;

    private static IConfiguration Configuration(params (string Name, string ConnectionString)[] connections) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(connections.ToDictionary(c => $"ConnectionStrings:{c.Name}", c => (string?)c.ConnectionString))
            .Build();

    private static void Quiet<TContext>(EfCorePersistenceBuilder<TContext> p)
        where TContext : SharedKernelDbContext =>
        p.ConfigureDbContext((_, o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

    // Two contexts; "b" on the same database as "a" unless bDatabase is given.
    private async Task<ServiceProvider> TwoContextsAsync(string? bDatabase = null)
    {
        var aDatabase = NewDatabase();
        var configuration = Configuration(("a", aDatabase), ("b", bDatabase ?? aDatabase));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestContext>(_ => new FakeAuditActorContext());
        services.AddSharedKernelPostgres<R1AContext>(configuration, "a", Quiet);
        services.AddSharedKernelPostgres<R1BContext>(configuration, "b", Quiet);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<R1AContext>().Database.EnsureCreatedAsync();
        var b = scope.ServiceProvider.GetRequiredService<R1BContext>();
        if (bDatabase is null)
            await ((RelationalDatabaseCreator)b.GetService<IDatabaseCreator>()).CreateTablesAsync();
        else
            await b.Database.EnsureCreatedAsync();

        return provider;
    }

    private static async Task<(bool A, bool B)> ExistsAsync(ServiceProvider provider, Guid aId, Guid bId)
    {
        await using var scope = provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<R1AContext>().As.AnyAsync(x => x.Id == aId),
            await scope.ServiceProvider.GetRequiredService<R1BContext>().Bs.AnyAsync(x => x.Id == bId));
    }

    // ---- C1 ----

    [Fact]
    public async Task C1_TheDefaultUnitOfWork_CommitsAChangeStagedThroughAnotherContextsRepository()
    {
        await using var provider = await TwoContextsAsync();
        var bId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>(); // what TransactionBehavior injects
            var repositoryB = scope.ServiceProvider.GetRequiredService<IRepository<R1BThing, Guid>>();
            await unitOfWork.ExecuteInTransactionAsync(ct => repositoryB.AddAsync(new R1BThing(bId, Clock) { Name = "b" }, ct));
        }

        (await ExistsAsync(provider, Guid.Empty, bId)).B.Should().BeTrue();
    }

    [Fact]
    public async Task C1_BothContexts_CommitOrRollBackTogether()
    {
        await using var provider = await TwoContextsAsync();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var act = () => unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await scope.ServiceProvider.GetRequiredService<IRepository<R1AThing, Guid>>().AddAsync(new R1AThing(aId, Clock), ct);
                await scope.ServiceProvider.GetRequiredService<IRepository<R1BThing, Guid>>().AddAsync(new R1BThing(bId, Clock), ct);
                await unitOfWork.SaveChangesAsync(ct); // both rows written inside the transaction ...
                throw new InvalidProgramException("... and then the operation fails");
            });
            await act.Should().ThrowAsync<InvalidProgramException>();
        }

        (await ExistsAsync(provider, aId, bId)).Should().Be((false, false));

        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // A context first resolved inside the transaction joins it too.
                scope.ServiceProvider.GetRequiredService<R1BContext>().Bs.Add(new R1BThing(bId, Clock));
                await scope.ServiceProvider.GetRequiredService<IRepository<R1AThing, Guid>>().AddAsync(new R1AThing(aId, Clock), ct);
            });
        }

        (await ExistsAsync(provider, aId, bId)).Should().Be((true, true));
    }

    [Fact]
    public async Task C1_SaveChangesOutsideATransaction_SavesEveryContextOfTheScope()
    {
        await using var provider = await TwoContextsAsync();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<R1AContext>().As.Add(new R1AThing(aId, Clock));
            scope.ServiceProvider.GetRequiredService<R1BContext>().Bs.Add(new R1BThing(bId, Clock));
            (await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync()).Should().Be(2);
        }

        (await ExistsAsync(provider, aId, bId)).Should().Be((true, true));
    }

    [Fact]
    public async Task C1_AContextOnAnotherDatabase_WithChanges_RefusesTheCommit_InsteadOfDroppingThem()
    {
        await using var provider = await TwoContextsAsync(bDatabase: NewDatabase());
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var act = () => unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await scope.ServiceProvider.GetRequiredService<IRepository<R1AThing, Guid>>().AddAsync(new R1AThing(aId, Clock), ct);
                await scope.ServiceProvider.GetRequiredService<IRepository<R1BThing, Guid>>().AddAsync(new R1BThing(bId, Clock), ct);
            });

            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*R1BContext*cannot join*");
        }

        (await ExistsAsync(provider, aId, bId)).Should().Be((false, false));
    }

    // ---- C2 ----

    [Fact]
    public async Task C2_ANestedTransactionOnAnotherContext_JoinsTheOuterOne_AndKeepsTheAmbientTransaction()
    {
        await using var provider = await TwoContextsAsync();
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWorkA = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var unitOfWorkB = scope.ServiceProvider.GetRequiredService<IUnitOfWork<R1BContext>>();
            var ambient = scope.ServiceProvider.GetRequiredService<IAmbientDbTransaction>();

            await unitOfWorkA.ExecuteInTransactionAsync(async ct =>
            {
                scope.ServiceProvider.GetRequiredService<R1AContext>().As.Add(new R1AThing(aId, Clock));
                var outer = ambient.Current;
                unitOfWorkB.IsTransactionActive.Should().BeTrue("every unit of work of the scope shares the transaction");

                await unitOfWorkB.ExecuteInTransactionAsync(_ =>
                {
                    scope.ServiceProvider.GetRequiredService<R1BContext>().Bs.Add(new R1BThing(bId, Clock));
                    return Task.CompletedTask;
                }, ct);

                ambient.Current.Should().Be(outer, "the nested call must not clear the outer transaction");
            });

            ambient.Current.Should().BeNull();
        }

        (await ExistsAsync(provider, aId, bId)).Should().Be((true, true));
    }

    // ---- C3 ----

    private sealed class CommitFailureInterceptor(Func<int, Exception?> failure) : DbTransactionInterceptor
    {
        private int _commits;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            failure(Interlocked.Increment(ref _commits)) is { } exception ? throw exception : ValueTask.FromResult(result);
    }

    // Fails the next commit once, after the test armed it (the setup's own commits pass).
    private sealed class CommitArm
    {
        public Exception? Next;

        public Exception? Take() => Interlocked.Exchange(ref Next, null);
    }

    private async Task<ServiceProvider> WithCommitFailureAsync(Func<int, Exception?> failure)
    {
        var configuration = Configuration(("a", NewDatabase()));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestContext>(_ => new FakeAuditActorContext());
        services.AddSharedKernelPostgres<R1AContext>(configuration, "a", p => p
            .ConfigureProvider(o => { o.MaxRetryCount = 3; o.MaxRetryDelay = TimeSpan.FromMilliseconds(10); })
            .ConfigureDbContext((_, o) => o
                .AddInterceptors(new CommitFailureInterceptor(failure))
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))));
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<R1AContext>().Database.EnsureCreatedAsync();
        return provider;
    }

    [Fact]
    public async Task C3_ACommitWithoutAResponse_IsNeverReplayed()
    {
        var arm = new CommitArm();
        await using var provider = await WithCommitFailureAsync(_ => arm.Take());
        arm.Next = new NpgsqlException("simulated connection loss", new IOException("broken pipe"));

        var runs = 0;
        await using var scope = provider.CreateAsyncScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(ct =>
        {
            runs++;
            return scope.ServiceProvider.GetRequiredService<IRepository<R1AThing, Guid>>().AddAsync(new R1AThing(Guid.NewGuid(), Clock), ct);
        });

        (await act.Should().ThrowAsync<CommitOutcomeUnknownException>()).WithInnerException<NpgsqlException>();
        runs.Should().Be(1, "an ambiguous commit must never run the operation again");
    }

    [Fact]
    public async Task C3_ACommitTheServerRejected_IsKnownToHaveRolledBack_AndIsRetried()
    {
        var arm = new CommitArm();
        await using var provider = await WithCommitFailureAsync(_ => arm.Take());
        arm.Next = new PostgresException("simulated serialization failure", "ERROR", "ERROR", "40001");

        var runs = 0;
        var id = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(ct =>
            {
                runs++;
                return scope.ServiceProvider.GetRequiredService<IRepository<R1AThing, Guid>>().AddAsync(new R1AThing(id, Clock), ct);
            });
        }

        runs.Should().Be(2);
        await using var verify = provider.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<R1AContext>().As.CountAsync(x => x.Id == id)).Should().Be(1);
    }

    // ---- C4 ----

    private async Task<ServiceProvider> SingleAsync<TContext>(IRequestContext caller)
        where TContext : SharedKernelDbContext
    {
        var configuration = Configuration(("c4", NewDatabase()));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestEntityVersionKeys();
        services.AddScoped(_ => caller);
        services.AddSharedKernelPostgres<TContext>(configuration, "c4", Quiet);
        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreatedAsync();
        return provider;
    }

    private static async Task DetachedWritesAsync<TContext, TAggregate>(ServiceProvider provider, Func<TAggregate> create, bool versionOnTheObject)
        where TContext : SharedKernelDbContext
        where TAggregate : class, SharedKernel.Domain.Abstractions.IAggregateRoot<Guid>
    {
        var aggregate = create();
        EntityVersion version;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Add(aggregate);
            await context.SaveChangesAsync();
            version = ConcurrencyVersion.Get(context, aggregate);
        }

        // The version the row was inserted with: stale once the update below ran.
        var inserted = version;

        TAggregate snapshot;
        await using (var scope = provider.CreateAsyncScope())
            snapshot = (await new EfReadRepository<TAggregate, Guid>(scope.ServiceProvider.GetRequiredService<TContext>()).GetByIdAsync(aggregate.Id))!;

        // UpdateAsync without a version.
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var repository = new EfRepository<TAggregate, Guid>(context);
            if (versionOnTheObject)
            {
                await repository.UpdateAsync(snapshot);
                await context.SaveChangesAsync();
                version = ConcurrencyVersion.Get(context, snapshot);
            }
            else
            {
                (await FluentActions.Awaiting(() => repository.UpdateAsync(snapshot)).Should().ThrowAsync<InvalidOperationException>(typeof(TAggregate).Name))
                    .WithMessage("*UpdateAsync(aggregate, expectedVersion)*");
                (await FluentActions.Awaiting(() => repository.DeleteAsync(snapshot)).Should().ThrowAsync<InvalidOperationException>())
                    .WithMessage("*DeleteAsync(aggregate, expectedVersion)*");
            }
        }

        if (!versionOnTheObject)
        {
            // With the version the client read: the detached update succeeds.
            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                await new EfRepository<TAggregate, Guid>(context).UpdateAsync(snapshot, version);
                await context.SaveChangesAsync();
                version = ConcurrencyVersion.Get(context, snapshot);
            }
        }

        // A stale version conflicts; the current one deletes.
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            inserted.Should().NotBe(version, typeof(TAggregate).Name);
            await new EfRepository<TAggregate, Guid>(context).DeleteAsync(snapshot, inserted);
            await FluentActions.Awaiting(() => context.SaveChangesAsync()).Should().ThrowAsync<ConflictException>(typeof(TAggregate).Name);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await new EfRepository<TAggregate, Guid>(context).DeleteAsync(snapshot, version);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task C4_DetachedWrites_OverEveryAggregateBase()
    {
        await using var plain = await SingleAsync<R1BasesContext>(new FakeAuditActorContext());
        await DetachedWritesAsync<R1BasesContext, R1Base>(plain, () => new R1Base(Guid.NewGuid(), Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1BasesContext, R1Auditable>(plain, () => new R1Auditable(Guid.NewGuid(), Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1BasesContext, R1Soft>(plain, () => new R1Soft(Guid.NewGuid(), Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1BasesContext, R1AuditSoft>(plain, () => new R1AuditSoft(Guid.NewGuid(), Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1BasesContext, R1Full>(plain, () => new R1Full(Guid.NewGuid(), Clock), versionOnTheObject: true);

        var tenant = Guid.NewGuid();
        await using var tenanted = await SingleAsync<R1TenantedBasesContext>(new FakeAuditActorContext("actor", tenant));
        await DetachedWritesAsync<R1TenantedBasesContext, R1TBase>(tenanted, () => new R1TBase(Guid.NewGuid(), tenant, Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1TenantedBasesContext, R1TAuditable>(tenanted, () => new R1TAuditable(Guid.NewGuid(), tenant, Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1TenantedBasesContext, R1TSoft>(tenanted, () => new R1TSoft(Guid.NewGuid(), tenant, Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1TenantedBasesContext, R1TAuditSoft>(tenanted, () => new R1TAuditSoft(Guid.NewGuid(), tenant, Clock), versionOnTheObject: false);
        await DetachedWritesAsync<R1TenantedBasesContext, R1TFull>(tenanted, () => new R1TFull(Guid.NewGuid(), tenant, Clock), versionOnTheObject: true);
    }

    // ---- C5 ----

    [Fact]
    public async Task C5_AGrandchildChange_TouchesTheRoot_SoAConcurrentWriterConflicts()
    {
        await using var provider = await SingleAsync<R1GraphContext>(new FakeAuditActorContext());
        var orderId = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var order = new R1Order5(orderId, Clock);
            var line = new R1Line5(Guid.NewGuid());
            line.Discounts.Add(new R1Discount5(Guid.NewGuid()) { Percent = 1 });
            order.Lines.Add(line);
            var context = scope.ServiceProvider.GetRequiredService<R1GraphContext>();
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var c1 = first.ServiceProvider.GetRequiredService<R1GraphContext>();
        var c2 = second.ServiceProvider.GetRequiredService<R1GraphContext>();
        var o1 = await c1.Orders.Include(o => o.Lines).ThenInclude(l => l.Discounts).SingleAsync(o => o.Id == orderId);
        var o2 = await c2.Orders.Include(o => o.Lines).ThenInclude(l => l.Discounts).SingleAsync(o => o.Id == orderId);

        var before = ConcurrencyVersion.Get(c1, o1);
        o1.Lines[0].Discounts[0].Percent = 10;
        await c1.SaveChangesAsync();
        ConcurrencyVersion.Get(c1, o1).Should().NotBe(before, "the grandchild change must advance the root's version");

        o2.Lines[0].Discounts[0].Percent = 20;
        await FluentActions.Awaiting(() => c2.SaveChangesAsync()).Should().ThrowAsync<ConflictException>("the second writer based its change on a stale aggregate");
    }

    // ---- S4/F6 ----

    [Fact]
    public async Task S4_CoverageCheck_ReportsEveryUnprotectedTenantTable_AndPassesOnceTheModelHelperRan()
    {
        var tenant = Guid.NewGuid();
        await using var provider = await SingleAsync<R1RlsContext>(new FakeAuditActorContext("actor", tenant));

        var findings = await RowLevelSecurityCoverageCheck<R1RlsContext>.FindUnprotectedTablesAsync(provider, CancellationToken.None);
        findings.Should().HaveCount(2).And.Contain(f => f.Contains("r1rls_line", StringComparison.OrdinalIgnoreCase) || f.Contains("line", StringComparison.OrdinalIgnoreCase));

        var check = new RowLevelSecurityCoverageCheck<R1RlsContext>(provider, RowLevelSecurityCheckMode.Fail);
        await FluentActions.Awaiting(() => check.StartedAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not protected*");

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<R1RlsContext>();
            var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            migration.EnableTenantRowLevelSecurityForModel(context.GetService<IDesignTimeModel>().Model);
            foreach (var sql in migration.Operations.OfType<SqlOperation>())
                await context.Database.ExecuteSqlRawAsync(sql.Sql);
        }

        (await RowLevelSecurityCoverageCheck<R1RlsContext>.FindUnprotectedTablesAsync(provider, CancellationToken.None))
            .Should().BeEmpty("the model-driven helper protected the child table too");
        await check.StartedAsync(CancellationToken.None);
    }
}
