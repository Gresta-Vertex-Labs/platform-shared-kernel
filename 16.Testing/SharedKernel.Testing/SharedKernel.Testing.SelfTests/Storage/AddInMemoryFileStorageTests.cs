using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Testing.Storage;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Storage;

public sealed class AddInMemoryFileStorageTests
{
    [Fact]
    public void AddInMemoryFileStorage_ResolvesIFileStorage_AsInMemoryFileStorage()
    {
        var provider = BuildProvider();

        Assert.IsType<InMemoryFileStorage>(provider.GetRequiredService<IFileStorage>());
    }

    [Fact]
    public void AddInMemoryFileStorage_ResolvesIBlobUriGenerator_AsInMemoryBlobUriGenerator()
    {
        var provider = BuildProvider();

        Assert.IsType<InMemoryBlobUriGenerator>(provider.GetRequiredService<IBlobUriGenerator>());
    }

    [Fact]
    public void AddInMemoryFileStorage_BothServices_AreSingletons()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IFileStorage>(), provider.GetRequiredService<IFileStorage>());
        Assert.Same(provider.GetRequiredService<IBlobUriGenerator>(), provider.GetRequiredService<IBlobUriGenerator>());
    }

    [Fact]
    public void AddInMemoryFileStorage_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryFileStorage());

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryFileStorage();
        return services.BuildServiceProvider();
    }
}
