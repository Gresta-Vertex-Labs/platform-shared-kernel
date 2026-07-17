using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage.Abstractions.Abstractions;

namespace SharedKernel.Testing.Storage;

/// <summary>
/// DI convenience extensions registering the in-memory storage test doubles.
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryFileStorage"/> as the <see cref="IFileStorage"/> singleton and
    /// <see cref="InMemoryBlobUriGenerator"/> as the <see cref="IBlobUriGenerator"/> singleton.
    /// </summary>
    /// <remarks>
    /// Unlike <c>AddInMemoryMessageBus()</c>/<c>AddInMemoryEventPublisher()</c> (which deliberately
    /// diverge from a scoped production lifetime), this registration's singleton lifetime matches
    /// <see cref="IFileStorage"/>/<see cref="IBlobUriGenerator"/>'s own production lifetime exactly —
    /// both are already registered as singletons by <c>AddSharedKernelS3Storage()</c>/
    /// <c>AddSharedKernelObsStorage()</c> in <c>08.Storage</c> — so there is no lifetime deviation to
    /// document here.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddInMemoryFileStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<InMemoryFileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<InMemoryFileStorage>());
        services.AddSingleton<InMemoryBlobUriGenerator>();
        services.AddSingleton<IBlobUriGenerator>(sp => sp.GetRequiredService<InMemoryBlobUriGenerator>());
        return services;
    }
}
