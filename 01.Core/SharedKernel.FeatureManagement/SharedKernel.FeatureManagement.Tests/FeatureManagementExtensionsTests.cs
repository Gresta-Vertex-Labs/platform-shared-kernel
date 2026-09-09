using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.FeatureManagement.Extensions;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>
/// Covers <see cref="FeatureManagementExtensions.AddSharedKernelFeatureManagement"/>'s
/// <c>TryAddSingleton</c>-based <see cref="IFeatureManager"/> registration (SK.01.P518) — a
/// consumer registration made before this call must always win over the platform default.
/// </summary>
public sealed class FeatureManagementExtensionsTests
{
    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void AddSharedKernelFeatureManagement_ConsumerFakeRegisteredFirst_WinsOverPlatformDefault()
    {
        var services = new ServiceCollection();
        var fake = new FakeFeatureManager();

        services.AddSingleton<IFeatureManager>(fake);
        services.AddSharedKernelFeatureManagement(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(fake, provider.GetRequiredService<IFeatureManager>());
    }

    [Fact]
    public void AddSharedKernelFeatureManagement_CalledTwice_RegistersItsOwnAdapterExactlyOnce()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelFeatureManagement(EmptyConfiguration());
        services.AddSharedKernelFeatureManagement(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IFeatureManager>());
    }

    private sealed class FakeFeatureManager : IFeatureManager
    {
        public ValueTask<bool> IsEnabledAsync(string feature, CancellationToken ct = default) =>
            ValueTask.FromResult(false);

        public ValueTask<bool> IsEnabledAsync<TContext>(string feature, TContext context, CancellationToken ct = default) =>
            ValueTask.FromResult(false);

        public ValueTask<FeatureVariant> GetVariantAsync(string feature, CancellationToken ct = default) =>
            ValueTask.FromResult(FeatureVariant.Unassigned);

        public ValueTask<FeatureVariant> GetVariantAsync<TContext>(string feature, TContext context, CancellationToken ct = default) =>
            ValueTask.FromResult(FeatureVariant.Unassigned);
    }
}
