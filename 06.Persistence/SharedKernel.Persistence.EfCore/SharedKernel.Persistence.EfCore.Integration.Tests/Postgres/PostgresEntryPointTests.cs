using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Transactions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

// ---------------------------------------------------------------------------------------------------------
// Fixtures: plain aggregates (no IHasConcurrency, no configuration class) — everything comes from conventions.
// ---------------------------------------------------------------------------------------------------------

public sealed record EntryOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static EntryOrderId New() => new(Guid.NewGuid());
}

public sealed class EntryOrder : AuditableAggregateRoot<EntryOrderId>
{
    private readonly List<EntryLine> _lines = [];

    public EntryOrder(EntryOrderId id, string name, IClock clock) : base(id, clock) => Name = name;

    private EntryOrder() { }

    public string Name { get; set; } = string.Empty;

    public IReadOnlyCollection<EntryLine> Lines => _lines;

    public void AddLine(int quantity) => _lines.Add(new EntryLine(Guid.NewGuid(), Id, quantity));
}

public sealed class EntryLine
{
    public EntryLine(Guid id, EntryOrderId orderId, int quantity)
    {
        Id = id;
        OrderId = orderId;
        Quantity = quantity;
    }

    private EntryLine() { OrderId = null!; }

    public Guid Id { get; private set; }

    public EntryOrderId OrderId { get; private set; }

    public int Quantity { get; set; }
}

/// <summary>An aggregate with a raw <see cref="Guid"/> key, for <c>UseUuidV7Keys()</c>.</summary>
public sealed class EntryNote : AggregateRoot<Guid>
{
    public EntryNote(string text, IClock clock) : base(Guid.Empty, clock) => Text = text;

    private EntryNote() { }

    public string Text { get; private set; } = string.Empty;
}

public sealed class EntryTenantedItem : AggregateRoot<EntryOrderId>, IHasTenant
{
    public EntryTenantedItem(EntryOrderId id, Guid tenantId, string name, IClock clock) : base(id, clock)
    {
        TenantId = tenantId;
        Name = name;
    }

    private EntryTenantedItem() { }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;
}

public sealed class EntryOrderContext(DbContextOptions<EntryOrderContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<EntryOrder> Orders => Set<EntryOrder>();

    public DbSet<EntryNote> Notes => Set<EntryNote>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<EntryOrder>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).IsRequired();
        modelBuilder.Entity<EntryOrder>().Navigation(o => o.Lines).HasField("_lines");
    }
}

public sealed class EntryTenantedContext(DbContextOptions<EntryTenantedContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<EntryTenantedItem> Items => Set<EntryTenantedItem>();

    protected override bool ShouldApplyConfiguration(Type configurationType) => false;
}

/// <summary>Seeds one row per tenant — only possible because seeders run inside a cross-tenant scope.</summary>
public sealed class TwoTenantSeeder : IDataSeeder<EntryTenantedContext>
{
    public static readonly Guid TenantA = Guid.NewGuid();
    public static readonly Guid TenantB = Guid.NewGuid();

    public async Task SeedAsync(EntryTenantedContext context, CancellationToken cancellationToken = default)
    {
        var clock = new SystemClock();
        context.Items.Add(new EntryTenantedItem(EntryOrderId.New(), TenantA, "a", clock));
        context.Items.Add(new EntryTenantedItem(EntryOrderId.New(), TenantB, "b", clock));
        await context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// The one-line entry point against a real PostgreSQL: <c>ConnectionStrings:{name}</c>, conventions (strongly
/// typed ids, <c>xmin</c> on an aggregate root without any configuration), expected-version checks, aggregate
/// touch on PostgreSQL, UUID v7 keys, several contexts, startup validation and cross-tenant seeding.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class PostgresEntryPointTests(PostgreSqlContainerFixture fixture)
{
    private string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"sk_e1_{Guid.NewGuid():N}" }.ConnectionString;

    private static IConfiguration Configuration(params (string Name, string ConnectionString)[] connections) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(connections.ToDictionary(c => $"ConnectionStrings:{c.Name}", c => (string?)c.ConnectionString))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static void Quiet<TContext>(EfCorePersistenceBuilder<TContext> p)
        where TContext : SharedKernelDbContext =>
        p.ConfigureDbContext((_, o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

    private static async Task<ServiceProvider> OrdersAsync(string connectionString, Action<EfCorePersistenceBuilder<EntryOrderContext>>? configure = null)
    {
        var configuration = Configuration(("orders", connectionString));
        var provider = Build(configuration, s => s.AddSharedKernelPostgres<EntryOrderContext>(configuration, "orders", p =>
        {
            Quiet(p);
            configure?.Invoke(p);
        }));

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EntryOrderContext>().Database.EnsureCreatedAsync();
        return provider;
    }

    [Fact]
    public async Task OneLine_ReadsConnectionStringsByName_AndEveryAggregateRootGetsAnXminVersion()
    {
        await using var provider = await OrdersAsync(NewDatabase());
        var id = EntryOrderId.New();

        EntityVersion afterInsert;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = new EntryOrder(id, "first", new SystemClock());
            db.Orders.Add(order);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            afterInsert = ConcurrencyVersion.Get(db, order);
        }

        afterInsert.Should().NotBe(EntityVersion.None, "the shadow xmin token is read back after the insert");

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = await db.Orders.SingleAsync(o => o.Id == id);
            ConcurrencyVersion.Get(db, order).Should().Be(afterInsert);

            order.Name = "second";
            await db.SaveChangesAsync();
            ConcurrencyVersion.Get(db, order).Should().NotBe(afterInsert, "PostgreSQL assigns a new xmin on update");
        }
    }

    [Fact]
    public async Task Version_OfAnUntrackedAggregate_IsRefused_NeverReportedAsZero()
    {
        // Found by the BillingApi sample: GET via IReadRepository + ConcurrencyVersion.Get answered ETag "0", and the
        // client's following If-Match then failed every time with 412.
        await using var provider = await OrdersAsync(NewDatabase());
        var id = EntryOrderId.New();

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EntryOrderContext>().Orders.Add(new EntryOrder(id, "first", new SystemClock()));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var untracked = await scope.ServiceProvider.GetRequiredService<IReadRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(id);

            var act = () => ConcurrencyVersion.Get(db, untracked!);
            act.Should().Throw<InvalidOperationException>().WithMessage("*not tracked*IRepository*");

            var tracked = await scope.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(id);
            ConcurrencyVersion.Get(db, tracked!).Should().NotBe(EntityVersion.None);
        }
    }

    [Fact]
    public async Task ExpectedVersion_Stale_IsAConflictCarryingTheCurrentVersion()
    {
        await using var provider = await OrdersAsync(NewDatabase());
        var id = EntryOrderId.New();

        EntityVersion original;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = new EntryOrder(id, "v1", new SystemClock());
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            original = ConcurrencyVersion.Get(db, order);
        }

        EntityVersion current;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = await db.Orders.SingleAsync(o => o.Id == id);
            order.Name = "v2 by someone else";
            await db.SaveChangesAsync();
            current = ConcurrencyVersion.Get(db, order);
        }

        // The client read "v1" (ETag = original) and now sends a change with If-Match: original.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = await db.Orders.SingleAsync(o => o.Id == id);
            order.Name = "v2 by the stale client";

            ConcurrencyVersion.SetExpected(db, order, original);
            var act = () => db.SaveChangesAsync();

            var conflict = (await act.Should().ThrowAsync<ConflictException>()).Which;
            conflict.Error.Code.Should().Be(ConcurrencyVersion.ConflictErrorCode);
            ConcurrencyVersion.TryGetCurrentVersion(conflict, out var reported).Should().BeTrue();
            reported.Should().Be(current);
        }

        // Unchanged entity + stale expected version: reported immediately, nothing to write.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = await db.Orders.SingleAsync(o => o.Id == id);
            var act = () => ConcurrencyVersion.SetExpected(db, order, original);
            act.Should().Throw<ConflictException>();
        }

        // Matching version: accepted.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = await db.Orders.SingleAsync(o => o.Id == id);
            order.Name = "v3";
            ConcurrencyVersion.SetExpected(db, order, current);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ChildOnlyEdit_ConflictsWithAConcurrentRootEdit()
    {
        // M5 on PostgreSQL: the child's change touches the root, so the root's stale xmin is detected.
        await using var provider = await OrdersAsync(NewDatabase());
        var id = EntryOrderId.New();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
            var order = new EntryOrder(id, "o", new SystemClock());
            order.AddLine(1);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using var childEditor = provider.CreateAsyncScope();
        var childDb = childEditor.ServiceProvider.GetRequiredService<EntryOrderContext>();
        var childView = await childDb.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == id);

        await using (var rootEditor = provider.CreateAsyncScope())
        {
            var rootDb = rootEditor.ServiceProvider.GetRequiredService<EntryOrderContext>();
            (await rootDb.Orders.SingleAsync(o => o.Id == id)).Name = "renamed";
            await rootDb.SaveChangesAsync();
        }

        childView.Lines.Single().Quantity = 9;
        var act = () => childDb.SaveChangesAsync();

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UseUuidV7Keys_GeneratesTimeOrderedGuidKeys()
    {
        await using var provider = await OrdersAsync(NewDatabase(), p => p.UseUuidV7Keys());
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();

        var note = new EntryNote("hello", new SystemClock());
        db.Notes.Add(note);
        await db.SaveChangesAsync();

        note.Id.Should().NotBe(Guid.Empty);
        note.Id.Version.Should().Be(7);
    }

    [Fact]
    public async Task TwoContexts_TwoConnectionNames_EachUsesItsOwnDatabase()
    {
        var ordersDb = NewDatabase();
        var itemsDb = NewDatabase();
        var configuration = Configuration(("orders", ordersDb), ("items", itemsDb));

        await using var provider = Build(configuration, s => s
            .AddSharedKernelPostgres<EntryOrderContext>(configuration, "orders", Quiet)
            .AddSharedKernelPostgres<EntryTenantedContext>(configuration, "items", Quiet));

        await using var scope = provider.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
        var items = scope.ServiceProvider.GetRequiredService<EntryTenantedContext>();

        new NpgsqlConnectionStringBuilder(orders.Database.GetConnectionString()).Database
            .Should().Be(new NpgsqlConnectionStringBuilder(ordersDb).Database);
        new NpgsqlConnectionStringBuilder(items.Database.GetConnectionString()).Database
            .Should().Be(new NpgsqlConnectionStringBuilder(itemsDb).Database);

        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeAssignableTo<IUnitOfWork<EntryOrderContext>>();
        scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(EntryTenantedContext))
            .Should().BeAssignableTo<IUnitOfWork<EntryTenantedContext>>();
    }

    [Fact]
    public async Task Seeder_OnATenantedContext_RunsInsideACrossTenantScope()
    {
        var configuration = Configuration(("items", NewDatabase()));
        await using var provider = Build(configuration, s => s.AddSharedKernelPostgres<EntryTenantedContext>(configuration, "items", p =>
        {
            Quiet(p);
            p.AddSeeder<TwoTenantSeeder>();
        }));

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EntryTenantedContext>().Database.EnsureCreatedAsync();

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        await using var check = provider.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<EntryTenantedContext>();
        var rows = await db.Items.IgnoreQueryFilters().ToListAsync();
        rows.Select(r => r.TenantId).Should().BeEquivalentTo([TwoTenantSeeder.TenantA, TwoTenantSeeder.TenantB]);
        rows.Should().OnlyContain(r => r.Name.Length == 1);
    }

    [Fact]
    public void StartupValidation_MissingConnectionString_FailsAtStartup_NamingTheSetting()
    {
        var configuration = Configuration();
        using var provider = Build(configuration, s => s.AddSharedKernelPostgres<EntryOrderContext>(configuration, "orders", Quiet));

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionStrings:orders*");
    }

    [Fact]
    public void StartupValidation_ValidRegistration_BuildsTheModelAtStartup()
    {
        var configuration = Configuration(("orders", NewDatabase()));
        using var provider = Build(configuration, s => s.AddSharedKernelPostgres<EntryOrderContext>(configuration, "orders", Quiet));

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task CallerDbContextFactory_IsSingletonSafe_AndAttachesTheExplicitCaller()
    {
        await using var provider = await OrdersAsync(NewDatabase());
        var factory = provider.GetRequiredService<ICallerDbContextFactory<EntryOrderContext>>();

        await using var db = await factory.CreateDbContextAsync(new SystemRequestContext([], "nightly-job"));
        var order = new EntryOrder(EntryOrderId.New(), "by job", new SystemClock());
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        order.CreatedBy.Should().Be("nightly-job");
    }
}
