using Microsoft.Extensions.DependencyInjection;
using SharedKernel.FeatureManagement.Abstractions;

namespace SharedKernel.Testing.FeatureManagement;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Testing</c>'s fake
/// <c>SharedKernel.FeatureManagement</c> test double.
/// </summary>
public static class FakeFeatureManagementServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFeatureManager"/> → <see cref="FakeFeatureManager"/> as a singleton,
    /// mirroring <c>AddSharedKernelFeatureManagement()</c>'s own singleton lifetime exactly — no
    /// deviation to document.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFakeFeatureManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFeatureManager, FakeFeatureManager>();

        return services;
    }
}
