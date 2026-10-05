using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Idempotency.Redis.Store;
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
    public async Task Host_StartsSuccessfullyAndResolvesAStorePerSelectedPurpose()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
                services.AddRedisIdempotency(p => p.ForRequests().ForMessages());
            })
            .Build();

        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        Assert.IsType<RedisIdempotencyStore>(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Request));
        Assert.IsType<RedisIdempotencyStore>(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Message));

        await host.StopAsync();
    }

    [Fact]
    public void AddRedisIdempotency_RegistersOnlyTheSelectedPurposes()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
        services.AddRedisIdempotency(p => p.ForMessages());

        Assert.True(services.HasIdempotencyStore(IdempotencyPurpose.Message));
        Assert.False(services.HasIdempotencyStore(IdempotencyPurpose.Request));
    }

    [Fact]
    public void AddRedisIdempotency_WithNoPurposeSelected_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = ConnectionString);

        Assert.Throws<InvalidOperationException>(() => services.AddRedisIdempotency(_ => { }));
    }

    [Fact]
    public void AddRedisIdempotency_TwiceForTheSamePurpose_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
        services.AddRedisIdempotency(p => p.ForRequests());

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddRedisIdempotency(p => p.ForRequests()));
        Assert.Contains("Request", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_RegistersTheAmbientRequestContextAccessor()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
                services.AddRedisIdempotency(p => p.ForRequests());
                // No accessor registered by the test: the extension adds the ambient one.
            })
            .Build();

        Assert.IsType<RequestContextAccessor>(host.Services.GetRequiredService<IRequestContextAccessor>());
    }

    [Fact]
    public void AddRedisIdempotency_AppliesConfigureDelegate()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = ConnectionString);
        services.AddRedisIdempotency(p => p.ForRequests(), o => o.AllowExecutionOnStoreUnavailable = true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            SharedKernel.Idempotency.Redis.Options.RedisIdempotencyOptions>>().Value;

        Assert.True(options.AllowExecutionOnStoreUnavailable);
    }

    [Fact]
    public void AddRedisIdempotency_WithoutRedisConnection_ThrowsNamingTheFix()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddRedisIdempotency(p => p.ForRequests()));

        Assert.Contains("AddRedisConnection", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(RedisIdempotencyServiceCollectionExtensions.AddRedisIdempotency), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRedisIdempotency_WithConfigurationBoundRedisConnection_Resolves()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SharedKernel:Caching:Redis:ConnectionString"] = ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(configuration);
        services.AddRedisIdempotency(p => p.ForRequests().ForMessages());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Request));
        Assert.NotNull(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Message));
    }
}
