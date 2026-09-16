namespace SharedKernel.Cryptography.Random;

/// <summary>Produces cryptographically secure random values.</summary>
/// <remarks>
/// Use this for every security-sensitive random value: keys, salts, nonces, tokens, one-time secrets and recovery
/// codes. <see cref="System.Random"/> is predictable and <see cref="Guid.NewGuid"/> is not specified to be
/// unpredictable, so neither is an acceptable substitute. The default implementation,
/// <see cref="SecureRandomGenerator"/>, delegates to the operating system's CSPRNG.
/// </remarks>
public interface ISecureRandomGenerator
{
    /// <summary>Returns a new array of random bytes.</summary>
    /// <param name="length">The number of bytes. Must be positive.</param>
    /// <returns>A new array of <paramref name="length"/> random bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is zero or negative.</exception>
    byte[] GetBytes(int length);

    /// <summary>Fills <paramref name="destination"/> with random bytes.</summary>
    /// <param name="destination">The buffer to fill.</param>
    void Fill(Span<byte> destination);

    /// <summary>Returns a uniformly distributed random integer in <c>[0, <paramref name="toExclusive"/>)</c>.</summary>
    /// <param name="toExclusive">The exclusive upper bound. Must be positive.</param>
    /// <returns>A random integer greater than or equal to zero and less than <paramref name="toExclusive"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="toExclusive"/> is zero or negative.</exception>
    int GetInt32(int toExclusive);

    /// <summary>
    /// Returns a string of <paramref name="length"/> characters, each chosen uniformly from
    /// <paramref name="alphabet"/>.
    /// </summary>
    /// <param name="alphabet">The characters to choose from. Must not be empty.</param>
    /// <param name="length">The number of characters. Must be positive.</param>
    /// <returns>The random string.</returns>
    /// <exception cref="ArgumentException"><paramref name="alphabet"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is zero or negative.</exception>
    string GetString(ReadOnlySpan<char> alphabet, int length);

    /// <summary>
    /// Returns a token of <paramref name="byteCount"/> random bytes encoded as unpadded Base64Url, safe for URLs,
    /// query strings, headers and file names.
    /// </summary>
    /// <param name="byteCount">
    /// The number of random bytes before encoding. Defaults to 32 (256 bits). Must be at least 16.
    /// </param>
    /// <returns>The encoded token. 32 bytes encode to 43 characters.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="byteCount"/> is less than 16.</exception>
    string GetToken(int byteCount = 32);
}
