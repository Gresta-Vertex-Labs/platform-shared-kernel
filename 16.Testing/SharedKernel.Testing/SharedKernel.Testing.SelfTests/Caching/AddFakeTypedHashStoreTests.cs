using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>Proves <see cref="CachingServiceCollectionExtensions.AddFakeTypedHashStore{T}"/>.</summary>
public sealed class AddFakeTypedHashStoreTests
{
    [Fact]
    public void AddFakeTypedHashStore_RegistersFakeTypedHashStore_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddFakeTypedHashStore<string>();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ITypedHashStore<string>>();
        var second = provider.GetRequiredService<ITypedHashStore<string>>();

        Assert.IsType<FakeTypedHashStore<string>>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddFakeTypedHashStore_CallableOncePerDistinctT_WithoutCollision()
    {
        var services = new ServiceCollection();
        services.AddFakeTypedHashStore<string>();
        services.AddFakeTypedHashStore<int>();
        var provider = services.BuildServiceProvider();

        var stringStore = provider.GetRequiredService<ITypedHashStore<string>>();
        var intStore = provider.GetRequiredService<ITypedHashStore<int>>();

        Assert.IsType<FakeTypedHashStore<string>>(stringStore);
        Assert.IsType<FakeTypedHashStore<int>>(intStore);
    }

    [Fact]
    public void AddFakeTypedHashStore_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeTypedHashStore<string>());
}
