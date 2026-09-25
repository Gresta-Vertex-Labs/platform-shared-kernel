using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage;

namespace SharedKernel.Testing.Storage;

/// <summary>Builds a storage registry over in-memory stores without a host, for tests of code that takes an <see cref="IFileStorageFactory"/>.</summary>
public static class InMemoryStorage
{
    /// <summary>Returns the <see cref="IFileStorageFactory"/> of a registry configured by <paramref name="configure"/>.</summary>
    /// <param name="configure">Adds stores, e.g. <c>b =&gt; b.AddInMemoryStore(store)</c>.</param>
    /// <returns>The factory; it validates requests and applies tenant prefixes exactly as in production.</returns>
    /// <example>
    /// <code>
    /// var reports = new InMemoryFileStorage("reports");
    /// IFileStorageFactory factory = InMemoryStorage.CreateFactory(b =&gt; b.AddInMemoryStore(reports));
    /// </code>
    /// </example>
    public static IFileStorageFactory CreateFactory(Action<IStorageBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var services = new ServiceCollection();
        configure(services.AddSharedKernelStorage());
        return services.BuildServiceProvider().GetRequiredService<IFileStorageFactory>();
    }

    /// <summary>Returns the <see cref="IFileStorageFactory"/> of a registry holding <paramref name="stores"/> as shared stores.</summary>
    /// <param name="stores">The stores, kept by the test for inspection.</param>
    /// <returns>The factory.</returns>
    public static IFileStorageFactory CreateFactory(params InMemoryFileStorage[] stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        return CreateFactory(builder =>
        {
            foreach (InMemoryFileStorage store in stores)
            {
                builder.AddInMemoryStore(store);
            }
        });
    }
}
