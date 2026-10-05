using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace SharedKernel.Localization;

/// <summary>Registers the application's <see cref="ILocalizationCatalog"/>.</summary>
/// <remarks>
/// An application has exactly one catalog, so each method throws if one is already registered.
/// A second registration would otherwise be ignored, and its translations would silently never
/// appear. To combine several sources, add them all to one <see cref="LocalizationCatalogBuilder"/>.
/// </remarks>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Builds an <see cref="InMemoryLocalizationCatalog"/> now and registers it as a singleton,
    /// both as <see cref="ILocalizationCatalog"/> and as <see cref="InMemoryLocalizationCatalog"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adds the translations, for example <c>catalog =&gt; catalog.AddJsonDirectory(path)</c>.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An <see cref="ILocalizationCatalog"/> is already registered.</exception>
    /// <remarks>
    /// The catalog is built during this call, not when it is first resolved, so a missing file or
    /// a broken translation fails application startup rather than the first request that returns
    /// an error.
    /// </remarks>
    public static IServiceCollection AddLocalizationCatalog(
        this IServiceCollection services,
        Action<LocalizationCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        EnsureNoCatalog(services);

        var builder = new LocalizationCatalogBuilder();
        configure(builder);
        InMemoryLocalizationCatalog catalog = builder.Build();

        services.TryAddSingleton(catalog);
        services.TryAddSingleton<ILocalizationCatalog>(catalog);
        return services;
    }

    /// <summary>
    /// Registers a <see cref="StringLocalizerLocalizationCatalog"/> over the <c>.resx</c> files of
    /// <typeparamref name="TResource"/> as the singleton <see cref="ILocalizationCatalog"/>.
    /// </summary>
    /// <typeparam name="TResource">The marker type the resource files are named after.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An <see cref="ILocalizationCatalog"/> is already registered.</exception>
    /// <remarks>
    /// Needs an <see cref="IStringLocalizerFactory"/>, which ASP.NET Core's
    /// <c>services.AddLocalization()</c> registers; this method does not register one.
    /// </remarks>
    public static IServiceCollection AddStringLocalizerCatalog<TResource>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureNoCatalog(services);

        services.TryAddSingleton<ILocalizationCatalog>(sp =>
            new StringLocalizerLocalizationCatalog(sp.GetRequiredService<IStringLocalizerFactory>(), typeof(TResource)));
        return services;
    }

    private static void EnsureNoCatalog(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ILocalizationCatalog)))
        {
            throw new InvalidOperationException(
                "An ILocalizationCatalog is already registered. Register one catalog per application, "
                    + "and add every translation source to it through LocalizationCatalogBuilder.");
        }
    }
}
