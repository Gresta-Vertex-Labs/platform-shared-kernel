using Microsoft.Extensions.DependencyInjection;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.Testing.FeatureManagement;
using Xunit;

namespace SharedKernel.Testing.SelfTests.FeatureManagement;

/// <summary>
/// Proves <see cref="FakeFeatureManagementServiceCollectionExtensions.AddFakeFeatureManagement"/>'s
/// DI registration shape. Proven exclusively in <c>SharedKernel.Testing.SelfTests</c> — see
/// <c>16.Testing/state-map.md</c> T-59.
/// </summary>
public sealed class FakeFeatureManagementServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFakeFeatureManagement_ResolvesIFeatureManager_AsFakeFeatureManager()
    {
        var provider = BuildProvider();

        Assert.IsType<FakeFeatureManager>(provider.GetRequiredService<IFeatureManager>());
    }

    [Fact]
    public void AddFakeFeatureManagement_IsRegisteredAsASingleton()
    {
        var provider = BuildProvider();

        Assert.Same(
            provider.GetRequiredService<IFeatureManager>(),
            provider.GetRequiredService<IFeatureManager>());
    }

    [Fact]
    public void AddFakeFeatureManagement_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeFeatureManagement());

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddFakeFeatureManagement();
        return services.BuildServiceProvider();
    }
}
