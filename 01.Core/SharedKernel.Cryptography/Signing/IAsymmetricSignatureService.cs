namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using an asymmetric algorithm (RSA or ECDSA).
/// </summary>
public interface IAsymmetricSignatureService
{
    /// <summary>
    /// Signs <paramref name="data"/> using the private key identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="data">The data to sign.</param>
    /// <param name="keyId">The identifier of the key pair to sign with, resolved by an <see cref="IAsymmetricKeyProvider"/> implemented by the consuming service.</param>
    /// <returns>The raw signature bytes.</returns>
    byte[] Sign(byte[] data, string keyId);

    /// <summary>
    /// Verifies that <paramref name="signature"/> is a valid signature of <paramref name="data"/>
    /// produced by the private key paired with the public key identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="data">The data that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="keyId">The identifier of the key pair to verify against.</param>
    /// <returns><c>true</c> if the signature is valid; otherwise <c>false</c>.</returns>
    bool Verify(byte[] data, byte[] signature, string keyId);
}
