namespace SharedKernel.Cryptography.Signing;

/// <summary>Computes and verifies HMAC-SHA256 authentication codes with a shared secret key.</summary>
/// <remarks>
/// Keys must be at least 32 random bytes. Do not use HMAC to compare two secrets for equality; use
/// <see cref="FixedTimeComparison"/>.
/// </remarks>
public interface IHmacSigner
{
    /// <summary>Computes the HMAC of <paramref name="data"/>.</summary>
    /// <param name="data">The data.</param>
    /// <param name="key">The shared key. At least 32 bytes.</param>
    /// <returns>The 32-byte HMAC.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is shorter than 32 bytes.</exception>
    byte[] Sign(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key);

    /// <summary>Verifies an HMAC in fixed time.</summary>
    /// <param name="data">The data.</param>
    /// <param name="signature">The HMAC to check.</param>
    /// <param name="key">The shared key. At least 32 bytes.</param>
    /// <returns><see langword="true"/> when <paramref name="signature"/> is the HMAC of <paramref name="data"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is shorter than 32 bytes.</exception>
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> key);
}
