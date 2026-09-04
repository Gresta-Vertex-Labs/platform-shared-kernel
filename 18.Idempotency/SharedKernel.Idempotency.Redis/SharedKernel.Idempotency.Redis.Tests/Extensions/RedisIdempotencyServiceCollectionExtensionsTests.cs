using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Extensions;

public sealed class RedisIdempotencyServiceCollectionExtensionsTests
{
    // "localhost:6379" is safe to use here even without a live Redis instance: AddRedisConnection
    // sets AbortOnConnectFail = false, so ConnectionMultiplexer.Connect() succeeds immediately and
    // retries in the background rather than throwing — these tests only prove DI *resolution*,
    // never issue a real Redis command.
    private const string ConnectionString = "localhost:6379";

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
                services.AddRedisConnection(ConnectionString);
                services.AddSharedKernelRedisIdempotency();
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
                services.AddRedisConnection(ConnectionString);
                services.AddSharedKernelRedisIdempotency();
                // Deliberately no ITenantContextAccessor registration.
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }

    [Fact]
    public void AddSharedKernelRedisIdempotency_AppliesConfigureDelegate()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(ConnectionString);
        services.AddSharedKernelRedisIdempotency(o =>
        {
            o.InFlightTtl = TimeSpan.FromSeconds(5);
            o.RetentionWindow = TimeSpan.FromMinutes(10);
            o.AllowExecutionOnStoreUnavailable = true;
        });
        services.AddSingleton<ITenantContextAccessor, TestTenantContextAccessor>();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            SharedKernel.Idempotency.Redis.Options.RedisIdempotencyOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(5), options.InFlightTtl);
        Assert.Equal(TimeSpan.FromMinutes(10), options.RetentionWindow);
        Assert.True(options.AllowExecutionOnStoreUnavailable);
    }
}
