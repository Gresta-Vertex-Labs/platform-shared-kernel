using System.Reflection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Core.Health;
using SharedKernel.Caching.Redis.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Caching.Redis.Tests.Integration;

/// <summary>
/// Regression tests for the shared connection: the distributed cache and the FusionCache backplane registered by
/// <c>AddRedisL2</c> use the <see cref="IConnectionMultiplexer"/> from <c>AddRedisConnection</c> and open no
/// Redis connections of their own.
/// </summary>
[Collection("Redis")]
public sealed class SharedConnectionIntegrationTests : IAsyncLifetime
{
    private const string AdminClientName = "sk-test-admin";
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    private ConnectionMultiplexer? _admin;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var adminOptions = ConfigurationOptions.Parse(_container.GetConnectionString());
        adminOptions.ClientName = AdminClientName;
        adminOptions.AllowAdmin = true;
        _admin = await ConnectionMultiplexer.ConnectAsync(adminOptions);
    }

    public async Task DisposeAsync()
    {
        if (_admin is not null)
            await _admin.DisposeAsync();

        await _container.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLayer_WritesOneStringPerEntryUnderTheKeyPrefix_AndIsNotRegisteredInDi()
    {
        await using var provider = BuildProvider(o => o.KeyPrefix = "shared:");
        var cache = provider.GetRequiredService<ICacheService>();
        var key = "shared-layer:" + Guid.NewGuid();

        await cache.SetAsync(key, "value", CachePolicy.Default);

        Assert.True(await PollAsync(() => _admin!.GetDatabase().KeyExistsAsync("shared:v2:" + key)));
        Assert.Equal(RedisType.String, await _admin!.GetDatabase().KeyTypeAsync("shared:v2:" + key));
        Assert.False(await _admin.GetDatabase().KeyExistsAsync("v2:" + key));

        // Handed to FusionCache only: nothing in DI can dispose it or bypass the cache through it.
        Assert.Null(provider.GetService<IDistributedCache>());
    }

    [Fact]
    public async Task DisposingTheDistributedCacheOrFusionCache_LeavesTheSharedConnectionOpen()
    {
        await using var provider = BuildProvider(o => o.KeyPrefix = "dispose:");
        var cache = provider.GetRequiredService<ICacheService>();
        await cache.SetAsync("dispose:key", "value", CachePolicy.Default);
        await cache.RemoveAsync("dispose:key");

        var shared = provider.GetRequiredService<IConnectionMultiplexer>();

        // The same wrapper AddRedisL2 hands to the backplane, which disposes the connection it was given when it
        // unsubscribes. The wrapper forwards everything else to the shared connection.
        var handed = new SharedConnectionMultiplexer(shared);
        Assert.Same(shared, handed.GetDatabase().Multiplexer);
        Assert.Equal(shared.IsConnected, handed.IsConnected);

        handed.Close();
        await handed.CloseAsync();
        handed.Dispose();
        await handed.DisposeAsync();

        // Disposing FusionCache disposes its distributed layer and unsubscribes (and disposes) its backplane.
        provider.GetRequiredService<IFusionCache>().Dispose();

        Assert.True(shared.IsConnected);
        Assert.True(await shared.GetDatabase().PingAsync() >= TimeSpan.Zero);
        await shared.GetDatabase().StringSetAsync("dispose:after", "still-usable");
        Assert.Equal("still-usable", (string?)await _admin!.GetDatabase().StringGetAsync("dispose:after"));
    }

    [Fact]
    public async Task DisposingTheProvider_DisposesTheSharedConnectionOnce_WithoutErrors()
    {
        var provider = BuildProvider();
        var cache = provider.GetRequiredService<ICacheService>();
        await cache.SetAsync("dispose-provider:key", "value", CachePolicy.Default);
        var shared = provider.GetRequiredService<IConnectionMultiplexer>();

        await provider.DisposeAsync();

        Assert.False(shared.IsConnected);
    }

    [Fact]
    public async Task KeyPrefix_BecomesTheBackplaneChannelPrefix()
    {
        await using var provider = BuildProvider(o => o.KeyPrefix = "tenant-x:");
        _ = provider.GetRequiredService<ICacheService>();

        var server = AdminServer();
        Assert.True(
            await PollAsync(async () => (await server.SubscriptionChannelsAsync(RedisChannel.Pattern("tenant-x:*Backplane*"))).Length > 0),
            "The backplane never subscribed to a channel starting with the key prefix. Channels: "
            + string.Join(", ", (await server.SubscriptionChannelsAsync(RedisChannel.Pattern("*"))).Select(c => c.ToString())));
    }

    [Fact]
    public async Task DifferentKeyPrefixes_DoNotEvictEachOthersMemoryEntriesThroughTheBackplane()
    {
        const string key = "isolation:entry";

        // A and C share a prefix (same deployment); B has another prefix on the same Redis.
        await using var providerA = BuildProvider(o => o.KeyPrefix = "deploy-1:");
        await using var providerB = BuildProvider(o => o.KeyPrefix = "deploy-2:");
        await using var providerC = BuildProvider(o => o.KeyPrefix = "deploy-1:");

        var cacheA = providerA.GetRequiredService<ICacheService>();
        var cacheB = providerB.GetRequiredService<ICacheService>();
        var cacheC = providerC.GetRequiredService<ICacheService>();

        var server = AdminServer();
        Assert.True(await PollAsync(async () =>
            (await server.SubscriptionChannelsAsync(RedisChannel.Pattern("deploy-1:*Backplane*"))).Length > 0
            && (await server.SubscriptionChannelsAsync(RedisChannel.Pattern("deploy-2:*Backplane*"))).Length > 0));

        // Memory-only entries: after a backplane eviction the next read misses, because Redis never held them.
        await cacheB.SetAsync(key, "b-local", CachePolicy.Default.LocalOnly());
        await cacheC.SetAsync(key, "c-local", CachePolicy.Default.LocalOnly());

        await cacheA.RemoveAsync(key);

        // Control: C shares A's prefix, so the eviction reaches it. Once it has, the message has been published.
        Assert.True(
            await PollAsync(async () => !(await cacheC.TryGetAsync<string>(key)).IsHit),
            "The eviction never reached the instance that shares the key prefix; the isolation check below would prove nothing.");

        await Task.Delay(300);

        Assert.Equal("b-local", (await cacheB.TryGetAsync<string>(key)).Value);
    }

    [Fact]
    public async Task CacheAndBackplane_OpenNoConnectionsBeyondTheSharedMultiplexer()
    {
        await using var provider = BuildProvider();
        var cache = provider.GetRequiredService<ICacheService>();
        var shared = provider.GetRequiredService<IConnectionMultiplexer>();

        // Exercise the distributed cache (write, read, L2 read-through) and the backplane (removals and a tag
        // eviction publish backplane messages).
        var key = "shared-connection:" + Guid.NewGuid();
        var tag = "shared-connection-tag-" + Guid.NewGuid();
        await cache.SetAsync(key, "value", CachePolicy.Default.WithTags(tag));
        Assert.Equal("value", (await cache.TryGetAsync<string>(key)).Value);
        await cache.RemoveAsync(key);
        await cache.RemoveByTagAsync(tag);
        await cache.GetOrSetAsync(key + ":gos", _ => ValueTask.FromResult("computed"), CachePolicy.Default);

        // The L2 write really went through Redis.
        Assert.True(await PollAsync(() => _admin!.GetDatabase().KeyExistsAsync("v2:" + key + ":gos")));

        // The backplane subscribed to its channel.
        var server = AdminServer();
        Assert.True(
            await PollAsync(async () => (await server.SubscriptionChannelsAsync(RedisChannel.Pattern("*Backplane*"))).Length > 0),
            "The FusionCache backplane never subscribed to its channel.");

        // Every client connection other than the admin connection belongs to the shared multiplexer: one
        // interactive and one subscription connection. A cache or backplane with its own multiplexer would add
        // at least two more.
        var clients = await ApplicationClientsAsync(2);
        Assert.True(
            clients.Length == 2,
            $"Expected only the shared multiplexer's 2 connections, found {clients.Length}: "
            + string.Join(" | ", clients.Select(c => c.Raw)));
        Assert.True(shared.IsConnected);
    }

    [Fact]
    public async Task TwoProviders_EachUseExactlyOneMultiplexer()
    {
        await using var first = BuildProvider();
        await using var second = BuildProvider();

        foreach (var provider in new[] { first, second })
        {
            var cache = provider.GetRequiredService<ICacheService>();
            var key = "two-providers:" + Guid.NewGuid();
            await cache.SetAsync(key, "value", CachePolicy.Default);
            await cache.RemoveAsync(key);
        }

        // Allow any lazily-opened connection to show up before counting.
        await Task.Delay(300);

        var clients = await ApplicationClientsAsync(4);
        Assert.True(
            clients.Length == 4,
            $"Expected 2 connections per provider (4), found {clients.Length}: "
            + string.Join(" | ", clients.Select(c => c.Raw)));
    }

    [Fact]
    public async Task BreakerDurations_ReachTheLiveFusionCacheInstance()
    {
        await using var provider = BuildProvider(o =>
        {
            o.DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(11);
            o.BackplaneCircuitBreakerDuration = TimeSpan.FromSeconds(13);
        });

        var fusionCache = provider.GetRequiredService<IFusionCache>();
        var field = fusionCache.GetType().GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field is not null, $"{fusionCache.GetType()} no longer has an _options field; update this test.");

        var live = (FusionCacheOptions)field!.GetValue(fusionCache)!;

        Assert.Equal(TimeSpan.FromSeconds(11), live.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(TimeSpan.FromSeconds(13), live.BackplaneCircuitBreakerDuration);
    }

    [Fact]
    public async Task ConfigurationOverloads_EndToEnd_WriteUnderTheBoundKeyPrefix()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Caching:ServiceName"] = "config-svc",
                ["SharedKernel:Caching:Redis:ConnectionString"] = _container.GetConnectionString(),
                ["SharedKernel:Caching:Redis:CommandTimeout"] = "00:00:03",
                ["SharedKernel:Caching:Redis:L2:KeyPrefix"] = "bound:",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(configuration);
        services.AddSharedKernelCaching(configuration).AddRedisL2(configuration);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        var cache = provider.GetRequiredService<ICacheService>();
        var key = "configured:" + Guid.NewGuid();
        await cache.SetAsync(key, "value", CachePolicy.Default);

        Assert.True(await PollAsync(() => _admin!.GetDatabase().KeyExistsAsync("bound:v2:" + key)));
        Assert.Equal("bound:", provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value.BackplaneChannelPrefix);
        Assert.Equal(3_000, provider.GetRequiredService<IConnectionMultiplexer>().TimeoutMilliseconds);

        var health = await provider.GetRequiredReadinessProbe(RedisReadinessProbeNames.Connection).ProbeAsync();
        Assert.True(health.IsHealthy);
    }

    private ServiceProvider BuildProvider(Action<RedisL2Options>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = _container.GetConnectionString());
        services.AddSharedKernelCaching(o => o.ServiceName = "shared-connection-svc").AddRedisL2(configure);
        return services.BuildServiceProvider();
    }

    private IServer AdminServer() => _admin!.GetServer(_admin.GetEndPoints()[0]);

    // Connections from earlier tests' disposed providers can linger for a moment, so wait for the count to settle
    // on the expected value; a cache or backplane with its own connection never settles there.
    private async Task<ClientInfo[]> ApplicationClientsAsync(int expected)
    {
        ClientInfo[] clients = [];
        await PollAsync(async () =>
        {
            clients = (await AdminServer().ClientListAsync())
                .Where(c => !string.Equals(c.Name, AdminClientName, StringComparison.Ordinal))
                .ToArray();
            return clients.Length == expected;
        });
        return clients;
    }

    private static async Task<bool> PollAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        do
        {
            if (await condition())
                return true;

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }
}
