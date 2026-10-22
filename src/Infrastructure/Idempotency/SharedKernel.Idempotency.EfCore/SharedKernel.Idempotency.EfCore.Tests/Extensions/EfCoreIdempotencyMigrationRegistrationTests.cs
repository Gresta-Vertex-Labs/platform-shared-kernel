using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Extensions;

/// <summary>
/// What <c>MigrateOnStartup</c> changes at registration, and proof that off (the default) registers what 1.0.0 did.
/// Nothing here connects to a database; the container-backed proofs are in the Postgres migration tests.
/// </summary>
public sealed class EfCoreIdempotencyMigrationRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sk_idempotency_test;Username=sk;Password=sk;Timeout=1";

    private const string HistoryTable = "__ef_migrations_history_idempotency";

    private static ServiceCollection Register(Action<EfCoreIdempotencyOptions>? configure, Action<DbContextOptionsBuilder>? db = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClock();
        services.AddEfCoreIdempotency(
            options =>
            {
                options.UsePostgres(TestNpgsqlDataSources.Get(ConnectionString));
                db?.Invoke(options);
            },
            p => p.ForRequests().ForMessages(),
            configure);
        return services;
    }

    private static IEnumerable<string> HostedServiceNames(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetServices<IHostedService>().Select(s => s.GetType().Name)];
    }

    private static string? HistoryTableOf(ServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdempotencyDbContext>();
        return context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single().MigrationsHistoryTableName;
    }

    [Fact]
    public void ByDefault_NoHostedServiceIsRegistered_AndTheContextKeepsItsOwnHistoryTableSetting()
    {
        var services = Register(configure: null);

        Assert.Empty(HostedServiceNames(services));
        Assert.Null(HistoryTableOf(services));
    }

    [Fact]
    public void ByDefault_ACallerSetHistoryTable_IsLeftUntouched()
    {
        var services = Register(configure: null, db: o => o.UseNpgsql(TestNpgsqlDataSources.Get(ConnectionString), n => n.MigrationsHistoryTable("consumer_history")));

        Assert.Equal("consumer_history", HistoryTableOf(services));
    }

    [Fact]
    public void OptedIn_RegistersTheContextMigrationOnce_AndGivesTheContextItsOwnHistoryTable()
    {
        var services = Register(o => o.MigrateOnStartup = true);

        Assert.Equal(["ContextMigrationHostedService`1"], HostedServiceNames(services));
        Assert.Equal(HistoryTable, HistoryTableOf(services));
    }

    [Fact]
    public void OptedIn_TheOwnHistoryTableWinsOverOneTheCallerSet()
    {
        var services = Register(o => o.MigrateOnStartup = true, db: o => o.UseNpgsql(TestNpgsqlDataSources.Get(ConnectionString), n => n.MigrationsHistoryTable("consumer_history")));

        Assert.Equal(HistoryTable, HistoryTableOf(services));
    }

    [Fact]
    public void ConfigureOptions_RunsExactlyOnce_AndItsValuesBackTheResolvedOptions()
    {
        var calls = 0;
        var services = Register(o =>
        {
            calls++;
            o.AllowExecutionOnStoreUnavailable = true;
            o.MigrateOnStartup = true;
            o.MigrationLockTimeout = TimeSpan.FromSeconds(7);
        });

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IOptions<EfCoreIdempotencyOptions>>().Value;
        using var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<IOptions<EfCoreIdempotencyOptions>>().Value;

        Assert.Equal(1, calls);
        Assert.True(first.AllowExecutionOnStoreUnavailable);
        Assert.True(first.MigrateOnStartup);
        Assert.Equal(TimeSpan.FromSeconds(7), first.MigrationLockTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositiveLockTimeout_Throws(int seconds)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Register(o =>
            {
                o.MigrateOnStartup = true;
                o.MigrationLockTimeout = TimeSpan.FromSeconds(seconds);
            }));

        Assert.Equal("configureOptions", exception.ParamName);
    }

    [Fact]
    public void ASecondRegistration_StillThrows_WhetherOrNotItOptsIn()
    {
        var off = Register(configure: null);
        Assert.Throws<InvalidOperationException>(() => off.AddEfCoreIdempotency(o => o.UseNpgsql(ConnectionString), p => p.ForRequests()));

        var on = Register(o => o.MigrateOnStartup = true);
        Assert.Throws<InvalidOperationException>(
            () => on.AddEfCoreIdempotency(o => o.UseNpgsql(ConnectionString), p => p.ForRequests(), o => o.MigrateOnStartup = true));
    }
}
