using Microsoft.Extensions.DependencyInjection;
using OpenFeature;

namespace SharedKernel.Testing.FeatureManagement;

/// <summary>Registers <see cref="FakeFeatureClient"/> in place of the real feature flags.</summary>
public static class FakeFeatureClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="FakeFeatureClient"/> as a singleton, resolvable both as itself (to set flags)
    /// and as <see cref="IFeatureClient"/> (what production code injects). Call it instead of
    /// <c>AddSharedKernelFeatureManagement</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets initial flag values.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddFakeFeatureFlags(this IServiceCollection services, Action<FakeFeatureClient>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var fake = new FakeFeatureClient();
        configure?.Invoke(fake);
        services.AddSingleton(fake);
        services.AddSingleton<IFeatureClient>(fake);

        return services;
    }
}
