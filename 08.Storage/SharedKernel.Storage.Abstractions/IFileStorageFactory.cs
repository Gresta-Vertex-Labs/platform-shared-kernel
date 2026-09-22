namespace SharedKernel.Storage;

/// <summary>Resolves registered stores by name, for code that picks its store at run time.</summary>
/// <remarks>
/// <para>
/// Prefer injecting the store itself (<c>[FromKeyedServices("invoices")] IFileStorage</c>). Use the factory for
/// a store named in data — a persisted <see cref="FileReference"/>, a job argument — or to enumerate stores.
/// Registered by <see cref="StorageServiceCollectionExtensions.AddSharedKernelStorage"/> as a singleton; safe for
/// concurrent use.
/// </para>
/// <para>
/// Name lookups ignore case (keyed injection does not: <c>[FromKeyedServices]</c> needs the registered
/// spelling). A store is created on first use, so a store whose settings are invalid can throw
/// <c>OptionsValidationException</c> on first resolution when the host was not started. An unknown name, or
/// asking for a tenant store as a shared one (or the reverse), is a programming or configuration error and throws
/// <see cref="InvalidOperationException"/> naming the registered stores.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Reopen an object from a reference persisted with the row that owns it.
/// FileReference reference = order.Invoice;
/// IFileStorage store = storageFactory.Open(reference);
/// Result&lt;FileDownload&gt; download = await store.DownloadAsync(reference.Key, cancellationToken: ct);
/// </code>
/// </example>
public interface IFileStorageFactory
{
    /// <summary>
    /// Gets the names of every registered store, shared and tenant-scoped, in their registered spelling.
    /// </summary>
    IReadOnlyCollection<string> StoreNames { get; }

    /// <summary>Reports whether <paramref name="storeName"/> is registered as tenant-scoped.</summary>
    /// <param name="storeName">The store name, compared ignoring case.</param>
    /// <returns><see langword="true"/> for a tenant store; <see langword="false"/> for a shared one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="storeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No store has that name.</exception>
    bool IsTenantScoped(string storeName);

    /// <summary>Returns the shared (not tenant-scoped) store named <paramref name="storeName"/>.</summary>
    /// <param name="storeName">The store name, compared ignoring case.</param>
    /// <returns>The store; the same singleton <c>[FromKeyedServices(name)] IFileStorage</c> resolves to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="storeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No store has that name, or it is tenant-scoped.</exception>
    IFileStorage GetStore(string storeName);

    /// <summary>Returns the tenant-scoped store named <paramref name="storeName"/>.</summary>
    /// <param name="storeName">The store name, compared ignoring case.</param>
    /// <returns>The store; call <see cref="ITenantFileStorage.ForTenant(string)"/> on it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="storeName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No store has that name, or it is shared by all tenants.</exception>
    ITenantFileStorage GetTenantStore(string storeName);

    /// <summary>Returns the store, or the tenant view of one, that <paramref name="reference"/> points into.</summary>
    /// <param name="reference">
    /// A reference returned by an earlier upload, copy or multipart completion. Its
    /// <see cref="FileReference.TenantId"/> selects the tenant view; load the reference through the same tenant
    /// filter as the row that holds it.
    /// </param>
    /// <returns>The store (or tenant view) to read <see cref="FileReference.Key"/> from.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="reference"/> or its <see cref="FileReference.Store"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The store is unknown, or the reference has no tenant for a tenant store (or a tenant for a shared one).
    /// </exception>
    /// <exception cref="ArgumentException">The reference's tenant id is not a valid tenant id.</exception>
    IFileStorage Open(FileReference reference);
}
