using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsftFeatureManagement = Microsoft.FeatureManagement;
using SharedKernel.FeatureManagement.Abstractions;

namespace SharedKernel.FeatureManagement.Extensions;

/// <summary>
/// DI extension methods for registering the SharedKernel feature management abstraction.
/// </summary>
public static class FeatureManagementExtensions
{
    /// <summary>
    /// Registers <see cref="IFeatureManager"/> backed by <c>Microsoft.FeatureManagement</c>
    /// and wires the feature flag configuration from <paramref name="configuration"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The application configuration. Feature flags are read from the
    /// <c>FeatureManagement</c> section by convention.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSharedKernelFeatureManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        MsftFeatureManagement.ServiceCollectionExtensions.AddFeatureManagement(
            services, configuration.GetSection("FeatureManagement"));
        services.AddSingleton<IFeatureManager, MicrosoftFeatureManagerAdapter>();

        return services;
    }
}
