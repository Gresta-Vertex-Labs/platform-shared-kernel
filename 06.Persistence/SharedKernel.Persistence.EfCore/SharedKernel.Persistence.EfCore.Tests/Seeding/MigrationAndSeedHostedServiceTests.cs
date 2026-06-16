using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Seeding;

/// <summary>
/// T-54: <see cref="IDataSeeder{TContext}"/>, <see cref="MigrationAndSeedHostedService{TContext}"/>,
/// <c>WithMigrationsOnStartup()</c>, and <c>AddSeeder&lt;TSeeder&gt;()</c> integration tests.
/// Uses SQLite in-memory for non-PostgreSQL scenarios (T-54 (5) advisory-lock test requires
/// a real PostgreSQL Testcontainer and is addressed separately in the PostgreSQL test project).
/// </summary>
public sealed class MigrationAndSeedHostedServiceTests
{
    private static IServiceCollection BuildSeedServices<TSeeder>(
        bool withMigrations = false)
        where TSeeder : class, IDataSeeder<SeedTestDbContext>
    {
        var services = new ServiceCollection();
        var builder = services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared"))
            .AddSeeder<TSeeder>();

        if (withMigrations)
            builder.WithMigrationsOnStartup();

        builder.Build();
        return services;
    }

    // -------------------------------------------------------------------------
    // T-54 (1): AddSeeder registers TSeeder as scoped + hosted service
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSeeder_RegistersSeeder_AsScopedService()
    {
        var services = BuildSeedServices<SeedTestSeeder>();

        var seederDescriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(SeedTestSeeder));

        seederDescriptor.Should().NotBeNull("AddSeeder must register TSeeder in DI");
        seederDescriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped,
            "seeder must be scoped so it receives a fresh TContext per run");
    }

    [Fact]
    public void AddSeeder_RegistersMigrationAndSeedHostedService()
    {
        var services = BuildSeedServices<SeedTestSeeder>();

        var hostedServiceDescriptor = services.Any(sd =>
            sd.ServiceType == typeof(IHostedService));

        hostedServiceDescriptor.Should().BeTrue(
            "AddSeeder must register MigrationAndSeedHostedService<TContext> as IHostedService");
    }

    [Fact]
    public async Task AddSeeder_StartingHost_InvokesSeedAsync_OncePerRegisteredSeeder()
    {
        // Arrange
        var callLog = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(callLog);
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared"))
            .AddSeeder<TrackingSeeder>()
            .Build();

        var provider = services.BuildServiceProvider();

        // Ensure the database exists before the hosted service runs
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        ctx.Database.EnsureCreated();

        // Act — run the hosted service
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        // Assert — TrackingSeeder.SeedAsync was called exactly once
        callLog.Should().ContainSingle()
            .Which.Should().Be("TrackingSeeder.SeedAsync",
                "SeedAsync must be called exactly once when the hosted service starts");
    }

    [Fact]
    public async Task AddSeeder_MultipleSeeder_RunInRegistrationOrder()
    {
        // Arrange
        var callLog = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(callLog);
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared"))
            .AddSeeder<FirstTrackingSeeder>()
            .AddSeeder<SecondTrackingSeeder>()
            .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        ctx.Database.EnsureCreated();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
            .Single();

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert — registration order preserved
        callLog.Should().HaveCount(2);
        callLog[0].Should().Be("FirstTrackingSeeder.SeedAsync");
        callLog[1].Should().Be("SecondTrackingSeeder.SeedAsync");
    }

    // -------------------------------------------------------------------------
    // T-54 (2): Seeder idempotency — running twice does not duplicate rows
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddSeeder_RunningHostTwice_DoesNotDuplicateRows_WhenSeederIsIdempotent()
    {
        // Arrange — use a shared SQLite connection so the in-memory DB persists across contexts
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts => opts.UseSqlite(connection))
            .AddSeeder<IdempotentSeedTestSeeder>()
            .Build();

        var provider = services.BuildServiceProvider();
        await using var initCtx = provider.GetRequiredService<SeedTestDbContext>();
        await initCtx.Database.EnsureCreatedAsync();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
            .Single();

        // Act — start twice (simulating two restarts)
        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        // Assert — exactly one row (seeder checks before inserting)
        await using var verifyCtx = provider.GetRequiredService<SeedTestDbContext>();
        var count = await verifyCtx.SeedItems.CountAsync();
        count.Should().Be(1, "idempotent seeder must not insert duplicate rows on second run");
    }

    // -------------------------------------------------------------------------
    // T-54 (4): Omitting both options → no hosted service registered
    // -------------------------------------------------------------------------

    [Fact]
    public void OmittingMigrationsAndSeeders_NoHostedServiceRegistered()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:"))
            .Build(); // no .WithMigrationsOnStartup() or .AddSeeder<T>()

        var hostedServiceDescriptor = services.Any(sd =>
            sd.ServiceType == typeof(IHostedService));

        hostedServiceDescriptor.Should().BeFalse(
            "no IHostedService must be registered when neither WithMigrationsOnStartup() " +
            "nor AddSeeder<T>() was called — fully opt-in, zero overhead");
    }

    [Fact]
    public void WithMigrationsOnStartup_Alone_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:"))
            .WithMigrationsOnStartup()
            .Build();

        var hostedServiceDescriptor = services.Any(sd =>
            sd.ServiceType == typeof(IHostedService));

        hostedServiceDescriptor.Should().BeTrue(
            "WithMigrationsOnStartup() alone must register the hosted service");
    }
}

// ---------------------------------------------------------------------------
// Test-local entity, ID, configuration, DbContext, and seeders
// ---------------------------------------------------------------------------

internal sealed record SeedItemId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static SeedItemId New() => new(Guid.NewGuid());
}

internal sealed class SeedItem : AggregateRoot<SeedItemId>
{
    public string Name { get; private set; } = string.Empty;

    public SeedItem(SeedItemId id, string name, IClock clock) : base(id, clock)
    {
        Name = name;
    }

    protected SeedItem() { }
}

internal sealed class SeedItemConfig : EntityTypeConfigurationBase<SeedItem, SeedItemId>
{
    public override void Configure(EntityTypeBuilder<SeedItem> builder)
    {
        base.Configure(builder);
        builder.ToTable("seed_items");
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
    }
}

/// <summary>Test DbContext with a single entity for seeder tests.</summary>
internal sealed class SeedTestDbContext : SharedKernelDbContext
{
    public DbSet<SeedItem> SeedItems => Set<SeedItem>();

    public SeedTestDbContext(
        DbContextOptions<SeedTestDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency)
        : base(options, audit, softDelete, concurrency)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<SeedItemId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SeedItemConfig());
    }
}

/// <summary>Simple test seeder that inserts a fixed row.</summary>
internal sealed class SeedTestSeeder : IDataSeeder<SeedTestDbContext>
{
    public async Task SeedAsync(SeedTestDbContext context, CancellationToken ct)
    {
        if (!await context.SeedItems.AnyAsync(ct))
        {
            context.SeedItems.Add(new SeedItem(SeedItemId.New(), "SeedData", new SystemClock()));
            await context.SaveChangesAsync(ct);
        }
    }
}

/// <summary>Seeder that records calls via injected call log.</summary>
internal sealed class TrackingSeeder(List<string> callLog) : IDataSeeder<SeedTestDbContext>
{
    public Task SeedAsync(SeedTestDbContext context, CancellationToken ct)
    {
        callLog.Add("TrackingSeeder.SeedAsync");
        return Task.CompletedTask;
    }
}

internal sealed class FirstTrackingSeeder(List<string> callLog) : IDataSeeder<SeedTestDbContext>
{
    public Task SeedAsync(SeedTestDbContext context, CancellationToken ct)
    {
        callLog.Add("FirstTrackingSeeder.SeedAsync");
        return Task.CompletedTask;
    }
}

internal sealed class SecondTrackingSeeder(List<string> callLog) : IDataSeeder<SeedTestDbContext>
{
    public Task SeedAsync(SeedTestDbContext context, CancellationToken ct)
    {
        callLog.Add("SecondTrackingSeeder.SeedAsync");
        return Task.CompletedTask;
    }
}

/// <summary>Idempotent seeder — inserts only once, checked by Name uniqueness.</summary>
internal sealed class IdempotentSeedTestSeeder : IDataSeeder<SeedTestDbContext>
{
    private const string SeedName = "UniqueRow";

    public async Task SeedAsync(SeedTestDbContext context, CancellationToken ct)
    {
        // Idempotency check — do not insert if the row already exists
        if (!await context.SeedItems.AnyAsync(e => e.Name == SeedName, ct))
        {
            context.SeedItems.Add(new SeedItem(SeedItemId.New(), SeedName, new SystemClock()));
            await context.SaveChangesAsync(ct);
        }
    }
}
