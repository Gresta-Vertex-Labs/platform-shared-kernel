using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.HashStore.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.HashStore.Tests;

/// <summary>Test payload stored in hash fields.</summary>
internal sealed record TestPayload(string Name, int Score);

[JsonSerializable(typeof(TestPayload))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal sealed partial class TestJsonContext : JsonSerializerContext;

/// <summary>
/// One Redis container shared by every test in the <c>"Redis"</c> collection, with the hash service and a typed
/// store for <see cref="TestPayload"/> registered. Tests isolate themselves by using a unique key.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    private ServiceProvider? _provider;

    internal ServiceProvider Provider => _provider!;

    internal IRedisHashService HashService => Provider.GetRequiredService<IRedisHashService>();

    internal ITypedHashStore<TestPayload> PayloadStore => Provider.GetRequiredService<ITypedHashStore<TestPayload>>();

    /// <summary>Gets the database the services write to, for direct key inspection.</summary>
    public IDatabase Database => Provider.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddRedisConnection(o => o.ConnectionString = _container.GetConnectionString())
            .AddRedisHashService()
            .AddTypedHashStore(TestJsonContext.Default.TestPayload);
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _container.DisposeAsync();
    }

    /// <summary>Returns a key no other test uses.</summary>
    public static string NewKey(string prefix) => $"test:hash:{prefix}:{Guid.NewGuid():N}";
}

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>;
