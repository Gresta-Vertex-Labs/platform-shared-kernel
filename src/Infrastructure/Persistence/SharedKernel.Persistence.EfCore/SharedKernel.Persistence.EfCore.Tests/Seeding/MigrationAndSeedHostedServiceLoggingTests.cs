using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Seeding;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Tests.Seeding;

/// <summary>
/// <see cref="MigrationAndSeedHostedService{TContext}"/>'s lifecycle
/// <c>[LoggerMessage]</c> logs (EventIds <c>6001</c>-<c>6006</c>).
/// </summary>
public sealed class MigrationAndSeedHostedServiceLoggingTests
{
    [Fact]
    public async Task StartAsync_Success_LogsStartedThenPerSeederThenCompleted_InOrder()
    {
        // Arrange
        var inMemoryLogger = new InMemoryLogger<MigrationAndSeedHostedService<SeedTestDbContext>>();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<MigrationAndSeedHostedService<SeedTestDbContext>>>(inMemoryLogger);
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .AddSeeder<SeedTestSeeder>()
            .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        ctx.Database.EnsureCreated();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
            .Single();

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        var records = inMemoryLogger.Records;
        records.ShouldHaveLogged(new EventId(6001), LogLevel.Information);
        records.ShouldHaveLoggedWithProperty(new EventId(6002), "SeederType", nameof(SeedTestSeeder));
        records.ShouldHaveLogged(new EventId(6003), LogLevel.Information);
        records.ShouldNotHaveLogged(new EventId(6004));

        var ordered = records.ToList();
        var startedIndex = ordered.FindIndex(r => r.EventId.Id == 6001);
        var seederIndex = ordered.FindIndex(r => r.EventId.Id == 6002);
        var completedIndex = ordered.FindIndex(r => r.EventId.Id == 6003);
        startedIndex.Should().BeLessThan(seederIndex);
        seederIndex.Should().BeLessThan(completedIndex);
    }

    [Fact]
    public async Task StartAsync_SeederThrows_LogsFailureWarning_NeverLogsCompleted()
    {
        // Arrange
        var inMemoryLogger = new InMemoryLogger<MigrationAndSeedHostedService<SeedTestDbContext>>();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<MigrationAndSeedHostedService<SeedTestDbContext>>>(inMemoryLogger);
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .AddSeeder<ThrowingSeeder>()
            .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        ctx.Database.EnsureCreated();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
            .Single();

        // Act
        Func<Task> act = () => hostedService.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        var records = inMemoryLogger.Records;
        records.ShouldHaveLogged(new EventId(6004), LogLevel.Warning);
        records.ShouldNotHaveLogged(new EventId(6003));
    }

    [Fact]
    public async Task StartAsync_MigrationLockConfigured_LogsAcquiredAndReleased_EvenWhenSeederThrows()
    {
        // Arrange — a fake IMigrationLock, sufficient to prove the
        // AdvisoryLockAcquired/Released logging fires around a seeder failure. The concrete
        // PostgreSQL advisory-lock implementation moved to SharedKernel.Persistence.Npgsql.
        var migrationLock = new FakeMigrationLock();

        var inMemoryLogger = new InMemoryLogger<MigrationAndSeedHostedService<SeedTestDbContext>>();

        var services = new ServiceCollection();
        services.AddSingleton<IMigrationLock>(migrationLock);
        services.AddSingleton<ILogger<MigrationAndSeedHostedService<SeedTestDbContext>>>(inMemoryLogger);
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .AddSeeder<ThrowingSeeder>()
            .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        ctx.Database.EnsureCreated();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
            .Single();

        // Act
        Func<Task> act = () => hostedService.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        var records = inMemoryLogger.Records;
        records.ShouldHaveLogged(new EventId(6005), LogLevel.Information); // AdvisoryLockAcquired
        records.ShouldHaveLogged(new EventId(6006), LogLevel.Information); // AdvisoryLockReleased — proves the
                                                                            // new logging does not disturb the
                                                                            // existing finally-block release ordering.
        records.ShouldHaveLogged(new EventId(6004), LogLevel.Warning);
        records.ShouldNotHaveLogged(new EventId(6013)); // NoMigrationLockRegistered — a lock WAS registered.

        // Each fires exactly once per StartAsync call — never duplicated across the
        // acquire/seed-failure/release sequence.
        records.ShouldHaveLoggedCount(new EventId(6005), 1);
        records.ShouldHaveLoggedCount(new EventId(6006), 1);
    }

    [Fact]
    public async Task StartAsync_NoMigrationLockRegistered_LogsErrorLevelWarning()
    {
        // Fail loudly (Error-level log) when no coordination lock is registered,
        // rather than silently racing multiple replicas.
        var inMemoryLogger = new InMemoryLogger<MigrationAndSeedHostedService<SeedTestDbContext>>();

        var services = new ServiceCollection();
        services.AddSingleton<ILogger<MigrationAndSeedHostedService<SeedTestDbContext>>>(inMemoryLogger);
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        services
            .AddSharedKernelEfCore<SeedTestDbContext>(opts =>
                opts.UseSqlite(connectionString)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .AddSeeder<SeedTestSeeder>()
            .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<SeedTestDbContext>();
        ctx.Database.EnsureCreated();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<MigrationAndSeedHostedService<SeedTestDbContext>>()
                .Single();

        await hostedService.StartAsync(CancellationToken.None);

        inMemoryLogger.Records.ShouldHaveLogged(new EventId(6013), LogLevel.Error);
    }
}

internal sealed class ThrowingSeeder : IDataSeeder<SeedTestDbContext>
{
    public Task SeedAsync(SeedTestDbContext context, CancellationToken ct)
        => throw new InvalidOperationException("Simulated seeder failure.");
}
