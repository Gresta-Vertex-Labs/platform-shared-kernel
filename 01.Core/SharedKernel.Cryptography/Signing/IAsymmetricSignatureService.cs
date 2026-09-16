namespace SharedKernel.Cryptography.Signing;

/// <summary>Signs and verifies data with keys from an <see cref="ISigningKeyProvider"/>.</summary>
/// <remarks>
/// <para>
/// Each key carries its own <see cref="SignatureAlgorithm"/>; callers name only the key. Data is hashed locally with
/// the key's digest algorithm, and only the digest is signed, so a key held in a key management service never
/// receives the data.
/// </para>
/// <para>
/// Signing with an unknown key id throws <see cref="KeyNotFoundException"/>: the caller chose the id. Verifying with
/// an unknown key id, or a malformed signature, returns <see langword="false"/>: both usually come from input.
/// </para>
/// </remarks>
public interface IAsymmetricSignatureService
{
    /// <summary>Signs <paramref name="data"/>.</summary>
    /// <param name="data">The data to sign.</param>
    /// <param name="keyId">The id of the signing key.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup or remote signing.</param>
    /// <returns>The signature.</returns>
    /// <exception cref="KeyNotFoundException">No key has <paramref name="keyId"/>.</exception>
    ValueTask<byte[]> SignAsync(ReadOnlyMemory<byte> data, string keyId, CancellationToken cancellationToken = default);

    /// <summary>Signs the contents of a stream, reading it to the end without buffering it.</summary>
    /// <param name="data">The stream to sign.</param>
    /// <param name="keyId">The id of the signing key.</param>
    /// <param name="cancellationToken">A token to cancel reading, the key lookup or remote signing.</param>
    /// <returns>The signature.</returns>
    /// <exception cref="KeyNotFoundException">No key has <paramref name="keyId"/>.</exception>
    ValueTask<byte[]> SignAsync(Stream data, string keyId, CancellationToken cancellationToken = default);

    /// <summary>Verifies a signature over <paramref name="data"/>.</summary>
    /// <param name="data">The signed data.</param>
    /// <param name="signature">The signature.</param>
    /// <param name="keyId">The id of the key that signed it.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns><see langword="true"/> only when the key exists and the signature is valid.</returns>
    ValueTask<bool> VerifyAsync(
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<byte> signature,
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies a signature over the contents of a stream, reading it to the end without buffering it.</summary>
    /// <param name="data">The signed stream.</param>
    /// <param name="signature">The signature.</param>
    /// <param name="keyId">The id of the key that signed it.</param>
    /// <param name="cancellationToken">A token to cancel reading or the key lookup.</param>
    /// <returns><see langword="true"/> only when the key exists and the signature is valid.</returns>
    ValueTask<bool> VerifyAsync(
        Stream data,
        ReadOnlyMemory<byte> signature,
        string keyId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the algorithm of a key, for example to write the <c>alg</c> header of a JWS.</summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns>The algorithm.</returns>
    /// <exception cref="KeyNotFoundException">No key has <paramref name="keyId"/>.</exception>
    ValueTask<SignatureAlgorithm> GetAlgorithmAsync(string keyId, CancellationToken cancellationToken = default);
}
