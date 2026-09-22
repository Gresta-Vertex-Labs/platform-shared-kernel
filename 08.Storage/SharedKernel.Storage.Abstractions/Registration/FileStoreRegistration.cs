using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage;

/// <summary>
/// How a provider package contributes one named store to the registry, through
/// <see cref="StorageServiceCollectionExtensions.AddStore(IStorageBuilder, FileStoreRegistration)"/>. Application
/// code never creates this; it calls a provider's <c>AddStore</c> or <c>AddTenantStore</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Factory"/> builds the provider's raw store over the whole bucket (and store key prefix): its
/// <see cref="IFileStorage.StoreName"/> must equal <see cref="Name"/> exactly and its
/// <see cref="IFileStorage.TenantId"/> must be <see langword="null"/>, otherwise resolving the store throws
/// <see cref="InvalidOperationException"/>. The raw store is never handed out: the registry wraps it in a view
/// that validates every request before it reaches the provider, and for a <see cref="TenantScoped"/> store
/// callers only reach it through <see cref="ITenantFileStorage.ForTenant(string)"/>, which prefixes every key
/// with <c>tenants/{tenantId}/</c>.
/// </para>
/// <para>
/// A provider's raw store therefore receives keys that may carry a tenant prefix, and must accept any valid key.
/// </para>
/// </remarks>
public sealed class FileStoreRegistration
{
    /// <summary>Initializes a new instance of the <see cref="FileStoreRegistration"/> class.</summary>
    /// <param name="name">The store name; see <see cref="IsValidStoreName(string)"/>.</param>
    /// <param name="tenantScoped">
    /// <see langword="true"/> to make the store reachable only through tenant views (<see cref="ITenantFileStorage"/>);
    /// <see langword="false"/> for a store shared by all tenants (<see cref="IFileStorage"/>).
    /// </param>
    /// <param name="factory">
    /// Creates the provider's raw store from the root service provider; called once, on first use, and the result
    /// kept for the container's lifetime. Must not return <see langword="null"/>.
    /// </param>
    /// <param name="probe">
    /// Checks the store's bucket is reachable with the configured credentials, for
    /// <see cref="IFileStorageHealthProbe.ProbeAsync"/>; returns a failed <c>Result</c> rather than throwing.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid store name.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="factory"/> or <paramref name="probe"/> is <see langword="null"/>.
    /// </exception>
    public FileStoreRegistration(
        string name,
        bool tenantScoped,
        Func<IServiceProvider, IFileStorage> factory,
        Func<IServiceProvider, CancellationToken, Task<Result>> probe)
    {
        if (!IsValidStoreName(name))
        {
            throw new ArgumentException(
                $"Store name '{name}' is invalid: use 1 to 64 characters from A-Z, a-z, 0-9, '.', '_' and '-', starting with a letter or digit.",
                nameof(name));
        }

        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(probe);

        Name = name;
        TenantScoped = tenantScoped;
        Factory = factory;
        Probe = probe;
    }

    /// <summary>
    /// Gets the store name: the service key of the store and <see cref="FileReference.Store"/> of its references.
    /// </summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the store is only reachable through tenant views.</summary>
    public bool TenantScoped { get; }

    /// <summary>Gets the function that creates the provider's raw store; see the constructor.</summary>
    public Func<IServiceProvider, IFileStorage> Factory { get; }

    /// <summary>Gets the function that checks the store's bucket is reachable; see the constructor.</summary>
    public Func<IServiceProvider, CancellationToken, Task<Result>> Probe { get; }

    /// <summary>
    /// Reports whether <paramref name="name"/> can name a store: 1 to 64 characters from <c>A-Z a-z 0-9 . _ -</c>,
    /// starting with a letter or digit.
    /// </summary>
    /// <param name="name">The candidate name, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when valid; <see langword="false"/> for <see langword="null"/> or empty.
    /// </returns>
    public static bool IsValidStoreName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= 64
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
