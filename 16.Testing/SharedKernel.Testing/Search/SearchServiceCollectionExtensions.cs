using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Testing.Search;

/// <summary>
/// DI convenience extensions registering the in-memory search test doubles.
/// </summary>
public static class SearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemorySearchIndex{TDocument}"/> as the
    /// <see cref="ISearchIndex{TDocument}"/> singleton for <typeparamref name="TDocument"/>,
    /// constructed against <paramref name="definition"/>. Call once per
    /// <typeparamref name="TDocument"/> the test needs indexed.
    /// </summary>
    /// <remarks>
    /// A deliberate deviation from the real production scoped lifetime -- <c>09.Search</c>'s own
    /// <c>AddIndex&lt;TDocument&gt;</c> registers <see cref="ISearchIndex{TDocument}"/> scoped,
    /// mirroring <c>InMemoryMessageBus</c>/<c>InMemoryEventPublisher</c>'s own already-documented
    /// scoped-to-singleton deviation for the identical reason: the same recorded-history instance
    /// must outlive the system-under-test's DI scope so post-hoc assertions can run after the
    /// action completes.
    /// </remarks>
    /// <typeparam name="TDocument">The search document type.</typeparam>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="definition">The index definition the fake validates requests against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemorySearchIndex<TDocument>(this IServiceCollection services, SearchIndexDefinition definition)
        where TDocument : class, ISearchDocument
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        services.AddSingleton(new InMemorySearchIndex<TDocument>(definition));
        services.AddSingleton<ISearchIndex<TDocument>>(sp => sp.GetRequiredService<InMemorySearchIndex<TDocument>>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemorySearchIndexProvisioner"/> as the
    /// <see cref="ISearchIndexProvisioner"/> singleton and
    /// <see cref="InMemorySearchProviderDescriptor"/> (constructed with
    /// <paramref name="providerName"/>) as the <see cref="ISearchProviderDescriptor"/> singleton.
    /// The two registered instances remain independent -- see
    /// <see cref="InMemorySearchIndex{TDocument}"/>'s remarks for the full non-coupling rationale.
    /// </summary>
    /// <remarks>
    /// Both registrations match their respective interface's real production lifetime exactly
    /// (both are root/singleton-shaped in production already) -- unlike
    /// <see cref="AddInMemorySearchIndex{TDocument}"/>, there is no lifetime deviation to flag here.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="providerName">The provider name <see cref="InMemorySearchProviderDescriptor"/> reports.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemorySearchProvisioning(this IServiceCollection services, string providerName = "in-memory-fake")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(providerName);

        services.AddSingleton<ISearchIndexProvisioner, InMemorySearchIndexProvisioner>();
        services.AddSingleton<ISearchProviderDescriptor>(_ => new InMemorySearchProviderDescriptor(providerName));
        return services;
    }
}
