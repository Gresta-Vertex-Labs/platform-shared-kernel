using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
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
    public async Task Host_WithTenantContextAccessorRegistered_StartsSuccessfullyAndResolvesAllThreeContracts()
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
        var keyStore = scope.ServiceProvider.GetRequiredService<IIdempotencyKeyStore>();
        var responseStore = scope.ServiceProvider.GetRequiredService<IIdempotencyResponseStore>();
        var messageStore = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        Assert.NotNull(keyStore);
        Assert.NotNull(messageStore);
        // Domain Invariant 7: IIdempotencyKeyStore and IIdempotencyResponseStore must be the same
        // instance, so IdempotentCommandBehavior's `is IIdempotencyResponseStore` check succeeds.
        Assert.Same(keyStore, responseStore);

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
