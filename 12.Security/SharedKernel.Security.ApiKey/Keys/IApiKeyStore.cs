namespace SharedKernel.Security.ApiKey.Keys;

/// <summary>Looks up managed API keys by key id.</summary>
/// <remarks>
/// Implemented by the consuming service over its own table or secret store. The store holds only hashes; the
/// validator checks the hash, expiry and revocation. Cache lookups if the store is remote, and evict the entry when a
/// key is revoked.
/// </remarks>
public interface IApiKeyStore
{
    /// <summary>Finds a key.</summary>
    /// <param name="keyId">The key id parsed from the presented key.</param>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>The record, or <see langword="null"/> when no key has this id.</returns>
    ValueTask<ApiKeyRecord?> FindAsync(string keyId, CancellationToken cancellationToken);
}
