using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace SharedKernel.Localization;

/// <summary>
/// DI registration extensions for <see cref="ILocalizationCatalog"/>.
/// </summary>
/// <remarks>
/// Deliberately never named <c>AddSharedKernelLocalization</c> — that name is reserved for
/// <c>13.ServiceDefaults</c>'s culture-<em>resolution</em> middleware entry point (P-483):
/// resolving which culture a request is in (<c>UserPreference</c> claim → tenant
/// <c>DefaultCulture</c> → <c>Accept-Language</c> header), a distinct concern from this package's
/// (looking up a translated message for an already-known code + culture pair). The two names must
/// never collide across domains.
/// </remarks>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="ILocalizationCatalog"/> backed by a new
    /// <see cref="InMemoryLocalizationCatalog"/>, optionally seeded via <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">An optional callback used to chain
    /// <see cref="InMemoryLocalizationCatalog.AddTranslation"/> calls seeding the catalog before
    /// it is registered.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Mutually exclusive with <see cref="AddStringLocalizerCatalog{TResource}"/> — both register
    /// <see cref="ILocalizationCatalog"/>, so calling both on the same
    /// <see cref="IServiceCollection"/> leaves whichever call ran last as the implementation that
    /// resolves (the standard "last registration wins" .NET DI container behavior for a
    /// single-implementation interface); pick exactly one per service.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddInMemoryLocalizationCatalog(
        this IServiceCollection services,
        Action<InMemoryLocalizationCatalog>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ILocalizationCatalog>(_ =>
        {
            var catalog = new InMemoryLocalizationCatalog();
            configure?.Invoke(catalog);
            return catalog;
        });

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="ILocalizationCatalog"/> backed by a
    /// <see cref="StringLocalizerLocalizationCatalog"/> wrapping the already-registered
    /// <see cref="IStringLocalizerFactory"/> for resource type <typeparamref name="TResource"/>.
    /// </summary>
    /// <typeparam name="TResource">The resource-owning marker type, matching the type passed to
    /// <see cref="IStringLocalizerFactory.Create(Type)"/> when resolving <c>.resx</c> files by
    /// naming convention.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// Requires an <see cref="IStringLocalizerFactory"/> to already be registered — typically via
    /// ASP.NET Core's own <c>services.AddLocalization()</c> — this method does not register one
    /// itself. Mutually exclusive with <see cref="AddInMemoryLocalizationCatalog"/> — see that
    /// method's remarks.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddStringLocalizerCatalog<TResource>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ILocalizationCatalog>(sp =>
        {
            IStringLocalizerFactory factory = sp.GetRequiredService<IStringLocalizerFactory>();
            return new StringLocalizerLocalizationCatalog(factory, typeof(TResource));
        });

        return services;
    }
}
