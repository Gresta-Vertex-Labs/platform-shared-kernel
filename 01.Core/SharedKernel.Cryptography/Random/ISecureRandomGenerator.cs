namespace SharedKernel.Cryptography.Random;

/// <summary>
/// Generates cryptographically secure random bytes and tokens.
/// </summary>
/// <remarks>
/// This is the only permitted source of randomness for security-sensitive values (tokens, keys,
/// nonces, salts) anywhere in the platform. <see cref="System.Random"/> and
/// <see cref="Guid.NewGuid"/> are never acceptable substitutes.
/// </remarks>
public interface ISecureRandomGenerator
{
    /// <summary>
    /// Generates <paramref name="length"/> cryptographically secure random bytes.
    /// </summary>
    /// <param name="length">The number of bytes to generate.</param>
    /// <returns>A new byte array of length <paramref name="length"/>.</returns>
    byte[] NextBytes(int length);

    /// <summary>
    /// Generates a cryptographically secure random token encoded as URL-safe Base64 (no padding) —
    /// safe to embed in query strings or HTTP headers.
    /// </summary>
    /// <param name="length">The number of underlying random bytes to generate before encoding. Defaults to 32.</param>
    /// <returns>A URL-safe Base64-encoded token string.</returns>
    string NextToken(int length = 32);
}
