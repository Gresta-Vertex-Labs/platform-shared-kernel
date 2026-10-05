using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Storage;

/// <summary>Registers SharedKernel storage and its named stores.</summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the store registry — <see cref="IFileStorageFactory"/>
    /// and the unkeyed <see cref="IFileStorage"/>/<see cref="ITenantFileStorage"/>, all singletons — and returns a
    /// builder to add a provider and its stores to.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The builder; chain a provider such as <c>.AddS3(configuration).AddStore("invoices")</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Every store is a keyed singleton under its exact registered name: <c>[FromKeyedServices("invoices")]
    /// IFileStorage</c> for a shared store, <c>[FromKeyedServices("documents")] ITenantFileStorage</c> for a tenant
    /// store. The unkeyed <see cref="IFileStorage"/> resolves only when exactly one shared store is registered, and
    /// the unkeyed <see cref="ITenantFileStorage"/> only when exactly one tenant store is; otherwise resolving them
    /// throws <see cref="InvalidOperationException"/> naming the stores to choose from.
    /// </para>
    /// <para>
    /// Stores and their provider clients are created on first use; the clients are disposed with the container.
    /// Provider settings are validated when the host starts (<c>OptionsValidationException</c> from
    /// <c>IHost.StartAsync</c>); without a started host, the first resolution of a store throws it instead.
    /// </para>
    /// <para>Calling this more than once is harmless; every call returns a builder over the same registry.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddSharedKernelStorage()
    ///     .AddS3(builder.Configuration)   // connection: SharedKernel:Storage:S3
    ///     .AddStore("invoices")           // store: SharedKernel:Storage:Stores:invoices
    ///     .AddTenantStore("documents");   // store: SharedKernel:Storage:Stores:documents
    ///
    /// // Inject: [FromKeyedServices("invoices")] IFileStorage, [FromKeyedServices("documents")] ITenantFileStorage
    /// </code>
    /// </example>
    public static IStorageBuilder AddSharedKernelStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<FileStorageFactory>();
        services.TryAddSingleton<IFileStorageFactory>(sp => sp.GetRequiredService<FileStorageFactory>());
        services.TryAddSingleton<IFileStorage>(sp => sp.GetRequiredService<FileStorageFactory>().GetDefaultStore());
        services.TryAddSingleton<ITenantFileStorage>(sp => sp.GetRequiredService<FileStorageFactory>().GetDefaultTenantStore());

        return new StorageBuilder(services);
    }

    /// <summary>
    /// Adds a provider's store to the registry and registers it as a keyed singleton under its name — as
    /// <see cref="ITenantFileStorage"/> when <see cref="FileStoreRegistration.TenantScoped"/>, otherwise as
    /// <see cref="IFileStorage"/> — and registers the store's readiness probe, named
    /// <see cref="StorageReadinessProbeNames.ForStore(string)"/>. Called by provider packages; application code calls the provider's
    /// <c>AddStore</c> or <c>AddTenantStore</c>.
    /// </summary>
    /// <param name="builder">The storage builder.</param>
    /// <param name="registration">The store.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="registration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A store with the same name, ignoring case, is already registered, by any provider.
    /// </exception>
    public static IStorageBuilder AddStore(this IStorageBuilder builder, FileStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registration);

        IServiceCollection services = builder.Services;
        bool duplicate = services.Any(d =>
            d.ServiceType == typeof(FileStoreRegistration)
            && !d.IsKeyedService
            && d.ImplementationInstance is FileStoreRegistration existing
            && string.Equals(existing.Name, registration.Name, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            throw new InvalidOperationException($"A storage store named '{registration.Name}' is already registered.");
        }

        services.AddSingleton(registration);
        services.AddReadinessProbe(sp => new FileStoreReadinessProbe(registration, sp));

        string name = registration.Name;
        if (registration.TenantScoped)
        {
            services.AddKeyedSingleton<ITenantFileStorage>(
                name,
                (sp, _) => sp.GetRequiredService<FileStorageFactory>().GetTenantStore(name));
        }
        else
        {
            services.AddKeyedSingleton<IFileStorage>(
                name,
                (sp, _) => sp.GetRequiredService<FileStorageFactory>().GetStore(name));
        }

        return builder;
    }

    private sealed class StorageBuilder(IServiceCollection services) : IStorageBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
