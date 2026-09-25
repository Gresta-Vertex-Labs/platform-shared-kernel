using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Execution.Context;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Remediation;

// Fixtures of the wave-3b R1 regression tests (final review findings C6-C8, S1, S6, S8, F4, F15).

public sealed record R1ItemId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static R1ItemId New() => new(Guid.NewGuid());
}

public sealed record R1ItemRaised : DomainEvent;

public sealed class R1Item : AggregateRoot<R1ItemId>
{
    public R1Item(R1ItemId id, string name, IClock clock)
        : base(id, clock) => Name = name;

    private R1Item()
    {
    }

    public string Name { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public Money Price { get; set; } = Money.Create(1m, Currency.Eur).Value;

    public Money? Discount { get; set; }

    public void Raise() => RaiseDomainEvent(at => new R1ItemRaised { OccurredOn = at });
}

public sealed class R1PlainContext(DbContextOptions<R1PlainContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<R1Item> Items => Set<R1Item>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;
}

// ---- tenanted model (S1, S8) ----

public sealed class R1Order : IHasTenant
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<R1OrderLine> Lines { get; set; } = [];
}

public sealed class R1OrderLine : IHasTenant
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid TenantId { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? CreatedBy { get; set; }
}

[TenantShared]
public sealed class R1Country
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class R1Currency
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;
}

public sealed class R1TenantedContext(DbContextOptions<R1TenantedContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<R1Order> Orders => Set<R1Order>();

    public DbSet<R1OrderLine> Lines => Set<R1OrderLine>();

    public DbSet<R1Country> Countries => Set<R1Country>();

    public DbSet<R1Currency> Currencies => Set<R1Currency>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<R1Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).IsRequired();
        });
        modelBuilder.Entity<R1OrderLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<R1Country>().HasKey(x => x.Id);
        modelBuilder.Entity<R1Currency>(e =>
        {
            e.HasKey(x => x.Id);
            e.IsTenantShared();
        });
    }
}

public sealed class R1Untenanted
{
    public Guid Id { get; set; }
}

public sealed class R1InvalidTenantedContext(DbContextOptions<R1InvalidTenantedContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<R1Untenanted> Rows => Set<R1Untenanted>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;
}

/// <summary>An open, shared in-memory SQLite database and helpers to create contexts over it.</summary>
internal sealed class R1Database : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public R1Database()
    {
        ConnectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public string ConnectionString { get; }

    public TContext Create<TContext>(
        Func<DbContextOptions<TContext>, PersistenceContextDependencies, TContext> factory,
        IRequestContext? caller = null,
        IDomainEventDispatcher? dispatcher = null)
        where TContext : SharedKernelDbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(ConnectionString)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var context = factory(options, PersistenceContextDependencies.Create(caller ?? new FakeAuditActorContext(), domainEventDispatcher: dispatcher));
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose() => _keepAlive.Dispose();
}
