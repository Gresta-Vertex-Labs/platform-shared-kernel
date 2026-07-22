using Microsoft.Extensions.DependencyInjection;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.Testing.Intelligence;

/// <summary>
/// DI convenience extensions registering the in-memory intelligence test doubles.
/// </summary>
public static class IntelligenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryEmbeddingGenerator"/> as the <see cref="IEmbeddingGenerator"/>
    /// singleton, bound to <paramref name="modelId"/>/<paramref name="dimension"/>.
    /// </summary>
    /// <remarks>
    /// Matches the real contract's own "engine/model clients are singletons" rule exactly -- no
    /// lifetime deviation to flag here, unlike <see cref="AddInMemoryVectorCollection{TRecord}"/>
    /// below.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="modelId">The embedding model identity the fake reports and validates against.</param>
    /// <param name="dimension">The vector dimension the fake produces. Must be positive.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryEmbeddingGenerator(this IServiceCollection services, string modelId, int dimension)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new InMemoryEmbeddingGenerator(modelId, dimension));
        services.AddSingleton<IEmbeddingGenerator>(sp => sp.GetRequiredService<InMemoryEmbeddingGenerator>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryVectorCollection{TRecord}"/> as the
    /// <see cref="IVectorCollection{TRecord}"/> singleton for <typeparamref name="TRecord"/>,
    /// constructed against <paramref name="definition"/>. Call once per <typeparamref name="TRecord"/>
    /// the test needs.
    /// </summary>
    /// <remarks>
    /// A deliberate deviation from the real production scoped per-collection registration (per
    /// <c>10.Intelligence</c>'s own DI Registration rule: "per-collection and per-request services
    /// are scoped"), mirroring <c>AddInMemorySearchIndex&lt;TDocument&gt;</c>'s identical documented
    /// deviation: the same recorded-history instance must outlive the system-under-test's DI scope
    /// so post-hoc assertions can run after the action completes.
    /// </remarks>
    /// <typeparam name="TRecord">The vector record type.</typeparam>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="definition">The collection definition the fake validates requests against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryVectorCollection<TRecord>(this IServiceCollection services, VectorCollectionDefinition definition)
        where TRecord : class, IVectorRecord
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        services.AddSingleton(new InMemoryVectorCollection<TRecord>(definition));
        services.AddSingleton<IVectorCollection<TRecord>>(sp => sp.GetRequiredService<InMemoryVectorCollection<TRecord>>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryVectorCollectionProvisioner"/> as the
    /// <see cref="IVectorCollectionProvisioner"/> singleton and
    /// <see cref="InMemoryVectorProviderDescriptor"/> (constructed with <paramref name="providerName"/>)
    /// as the <see cref="IVectorProviderDescriptor"/> singleton. The two registered instances remain
    /// independent -- see <see cref="InMemoryVectorCollectionProvisioner"/>'s remarks for the full
    /// non-coupling rationale.
    /// </summary>
    /// <remarks>
    /// Both registrations match their respective interface's real production lifetime exactly (both
    /// are root/singleton-shaped in production already) -- no lifetime deviation to flag here.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="providerName">The provider name <see cref="InMemoryVectorProviderDescriptor"/> reports.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryVectorProvisioning(this IServiceCollection services, string providerName = "in-memory-fake")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(providerName);

        services.AddSingleton<IVectorCollectionProvisioner, InMemoryVectorCollectionProvisioner>();
        services.AddSingleton<IVectorProviderDescriptor>(_ => new InMemoryVectorProviderDescriptor(providerName));
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemorySemanticKernel"/> as the <see cref="ISemanticKernel"/> singleton
    /// and <see cref="InMemoryCompletionProviderDescriptor"/> as the
    /// <see cref="ICompletionProviderDescriptor"/> singleton.
    /// </summary>
    /// <remarks>
    /// The singleton choice for <see cref="ISemanticKernel"/> is ASSUMED, not yet confirmed against
    /// a real lifetime, since <c>10.Intelligence</c>'s own <c>AddSharedKernelSemanticKernel()</c> DI
    /// builder has not shipped -- reconcile this registration once it does.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemorySemanticKernel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISemanticKernel, InMemorySemanticKernel>();
        services.AddSingleton<ICompletionProviderDescriptor, InMemoryCompletionProviderDescriptor>();
        return services;
    }
}
