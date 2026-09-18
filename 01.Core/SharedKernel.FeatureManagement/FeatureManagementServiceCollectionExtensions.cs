using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using OpenFeature;
using OpenFeature.Hosting;
using OpenFeature.Model;
using SharedKernel.FeatureManagement.Internal;

namespace SharedKernel.FeatureManagement;

/// <summary>Registers feature flags for a service.</summary>
public static class FeatureManagementServiceCollectionExtensions
{
    private const string TelemetryHookName = "SharedKernel.FeatureManagement.Telemetry";

    /// <summary>
    /// Registers OpenFeature's <see cref="IFeatureClient"/> (scoped) backed by <c>Microsoft.FeatureManagement</c>,
    /// reading flags from <paramref name="configuration"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pass the application's root configuration. Flags are read from the <c>feature_management</c> section
    /// (the Microsoft Feature Management schema, which supports variants, allocation and telemetry) and from the
    /// older <c>FeatureManagement</c> section; passing <c>configuration.GetSection(...)</c> hides the first one.
    /// </para>
    /// <para>
    /// The provider is initialized when the host starts. Outside a host (a plain <see cref="ServiceProvider"/>
    /// in a test), call <c>IFeatureLifecycleManager.EnsureInitializedAsync()</c> first; until then every
    /// evaluation returns its default with <c>ErrorType.ProviderNotReady</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application's root configuration.</param>
    /// <param name="configure">Optional settings: startup validation, per-scope evaluation, telemetry, custom filters.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="InvalidOperationException">Feature management is already registered.</exception>
    public static IServiceCollection AddSharedKernelFeatureManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<FeatureFlagOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(static d => d.ServiceType == typeof(FeatureFlagRegistration)))
        {
            throw new InvalidOperationException(
                "AddSharedKernelFeatureManagement has already been called. Call it once, with every option in one configure action.");
        }

        var options = new FeatureFlagOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(new FeatureFlagRegistration(options));

        IFeatureManagementBuilder featureManagement = services.AddFeatureManagement(configuration);
        foreach (Action<IFeatureManagementBuilder> action in options.FeatureManagementActions)
        {
            action(featureManagement);
        }

        HideTelemetryFromTheEvaluator(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFeatureTargetingContextAccessor, BaggageTenantTargetingContextAccessor>();
        services.TryAddSingleton<MicrosoftFeatureManagementProvider>();

        services.AddOpenFeature(builder =>
        {
            builder.AddProvider(static sp => sp.GetRequiredService<MicrosoftFeatureManagementProvider>());

            foreach (Action<OpenFeatureBuilder> action in options.OpenFeatureActions)
            {
                action(builder);
            }

            if (!builder.IsContextConfigured)
            {
                builder.AddContext(static (context, sp) =>
                {
                    FeatureTargetingContext? targeting = sp.GetService<IFeatureTargetingContextAccessor>()?.GetTargetingContext();
                    if (targeting is not null)
                    {
                        context.Merge(targeting.ToEvaluationContext());
                    }
                });
            }

            if (options.Telemetry != FeatureTelemetryMode.Off)
            {
                builder.AddHook(TelemetryHookName, new FeatureTelemetryHook(options.Telemetry));
            }
        });

        // OpenFeature.Hosting registers a scoped client; replace it so the scope's result cache wraps it.
        services.Replace(ServiceDescriptor.Scoped<IFeatureClient>(sp => CreateClient(sp, options)));

        if (options.FlagsToValidate.Count > 0)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FeatureFlagValidationService>());
        }

        return services;
    }

    // Microsoft.FeatureManagement adds its own activity event, with the targeting id, for flags with telemetry
    // enabled. Its evaluator gets definitions without telemetry; the provider keeps the configured ones.
    private static void HideTelemetryFromTheEvaluator(IServiceCollection services)
    {
        ServiceDescriptor configured = services.Last(static d => d.ServiceType == typeof(IFeatureDefinitionProvider));
        services.TryAddSingleton(sp => new ConfiguredFeatureDefinitions(Create(sp, configured)));
        services.RemoveAll<IFeatureDefinitionProvider>();
        services.TryAddSingleton<IFeatureDefinitionProvider>(
            static sp => new EvaluationFeatureDefinitionProvider(sp.GetRequiredService<ConfiguredFeatureDefinitions>().Inner));

        static IFeatureDefinitionProvider Create(IServiceProvider sp, ServiceDescriptor descriptor) =>
            (IFeatureDefinitionProvider)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
    }

    private static IFeatureClient CreateClient(IServiceProvider services, FeatureFlagOptions options)
    {
        FeatureClient client = services.GetRequiredService<Api>().GetClient();
        EvaluationContext? context = services.GetService<EvaluationContext>();
        if (context is not null)
        {
            client.SetContext(context);
        }

        return options.EvaluateOncePerScope
            ? new ScopedFeatureClient(client, services.GetRequiredService<TimeProvider>(), options.ScopeResultLifetime)
            : client;
    }
}
