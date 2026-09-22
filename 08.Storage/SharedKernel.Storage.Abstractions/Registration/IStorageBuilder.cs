using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Storage;

/// <summary>
/// Registers storage providers and their named stores; returned by
/// <see cref="StorageServiceCollectionExtensions.AddSharedKernelStorage"/>.
/// </summary>
/// <remarks>
/// Provider packages add extension methods to this interface, such as <c>AddS3</c> and <c>AddObs</c>, and
/// contribute stores with
/// <see cref="StorageServiceCollectionExtensions.AddStore(IStorageBuilder, FileStoreRegistration)"/>. Store names are
/// unique across all providers of the service.
/// </remarks>
public interface IStorageBuilder
{
    /// <summary>Gets the service collection stores are registered into.</summary>
    IServiceCollection Services { get; }
}
