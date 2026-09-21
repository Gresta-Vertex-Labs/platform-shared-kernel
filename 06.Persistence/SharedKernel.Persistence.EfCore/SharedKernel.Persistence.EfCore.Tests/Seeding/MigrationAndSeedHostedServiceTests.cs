using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Extensions;
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
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        var builder = services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
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
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
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
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
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
            .AddSharedKernelEfCore<SeedTestDbContext>(opts => opts.UseSqlite(connection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
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
                opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .Build(); // no.WithMigrationsOnStartup() or.AddSeeder<T>()

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
                opts.UseSqlite("DataSource=:memory:").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .WithMigrationsOnStartup()
                        .Build();

        var hostedServiceDescriptor = services.Any(sd =>
            sd.ServiceType == typeof(IHostedService));

        hostedServiceDescriptor.Should().BeTrue(
            "WithMigrationsOnStartup() alone must register the hosted service");
    }

    // -------------------------------------------------------------------------
    // IMigrationLock acquire/release proof, replacing the former raw
    // pg_advisory_lock SQL proof, which moved to SharedKernel.Persistence.Npgsql.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_MigrationLockRegistered_AcquiresBeforeSeedingAndReleasesAfter()
    {
        var fakeLock = new FakeMigrationLock();
        var services = new ServiceCollection();
        services.AddSingleton<IMigrationLock>(fakeLock);
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .AddSeeder<SeedTestSeeder>()
                        .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
                .Single();

        await hostedService.StartAsync(CancellationToken.None);

        fakeLock.AcquireCallCount.Should().Be(1);
        fakeLock.LastLockKey.Should().Contain(nameof(SeedTestDbContext));
        fakeLock.LastHandle!.DisposeCallCount.Should().Be(1,
            "the lock handle must be released (disposed) exactly once after seeding completes");
    }

    [Fact]
    public async Task StartAsync_SeederThrows_StillReleasesMigrationLock()
    {
        var fakeLock = new FakeMigrationLock();
        var services = new ServiceCollection();
        services.AddSingleton<IMigrationLock>(fakeLock);
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .AddSeeder<ThrowingSeeder>()
                        .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
                .Single();

        var act = async () => await hostedService.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fakeLock.LastHandle!.DisposeCallCount.Should().Be(1,
            "the lock must be released even when a seeder throws");
    }

    [Fact]
    public async Task StartAsync_NoMigrationLockRegistered_StillSeeds_WithoutThrowing()
    {
        // A missing IMigrationLock is a loud Error-level LOG (proven in
        // MigrationAndSeedHostedServiceLoggingTests), never a hard failure — a deliberately
        // single-replica or non-PostgreSQL deployment must still start up successfully.
        var services = new ServiceCollection();
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
                    .AddSeeder<SeedTestSeeder>()
                        .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
                .Single();

        var act = async () => await hostedService.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();

        await using var verifyCtx = provider.GetRequiredService<SeedTestDbContext>();
        (await verifyCtx.SeedItems.CountAsync()).Should().Be(1);
    }
}

// ---------------------------------------------------------------------------
// A fake IMigrationLock recording acquire/release calls.
// ---------------------------------------------------------------------------

internal sealed class FakeMigrationLock : SharedKernel.Persistence.Abstractions.Coordination.IMigrationLock
{
    public int AcquireCallCount { get; private set; }
    public string? LastLockKey { get; private set; }
    public FakeMigrationLockHandle? LastHandle { get; private set; }

    public Task<IAsyncDisposable> AcquireAsync(string lockKey, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        AcquireCallCount++;
        LastLockKey = lockKey;
        LastHandle = new FakeMigrationLockHandle();
        return Task.FromResult<IAsyncDisposable>(LastHandle);
    }
}

internal sealed class FakeMigrationLockHandle : IAsyncDisposable
{
    public int DisposeCallCount { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        return ValueTask.CompletedTask;
    }
}

// ThrowingSeeder is defined in MigrationAndSeedHostedServiceLoggingTests.cs (same namespace).

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

    private SeedItem() { }
}

internal sealed class SeedItemConfig : IEntityTypeConfiguration<SeedItem>
{
    public void Configure(EntityTypeBuilder<SeedItem> builder)
    {
        builder.HasKey("Id");
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
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
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
