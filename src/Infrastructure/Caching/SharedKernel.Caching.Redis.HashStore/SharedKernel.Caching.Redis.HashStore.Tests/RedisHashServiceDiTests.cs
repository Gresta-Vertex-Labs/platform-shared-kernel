using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.HashStore.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.HashStore.Tests;

/// <summary>
/// Registration contract of <see cref="RedisHashServiceExtensions"/>.
/// </summary>
public sealed class RedisHashServiceDiTests
{
    [Fact]
    public void AddRedisHashService_RegistersSingletonHashService()
    {
        var services = WithConnection();

        var returned = services.AddRedisHashService();

        Assert.Same(services, returned);
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRedisHashService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(RedisHashService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddRedisHashService_ResolvesSameInstance()
    {
        var services = WithConnection();
        services.AddLogging();
        services.AddRedisHashService();

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IRedisHashService>(), provider.GetRequiredService<IRedisHashService>());
    }

    [Fact]
    public void AddRedisHashService_CalledTwice_DoesNotDuplicateRegistration()
    {
        var services = WithConnection();

        services.AddRedisHashService();
        var count = services.Count;
        services.AddRedisHashService();

        Assert.Equal(count, services.Count);
        Assert.Single(services, d => d.ServiceType == typeof(IRedisHashService));
    }

    [Fact]
    public void AddRedisHashService_WithoutConnection_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddRedisHashService());

        Assert.Contains("AddRedisConnection", ex.Message);
        Assert.Contains(nameof(RedisHashServiceExtensions.AddRedisHashService), ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void AddTypedHashStore_WithoutConnection_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTypedHashStore(TestJsonContext.Default.TestPayload));

        Assert.Contains("AddRedisConnection", ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void AddTypedHashStore_RegistersStoreAndHashService()
    {
        var services = WithConnection();

        var returned = services.AddTypedHashStore(TestJsonContext.Default.TestPayload);

        Assert.Same(services, returned);
        Assert.Single(services, d => d.ServiceType == typeof(IRedisHashService));
        var store = Assert.Single(services, d => d.ServiceType == typeof(ITypedHashStore<TestPayload>));
        Assert.Equal(ServiceLifetime.Singleton, store.Lifetime);
    }

    [Fact]
    public void AddTypedHashStore_AfterAddRedisHashService_KeepsOneHashService()
    {
        var services = WithConnection();

        services
            .AddRedisHashService()
            .AddTypedHashStore(TestJsonContext.Default.TestPayload)
            .AddTypedHashStore(TestJsonContext.Default.Int32);

        Assert.Single(services, d => d.ServiceType == typeof(IRedisHashService));
        Assert.Single(services, d => d.ServiceType == typeof(ITypedHashStore<TestPayload>));
        Assert.Single(services, d => d.ServiceType == typeof(ITypedHashStore<int>));
    }

    [Fact]
    public void AddTypedHashStore_SecondStoreForSameType_ThrowsInvalidOperationException()
    {
        var services = WithConnection();
        services.AddTypedHashStore(TestJsonContext.Default.TestPayload);

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddTypedHashStore(TestJsonContext.Default.TestPayload));

        Assert.Contains(nameof(TestPayload), ex.Message);
        Assert.Single(services, d => d.ServiceType == typeof(ITypedHashStore<TestPayload>));
    }

    [Fact]
    public void AddTypedHashStore_ResolvesSingletonTypedStore()
    {
        var services = WithConnection();
        services.AddLogging();
        services.AddTypedHashStore(TestJsonContext.Default.TestPayload);

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ITypedHashStore<TestPayload>>();

        Assert.IsType<TypedHashStore<TestPayload>>(store);
        Assert.Same(store, provider.GetRequiredService<ITypedHashStore<TestPayload>>());
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRedisHashService());
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddTypedHashStore(TestJsonContext.Default.TestPayload));
        Assert.Throws<ArgumentNullException>(() => WithConnection().AddTypedHashStore<TestPayload>(null!));
    }

    private static IServiceCollection WithConnection() =>
        new ServiceCollection().AddRedisConnection(o => o.ConnectionString = "localhost:6379");
}
