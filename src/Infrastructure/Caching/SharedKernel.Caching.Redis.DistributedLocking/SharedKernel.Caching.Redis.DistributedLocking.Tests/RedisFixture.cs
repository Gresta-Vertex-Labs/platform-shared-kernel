using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// One Redis container shared by every test in the <c>"Redis"</c> collection. Tests isolate
/// themselves by using a unique resource name.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    private ServiceProvider? _provider;

    /// <summary>Gets the connection string of the running container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Gets the lock service registered through <c>AddRedisDistributedLocking</c>.</summary>
    public IDistributedLockService LockService => _provider!.GetRequiredService<IDistributedLockService>();

    /// <summary>Gets the database the lock service writes to, for direct key inspection.</summary>
    public IDatabase Database => _provider!.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = _container.GetConnectionString());
        new TestCachingBuilder(services).AddRedisDistributedLocking();
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _container.DisposeAsync();
    }

    /// <summary>Returns a resource name no other test uses.</summary>
    public static string NewResource(string prefix) => $"test:{prefix}:{Guid.NewGuid():N}";

    /// <summary>The key a lock or lease on <paramref name="resource"/> occupies.</summary>
    public static RedisKey LockKey(string resource) => "sharedkernel:lock:{" + resource + "}";

    /// <summary>The key holding the fencing counter of <paramref name="resource"/>.</summary>
    public static RedisKey FencingKey(string resource) => "sharedkernel:lock-fencing:{" + resource + "}";
}

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>;
