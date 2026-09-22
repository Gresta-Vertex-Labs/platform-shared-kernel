using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Extensions;

public sealed class EfCoreIdempotencyServiceCollectionExtensionsTests
{
    // A syntactically valid but never-actually-connected-to Npgsql connection string. Building an
    // IdempotencyDbContext instance via DI does not open a connection — EF Core connects lazily on
    // first query — so these tests prove DI *resolution* only, never issue a real query.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sk_idempotency_test;Username=sk;Password=sk;Timeout=1";

    private sealed class TestTenantContextAccessor : ITenantContextAccessor
    {
        public Guid? TenantId => Guid.NewGuid();
    }

    [Fact]
    public async Task Host_WithTenantContextAccessorRegistered_StartsSuccessfullyAndResolvesBothContracts()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddClock();
                services.AddSharedKernelEfCoreIdempotency(options => options.UseNpgsql(ConnectionString));
                services.AddSingleton<ITenantContextAccessor, TestTenantContextAccessor>();
            })
            .Build();

        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var requestStore = scope.ServiceProvider.GetRequiredService<IRequestIdempotencyStore>();
        var messageStore = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        Assert.NotNull(requestStore);
        Assert.NotNull(messageStore);

        await host.StopAsync();
    }

    [Fact]
    public async Task Host_WithoutTenantContextAccessorRegistered_ThrowsAtStartAsync()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddClock();
                services.AddSharedKernelEfCoreIdempotency(options => options.UseNpgsql(ConnectionString));
                // Deliberately no ITenantContextAccessor registration.
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }
    [Fact]
    public void AddSharedKernelEfCoreIdempotency_RunsTheStoreWithoutRetry_EvenOverTheRetryingPlatformDefault()
    {
        // UsePostgres turns retry on by default; the store makes single atomic statements and leaves retry to
        // its caller, so an unreachable store fails fast (fail-open or fail-closed) instead of backing off.
        var services = new ServiceCollection();
        services.AddClock();
        services.AddSharedKernelEfCoreIdempotency(options => options.UsePostgres(
            SharedKernel.Testing.Persistence.TestNpgsqlDataSources.Get(ConnectionString)));
        services.AddSingleton<ITenantContextAccessor, TestTenantContextAccessor>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SharedKernel.Idempotency.EfCore.Context.IdempotencyDbContext>();

        Assert.False(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }


    [Fact]
    public void AddSharedKernelEfCoreIdempotency_AppliesConfigureOptionsDelegate()
    {
        var services = new ServiceCollection();
        services.AddClock();
        services.AddSharedKernelEfCoreIdempotency(
            options => options.UseNpgsql(ConnectionString),
            configureOptions: o =>
            {
                o.InFlightTtl = TimeSpan.FromSeconds(5);
                o.RetentionWindow = TimeSpan.FromMinutes(10);
                o.AllowExecutionOnStoreUnavailable = true;
            });
        services.AddSingleton<ITenantContextAccessor, TestTenantContextAccessor>();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            SharedKernel.Idempotency.EfCore.Options.EfCoreIdempotencyOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(5), options.InFlightTtl);
        Assert.Equal(TimeSpan.FromMinutes(10), options.RetentionWindow);
        Assert.True(options.AllowExecutionOnStoreUnavailable);
    }
}
