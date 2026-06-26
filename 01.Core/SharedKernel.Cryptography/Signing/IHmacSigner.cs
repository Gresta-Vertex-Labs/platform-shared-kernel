namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using a keyed-hash message authentication code (HMAC).
/// </summary>
public interface IHmacSigner
{
    /// <summary>
    /// Computes the HMAC of <paramref name="data"/> using <paramref name="secret"/>.
    /// </summary>
    /// <param name="data">The data to sign.</param>
    /// <param name="secret">The shared secret key.</param>
    /// <returns>The raw HMAC bytes.</returns>
    byte[] Sign(byte[] data, byte[] secret);

    /// <summary>
    /// Verifies that <paramref name="signature"/> is the correct HMAC of <paramref name="data"/>
    /// under <paramref name="secret"/>, using a constant-time comparison.
    /// </summary>
    /// <param name="data">The data that was signed.</param>
    /// <param name="signature">The HMAC to verify.</param>
    /// <param name="secret">The shared secret key.</param>
    /// <returns><c>true</c> if the signature is valid; otherwise <c>false</c>.</returns>
    bool Verify(byte[] data, byte[] signature, byte[] secret);
}
