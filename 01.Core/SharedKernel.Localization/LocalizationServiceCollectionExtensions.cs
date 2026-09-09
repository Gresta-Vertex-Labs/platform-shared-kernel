using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <para>
    /// Mutually exclusive with <see cref="AddStringLocalizerCatalog{TResource}"/> — both register
    /// <see cref="ILocalizationCatalog"/> via <c>TryAddSingleton</c>, so calling both on the same
    /// <see cref="IServiceCollection"/> leaves whichever call ran <b>first</b> as the
    /// implementation that resolves — the standard <c>TryAdd</c> "first registration wins"
    /// behavior for a single-implementation interface. This is a deliberate inversion of this
    /// package's own prior "last call wins" behavior (P-518/WO-083), part of standardizing every
    /// DI extension method across the <c>01.Core</c> domain onto the <c>TryAdd*</c> idiom; call
    /// exactly one of these two methods per service, in whichever order you want to win.
    /// </para>
    /// <para>
    /// The returned <see cref="InMemoryLocalizationCatalog"/> is sealed —
    /// <see cref="InMemoryLocalizationCatalog.Seal"/> is called automatically immediately after
    /// <paramref name="configure"/> returns, before the singleton is ever handed to a resolver.
    /// This makes the singleton safe to read concurrently from any number of threads with zero
    /// lock overhead, since no further mutation can ever occur after this point. Consequently, any
    /// call to <see cref="InMemoryLocalizationCatalog.AddTranslation"/> against the resolved
    /// instance — including one made through this same <paramref name="configure"/> callback after
    /// it has already returned once, which cannot happen through normal use — always throws
    /// <see cref="InvalidOperationException"/>. Seed every translation you need inside
    /// <paramref name="configure"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddInMemoryLocalizationCatalog(
        this IServiceCollection services,
        Action<InMemoryLocalizationCatalog>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ILocalizationCatalog>(_ =>
        {
            var catalog = new InMemoryLocalizationCatalog();
            configure?.Invoke(catalog);
            catalog.Seal();
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
    /// itself. Mutually exclusive with <see cref="AddInMemoryLocalizationCatalog"/> — registers
    /// <see cref="ILocalizationCatalog"/> via <c>TryAddSingleton</c>, so whichever of the two
    /// methods runs <b>first</b> is the one that resolves; see that method's remarks for the
    /// full first-wins behavior description.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddStringLocalizerCatalog<TResource>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ILocalizationCatalog>(sp =>
        {
            IStringLocalizerFactory factory = sp.GetRequiredService<IStringLocalizerFactory>();
            return new StringLocalizerLocalizationCatalog(factory, typeof(TResource));
        });

        return services;
    }
}
