using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage;

namespace SharedKernel.Testing.Storage;

/// <summary>
/// Registers <see cref="InMemoryFileStorage"/> stores with SharedKernel storage, as a provider package
/// registers its stores: <c>services.AddSharedKernelStorage().AddInMemoryStore("invoices")</c>.
/// </summary>
/// <remarks>
/// <para>
/// Each store goes through the real storage registry, so application code resolves it exactly as in
/// production — <c>[FromKeyedServices("invoices")] IFileStorage</c>, <see cref="ITenantFileStorage"/>,
/// <see cref="IFileStorageFactory"/>, the store's readiness probe — with the registry's request
/// validation and tenant isolation in front of it.
/// </para>
/// <para>
/// The raw store is also registered as a keyed singleton <see cref="InMemoryFileStorage"/> under the
/// store name, so a test can inspect or seed it: <c>provider.GetInMemoryStore("invoices")</c>. Its keys
/// are store keys — see <see cref="InMemoryFileStorage.TenantKey"/> for a tenant store.
/// </para>
/// </remarks>
public static class InMemoryStorageBuilderExtensions
{
    /// <summary>Adds an in-memory store shared by all tenants, resolvable as <c>[FromKeyedServices(name)] IFileStorage</c>.</summary>
    /// <param name="builder">The storage builder from <c>AddSharedKernelStorage()</c>.</param>
    /// <param name="name">The store name.</param>
    /// <param name="configure">Sets the store's options.</param>
    /// <returns>The same builder.</returns>
    public static IStorageBuilder AddInMemoryStore(
        this IStorageBuilder builder,
        string name,
        Action<InMemoryFileStorageOptions>? configure = null) =>
        builder.AddInMemoryStore(Create(name, configure), tenantScoped: false);

    /// <summary>
    /// Adds an in-memory tenant-scoped store, resolvable as <c>[FromKeyedServices(name)] ITenantFileStorage</c>,
    /// whose tenant views keep each tenant under <c>tenants/{tenantId}/</c>.
    /// </summary>
    /// <param name="builder">The storage builder from <c>AddSharedKernelStorage()</c>.</param>
    /// <param name="name">The store name.</param>
    /// <param name="configure">Sets the store's options.</param>
    /// <returns>The same builder.</returns>
    public static IStorageBuilder AddInMemoryTenantStore(
        this IStorageBuilder builder,
        string name,
        Action<InMemoryFileStorageOptions>? configure = null) =>
        builder.AddInMemoryStore(Create(name, configure), tenantScoped: true);

    /// <summary>Adds an existing in-memory store, shared by all tenants, under its <see cref="InMemoryFileStorage.StoreName"/>.</summary>
    /// <param name="builder">The storage builder from <c>AddSharedKernelStorage()</c>.</param>
    /// <param name="store">The store, kept by the test for inspection.</param>
    /// <returns>The same builder.</returns>
    public static IStorageBuilder AddInMemoryStore(this IStorageBuilder builder, InMemoryFileStorage store) =>
        builder.AddInMemoryStore(store, tenantScoped: false);

    /// <summary>Adds an existing in-memory store as a tenant-scoped store under its <see cref="InMemoryFileStorage.StoreName"/>.</summary>
    /// <param name="builder">The storage builder from <c>AddSharedKernelStorage()</c>.</param>
    /// <param name="store">The store, kept by the test for inspection.</param>
    /// <returns>The same builder.</returns>
    public static IStorageBuilder AddInMemoryTenantStore(this IStorageBuilder builder, InMemoryFileStorage store) =>
        builder.AddInMemoryStore(store, tenantScoped: true);

    /// <summary>Returns the raw in-memory store registered under <paramref name="name"/>, for inspection and seeding.</summary>
    /// <param name="services">The service provider.</param>
    /// <param name="name">The store name.</param>
    /// <returns>The store.</returns>
    public static InMemoryFileStorage GetInMemoryStore(this IServiceProvider services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredKeyedService<InMemoryFileStorage>(name);
    }

    private static InMemoryFileStorage Create(string name, Action<InMemoryFileStorageOptions>? configure)
    {
        var options = new InMemoryFileStorageOptions();
        configure?.Invoke(options);
        return new InMemoryFileStorage(name, options);
    }

    private static IStorageBuilder AddInMemoryStore(this IStorageBuilder builder, InMemoryFileStorage store, bool tenantScoped)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);

        builder.AddStore(new FileStoreRegistration(
            store.StoreName,
            tenantScoped,
            _ => store,
            (_, cancellationToken) => store.ProbeAsync(cancellationToken)));
        builder.Services.AddKeyedSingleton(store.StoreName, store);
        return builder;
    }
}
