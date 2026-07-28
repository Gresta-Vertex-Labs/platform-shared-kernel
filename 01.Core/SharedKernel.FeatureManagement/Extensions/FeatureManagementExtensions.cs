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
    /// The application's root configuration. Plain boolean feature flags are read from the
    /// <c>FeatureManagement</c> section by convention. Weighted-variant/gradual-rollout features
    /// (P-298/WO-049) are read from the <c>feature_management:feature_flags</c> section, following
    /// <c>Microsoft.FeatureManagement</c>'s own
    /// <see href="https://github.com/microsoft/FeatureManagement/blob/main/Schema/FeatureManagement.v2.0.0.schema.json">Microsoft Feature Management schema</see>
    /// — no additional configuration is required beyond what that schema itself needs.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <paramref name="configuration"/> must be the application's root <see cref="IConfiguration"/>,
    /// never a value pre-scoped to the <c>FeatureManagement</c> section (e.g. via
    /// <c>configuration.GetSection("FeatureManagement")</c>) — confirmed empirically (P-298/WO-049):
    /// <c>Microsoft.FeatureManagement</c>'s <c>ConfigurationFeatureDefinitionProvider</c> looks for
    /// the legacy <c>FeatureManagement</c> section and the variant/allocation
    /// <c>feature_management:feature_flags</c> section independently, both relative to whatever
    /// <see cref="IConfiguration"/> instance it is given. Pre-scoping to <c>FeatureManagement</c>
    /// silently makes the variant schema unreachable (it lives under an entirely different,
    /// unscoped root key) with no error or warning — plain boolean flags still resolve either way,
    /// which is why this defect was easy to miss. Passing the root configuration here matches this
    /// method's public contract, which has always accepted the application's full
    /// <see cref="IConfiguration"/>, so no consumer-visible signature change is required to fix it.
    /// </remarks>
    public static IServiceCollection AddSharedKernelFeatureManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        MsftFeatureManagement.ServiceCollectionExtensions.AddFeatureManagement(services, configuration);
        services.AddSingleton<IFeatureManager, MicrosoftFeatureManagerAdapter>();

        return services;
    }
}
