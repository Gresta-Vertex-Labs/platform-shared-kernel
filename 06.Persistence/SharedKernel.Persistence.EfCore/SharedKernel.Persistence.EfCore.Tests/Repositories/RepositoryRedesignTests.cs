using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Tests.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

// ---------------------------------------------------------------------------
// P-558 (D3/D7): concrete repositories, tracking split, whole-aggregate GetById, inline specifications,
// call-site paging, keyset paging (A27), open-generic registration over several contexts.
// ---------------------------------------------------------------------------

public sealed record ShopOrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ShopOrderId New() => new(Guid.NewGuid());
}

public sealed record ShopInvoiceId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ShopInvoiceId New() => new(Guid.NewGuid());
}

public sealed class ShopProduct
{
    public ShopProduct(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    private ShopProduct() { }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;
}

public sealed class ShopOrderLine
{
    public ShopOrderLine(Guid id, ShopProduct product)
    {
        Id = id;
        Product = product;
    }

    private ShopOrderLine() { }

    public Guid Id { get; private set; }

    public ShopProduct Product { get; private set; } = null!;
}

public sealed class ShopOrder : AggregateRoot<ShopOrderId>
{
    public ShopOrder(ShopOrderId id, string name, int number, IClock clock) : base(id, clock)
    {
        Name = name;
        Number = number;
    }

    private ShopOrder() { }

    public string Name { get; private set; } = string.Empty;

    public int Number { get; private set; }

    public List<ShopOrderLine> Lines { get; private set; } = [];

    public void Rename(string name) => Name = name;
}

public sealed class ShopInvoiceLine
{
    public ShopInvoiceLine(Guid id, decimal amount)
    {
        Id = id;
        Amount = amount;
    }

    private ShopInvoiceLine() { }

    public Guid Id { get; private set; }

    public decimal Amount { get; private set; }
}

public sealed class ShopInvoice : AggregateRoot<ShopInvoiceId>
{
    public ShopInvoice(ShopInvoiceId id, IClock clock) : base(id, clock) { }

    private ShopInvoice() { }

    public List<ShopInvoiceLine> Lines { get; private set; } = [];
}

public sealed class ShopOrderConfig : EntityTypeConfigurationBase<ShopOrder, ShopOrderId>
{
    public override void Configure(EntityTypeBuilder<ShopOrder> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey("OrderId").IsRequired();
    }
}

public sealed class ShopInvoiceConfig : EntityTypeConfigurationBase<ShopInvoice, ShopInvoiceId>
{
    public override void Configure(EntityTypeBuilder<ShopInvoice> builder)
    {
        base.Configure(builder);
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey("InvoiceId").IsRequired();
        builder.Navigation(e => e.Lines).AutoInclude();
    }
}

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<ShopOrder> Orders => Set<ShopOrder>();

    public DbSet<ShopInvoice> Invoices => Set<ShopInvoice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ShopOrderId, Guid>();
        configurationBuilder.ConfigureStronglyTypedId<ShopInvoiceId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ShopOrderConfig());
        modelBuilder.ApplyConfiguration(new ShopInvoiceConfig());
        modelBuilder.Entity<ShopOrderLine>().HasOne(l => l.Product).WithMany().HasForeignKey("ProductId");
    }

    public static ShopDbContext Create()
    {
        var clock = new SystemClock();
        var options = new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlite("DataSource=:memory:")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var context = new ShopDbContext(options, new PersistenceContextDependencies(
            new AuditInterceptor(new SharedKernel.Testing.Persistence.FakeAuditActorContext(), clock),
            new SoftDeleteInterceptor(clock),
            new ConcurrencyInterceptor()));

        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }
}

/// <summary>A second context that maps <see cref="ShopOrder"/> too, for the ambiguity check.</summary>
public sealed class ShopMirrorDbContext(DbContextOptions<ShopMirrorDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<ShopOrderId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ShopOrderConfig());
        modelBuilder.Entity<ShopOrderLine>().HasOne(l => l.Product).WithMany().HasForeignKey("ProductId");
    }

    public static ShopMirrorDbContext Create()
    {
        var clock = new SystemClock();
        var options = new DbContextOptionsBuilder<ShopMirrorDbContext>()
            .UseSqlite("DataSource=:memory:")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new ShopMirrorDbContext(options, new PersistenceContextDependencies(
            new AuditInterceptor(new SharedKernel.Testing.Persistence.FakeAuditActorContext(), clock),
            new SoftDeleteInterceptor(clock),
            new ConcurrencyInterceptor()));
    }
}

/// <summary>Overrides the aggregate-query hook so GetByIdAsync loads Lines.Product.</summary>
internal sealed class ShopOrderRepository(ShopDbContext context) : EfRepository<ShopOrder, ShopOrderId>(context)
{
    protected override IQueryable<ShopOrder> AggregateQuery() =>
        base.AggregateQuery().Include(o => o.Lines).ThenInclude(l => l.Product);
}

public sealed class RepositoryRedesignTests
{
    private static readonly SystemClock Clock = new();

    private static async Task<(ShopDbContext Context, List<ShopOrder> Orders)> SeedAsync(int count = 5, Func<int, int>? number = null)
    {
        var context = ShopDbContext.Create();
        var orders = new List<ShopOrder>();
        for (var i = 1; i <= count; i++)
        {
            var order = new ShopOrder(ShopOrderId.New(), $"Order {i}", number?.Invoke(i) ?? i, Clock);
            order.Lines.Add(new ShopOrderLine(Guid.NewGuid(), new ShopProduct(Guid.NewGuid(), $"Product {i}")));
            orders.Add(order);
        }

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (context, orders);
    }

    // ---- tracking ----

    [Fact]
    public async Task ReadRepository_NeverTracks()
    {
        var (context, orders) = await SeedAsync();
        using var _ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        (await repo.GetByIdAsync(orders[0].Id)).Should().NotBeNull();
        (await repo.FirstOrDefaultAsync(Spec.For<ShopOrder>().Where(o => o.Number == 2))).Should().NotBeNull();
        (await repo.ListAsync(Spec.For<ShopOrder>())).Should().HaveCount(5);
        (await repo.GetByIdsAsync([orders[0].Id, orders[1].Id])).Should().HaveCount(2);

        context.ChangeTracker.Entries().Should().BeEmpty("the read repository must never track");
    }

    [Fact]
    public async Task WriteRepository_TracksItsFetches_AndSavesChangesWithoutUpdate()
    {
        var (context, orders) = await SeedAsync();
        using var _ = context;
        var repo = new EfRepository<ShopOrder, ShopOrderId>(context);

        var order = await repo.GetByIdAsync(orders[0].Id);
        var listed = await repo.ListAsync(Spec.For<ShopOrder>().Where(o => o.Number > 3));

        context.Entry(order!).State.Should().Be(EntityState.Unchanged);
        listed.Should().OnlyContain(o => context.Entry(o).State == EntityState.Unchanged);

        order!.Rename("Renamed");
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        (await context.Orders.SingleAsync(o => o.Id == orders[0].Id)).Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task WriteRepository_ThroughTheReadContract_StaysUntracked()
    {
        var (context, orders) = await SeedAsync();
        using var _ = context;
        IReadRepository<ShopOrder, ShopOrderId> repo = new EfRepository<ShopOrder, ShopOrderId>(context);

        await repo.GetByIdAsync(orders[0].Id);
        await repo.ListAsync(Spec.For<ShopOrder>());

        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task WriteRepository_TracksEvenWhenTheContextDefaultIsNoTracking()
    {
        var (context, orders) = await SeedAsync();
        using var _ = context;
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var repo = new EfRepository<ShopOrder, ShopOrderId>(context);

        var order = await repo.GetByIdAsync(orders[0].Id);

        context.Entry(order!).State.Should().Be(EntityState.Unchanged);
    }

    // ---- whole aggregate ----

    [Fact]
    public async Task GetByIdAsync_LoadsAutoIncludedNavigations()
    {
        using var context = ShopDbContext.Create();
        var invoice = new ShopInvoice(ShopInvoiceId.New(), Clock);
        invoice.Lines.Add(new ShopInvoiceLine(Guid.NewGuid(), 10m));
        invoice.Lines.Add(new ShopInvoiceLine(Guid.NewGuid(), 20m));
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loaded = await new EfReadRepository<ShopInvoice, ShopInvoiceId>(context).GetByIdAsync(invoice.Id);

        loaded!.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_UsesTheOverridableAggregateQueryHook()
    {
        var (context, orders) = await SeedAsync();
        using var _ = context;

        var loaded = await new ShopOrderRepository(context).GetByIdAsync(orders[2].Id);

        loaded!.Lines.Should().ContainSingle().Which.Product.Name.Should().Be("Product 3");
    }

    // ---- inline specification translation ----

    [Fact]
    public async Task InlineSpec_IncludeThenInclude_LoadsTheWholePath()
    {
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        var spec = Spec.For<ShopOrder>()
            .Where(o => o.Number >= 2)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .OrderByDescending(o => o.Number)
            .Take(2)
            .AsSplitQuery();

        var result = await repo.ListAsync(spec);

        result.Select(o => o.Number).Should().Equal(5, 4);
        result.Should().OnlyContain(o => o.Lines.Count == 1 && o.Lines[0].Product.Name.StartsWith("Product"));
    }

    [Fact]
    public async Task InlineSpec_Select_ProjectsInSql()
    {
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        var names = await repo.ListProjectedAsync(
            Spec.For<ShopOrder>().Where(o => o.Number <= 2).OrderBy(o => o.Number).Select(o => o.Name));

        names.Should().Equal("Order 1", "Order 2");
    }

    // ---- offset paging at the call site ----

    [Fact]
    public async Task ListPagedAsync_PagesAtTheCallSite()
    {
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        var page = await repo.ListPagedAsync(
            Spec.For<ShopOrder>().OrderBy(o => o.Number), PageRequest.Create(page: 2, pageSize: 2).Value);

        page.Items.Select(o => o.Number).Should().Equal(3, 4);
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(2);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task ListPagedProjectedAsync_PagesAtTheCallSite()
    {
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        var page = await repo.ListPagedProjectedAsync(
            Spec.For<ShopOrder>().OrderByDescending(o => o.Number).Select(o => o.Number),
            PageRequest.Create(page: 3, pageSize: 2).Value);

        page.Items.Should().Equal(1);
        page.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task ListPagedAsync_RejectsUnorderedAndSelfPagedSpecifications()
    {
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        await FluentActions.Awaiting(() => repo.ListPagedAsync(Spec.For<ShopOrder>(), PageRequest.First))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*primary sort*");
        await FluentActions.Awaiting(() => repo.ListPagedAsync(Spec.For<ShopOrder>().OrderBy(o => o.Number).Take(3), PageRequest.First))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Skip/Take*");
    }

    // ---- keyset paging at the call site ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListKeysetAsync_WalksEveryRowExactlyOnce_WithTiesOnTheKey(bool descending)
    {
        // Numbers 1,1,2,2,3,3,4: the identity breaks ties, so no row is skipped or repeated.
        var (context, orders) = await SeedAsync(count: 7, number: i => (i + 1) / 2);
        using var _ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        var seen = new List<ShopOrder>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await repo.ListKeysetAsync(
                Spec.For<ShopOrder>(), CursorPageRequest.Create(cursor, limit: 2).Value, o => o.Number, descending);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        pages.Should().Be(4);
        seen.Select(o => o.Id).Should().OnlyHaveUniqueItems().And.HaveCount(orders.Count);
        var numbers = seen.Select(o => o.Number).ToList();
        numbers.Should().Equal(descending ? numbers.OrderByDescending(n => n) : numbers.OrderBy(n => n));
    }

    [Fact]
    public async Task ListKeysetAsync_AppliesTheSpecificationFilter()
    {
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        var page = await repo.ListKeysetAsync(
            Spec.For<ShopOrder>().Where(o => o.Number % 2 == 1), CursorPageRequest.First, o => o.Number);

        page.Items.Select(o => o.Number).Should().Equal(1, 3, 5);
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ListKeysetAsync_RejectsAMalformedCursor_AndAnOrderedSpecification()
    {
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        await FluentActions.Awaiting(() => repo.ListKeysetAsync(
                Spec.For<ShopOrder>(), CursorPageRequest.Create("v1.not-a-cursor").Value, o => o.Number))
            .Should().ThrowAsync<ValidationException>();

        await FluentActions.Awaiting(() => repo.ListKeysetAsync(
                Spec.For<ShopOrder>().OrderBy(o => o.Name), CursorPageRequest.First, o => o.Number))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ordering*");
    }

    [Fact]
    public async Task ListKeysetProjectedAsync_ProjectsServerSide_AndPagesByTheAggregateKey()
    {
        // A27 regression: the projection used to run in memory with a Compile() per call. EF.Functions.Like
        // cannot run in memory, so this only succeeds when the projection is translated to SQL.
        var (context, _) = await SeedAsync();
        using var __ = context;
        var repo = new EfReadRepository<ShopOrder, ShopOrderId>(context);
        var spec = Spec.For<ShopOrder>().Select(o => new { o.Name, Odd = EF.Functions.Like(o.Name, "%1") || EF.Functions.Like(o.Name, "%3") });

        var first = await repo.ListKeysetProjectedAsync(spec, CursorPageRequest.Create(limit: 3).Value, o => o.Number);
        var second = await repo.ListKeysetProjectedAsync(spec, CursorPageRequest.Create(first.NextCursor, 3).Value, o => o.Number);

        first.Items.Select(r => r.Name).Should().Equal("Order 1", "Order 2", "Order 3");
        first.Items.Select(r => r.Odd).Should().Equal(true, false, true);
        second.Items.Select(r => r.Name).Should().Equal("Order 4", "Order 5");
        second.HasMore.Should().BeFalse();
    }

    [Fact]
    public void KeyAccessor_IsCompiledOncePerKeyPath()
    {
        // A27 regression: no Compile() per call.
        var first = RepositoryExpressions<ShopOrder, ShopOrderId>.KeyAccessor(o => o.Number);
        var second = RepositoryExpressions<ShopOrder, ShopOrderId>.KeyAccessor(x => x.Number);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task KeysetCursor_FromTheFakeRepository_DecodesAgainstTheRealRepository()
    {
        var (context, orders) = await SeedAsync();
        using var __ = context;
        var fake = new SharedKernel.Testing.Persistence.FakeRepository<ShopOrder, ShopOrderId>(o => o.Id, orders);
        var real = new EfReadRepository<ShopOrder, ShopOrderId>(context);

        var fakeFirst = await fake.ListKeysetAsync(Spec.For<ShopOrder>(), CursorPageRequest.Create(limit: 2).Value, o => o.Number);
        var realSecond = await real.ListKeysetAsync(Spec.For<ShopOrder>(), CursorPageRequest.Create(fakeFirst.NextCursor, 2).Value, o => o.Number);

        realSecond.Items.Select(o => o.Number).Should().Equal(3, 4);
    }

    // ---- expected version ----

    [Fact]
    public async Task UpdateAsync_WithExpectedVersion_RequiresAnXminRowVersion()
    {
        var (context, orders) = await SeedAsync();
        using var __ = context;
        var repo = new EfRepository<ShopOrder, ShopOrderId>(context);

        await FluentActions.Awaiting(() => repo.UpdateAsync(orders[0], expectedVersion: 7))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*xmin*");
    }

    // ---- open-generic registration ----

    private static ServiceProvider BuildProvider(bool withMirror = false)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => ShopDbContext.Create());
        services.AddScoped(_ => TestDbContextFactory.CreateTestDbContext());
        RepositoryRegistration.Register<ShopDbContext>(services);
        RepositoryRegistration.Register<TestDbContext>(services);
        RepositoryRegistration.Register<ShopDbContext>(services); // idempotent

        if (withMirror)
        {
            services.AddScoped(_ => ShopMirrorDbContext.Create());
            RepositoryRegistration.Register<ShopMirrorDbContext>(services);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task OpenGenericRepositories_ResolveTheContextThatMapsEachAggregate()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var orders = sp.GetRequiredService<IRepository<ShopOrder, ShopOrderId>>();
        var tests = sp.GetRequiredService<IReadRepository<TestAggregate, TestId>>();
        var bulk = sp.GetRequiredService<IBulkMutationRepository<ShopOrder, ShopOrderId>>();

        await orders.AddAsync(new ShopOrder(ShopOrderId.New(), "Via DI", 1, Clock));
        await sp.GetRequiredService<ShopDbContext>().SaveChangesAsync();

        (await orders.CountAsync(Spec.For<ShopOrder>())).Should().Be(1);
        (await tests.CountAsync(Spec.For<TestAggregate>())).Should().Be(0);
        (await bulk.ExecuteUpdateAsync(Spec.For<ShopOrder>().Where(o => o.Number == 1), s => s.SetProperty(o => o.Name, "Bulk")))
            .Should().Be(1);
        sp.GetRequiredService<IReadRepository<ShopOrder, ShopOrderId>>().Should().NotBeAssignableTo<IRepository<ShopOrder, ShopOrderId>>();
    }

    [Fact]
    public void OpenGenericRepositories_AggregateMappedByTwoContexts_FailsWithBothNames()
    {
        using var provider = BuildProvider(withMirror: true);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IRepository<ShopOrder, ShopOrderId>>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ShopDbContext*ShopMirrorDbContext*");
    }

    [Fact]
    public void OpenGenericRepositories_UnmappedAggregate_Fails()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IReadRepository<ParentEntity, ParentId>>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*No registered SharedKernelDbContext maps*");
    }

    [Fact]
    public void ClosedRegistration_WinsOverTheOpenGeneric()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => ShopDbContext.Create());
        services.AddScoped<IRepository<ShopOrder, ShopOrderId>, ShopOrderRepository>();
        RepositoryRegistration.Register<ShopDbContext>(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IRepository<ShopOrder, ShopOrderId>>().Should().BeOfType<ShopOrderRepository>();
    }
}
