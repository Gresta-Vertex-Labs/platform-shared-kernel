namespace SharedKernel.Cryptography.Signing;

/// <summary>Resolves the <see cref="SigningKey"/> instances <see cref="IAsymmetricSignatureService"/> uses.</summary>
/// <remarks>
/// The provider owns the keys it returns; callers never dispose them. Throw when a remote key service is unreachable
/// or denies access.
/// </remarks>
public interface ISigningKeyProvider
{
    /// <summary>Gets the key with id <paramref name="keyId"/>.</summary>
    /// <param name="keyId">
    /// The key id. When verifying, it may come from untrusted input such as a token header, so an implementation must
    /// not let arbitrary ids trigger unbounded work or remote calls.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>The key, or <see langword="null"/> when no key has that id.</returns>
    ValueTask<SigningKey?> GetSigningKeyAsync(string keyId, CancellationToken cancellationToken = default);
}
