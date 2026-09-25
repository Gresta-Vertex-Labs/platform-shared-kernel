using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Application.Pipeline.Idempotency;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Execution.Context;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Extensions;

public sealed class RedisIdempotencyServiceCollectionExtensionsTests
{
    // "localhost:6379" is safe to use here even without a live Redis instance: AddRedisConnection
    // sets AbortOnConnectFail = false, so the multiplexer is created without a server and
    // reconnects in the background rather than throwing — these tests only prove DI *resolution*,
    // never issue a real Redis command.
    private const string ConnectionString = "localhost:6379";

    [Fact]
    public async Task Host_StartsSuccessfullyAndResolvesBothContracts()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
                services.AddSharedKernelRedisIdempotency();
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
    public void Host_RegistersTheAmbientRequestContextAccessor()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
                services.AddSharedKernelRedisIdempotency();
                // No accessor registered by the test: the extension adds the ambient one.
            })
            .Build();

        Assert.IsType<RequestContextAccessor>(host.Services.GetRequiredService<IRequestContextAccessor>());
    }

    [Fact]
    public void AddSharedKernelRedisIdempotency_AppliesConfigureDelegate()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
        services.AddSharedKernelRedisIdempotency(o =>
        {
            o.InFlightTtl = TimeSpan.FromSeconds(5);
            o.RetentionWindow = TimeSpan.FromMinutes(10);
            o.AllowExecutionOnStoreUnavailable = true;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            SharedKernel.Idempotency.Redis.Options.RedisIdempotencyOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(5), options.InFlightTtl);
        Assert.Equal(TimeSpan.FromMinutes(10), options.RetentionWindow);
        Assert.True(options.AllowExecutionOnStoreUnavailable);
    }

    [Fact]
    public void AddSharedKernelRedisIdempotency_WithoutRedisConnection_ThrowsNamingTheFix()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddSharedKernelRedisIdempotency());

        Assert.Contains("AddRedisConnection", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RedisIdempotencyServiceCollectionExtensions.AddSharedKernelRedisIdempotency), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSharedKernelRedisIdempotency_WithConfigurationBoundRedisConnection_Resolves()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SharedKernel:Caching:Redis:ConnectionString"] = ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(configuration);
        services.AddSharedKernelRedisIdempotency();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRequestIdempotencyStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IIdempotencyStore>());
    }
}
