namespace SharedKernel.Storage;

/// <summary>The names of the readiness probes storage registers — one per named store.</summary>
public static class StorageReadinessProbeNames
{
    /// <summary>The prefix of every store's probe name.</summary>
    public const string Prefix = "storage-";

    /// <summary>
    /// The name of the readiness probe of the store named <paramref name="storeName"/> — <c>storage-{storeName}</c>.
    /// Every <c>AddStore</c> and <c>AddTenantStore</c> registers one; it checks the store's bucket is reachable with
    /// the configured credentials.
    /// </summary>
    /// <param name="storeName">The store name.</param>
    /// <returns>The probe name.</returns>
    /// <exception cref="ArgumentException"><paramref name="storeName"/> is <see langword="null"/>, empty or whitespace.</exception>
    public static string ForStore(string storeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        return Prefix + storeName;
    }
}
