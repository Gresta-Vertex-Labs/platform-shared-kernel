using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Random;

/// <summary>
/// Generates cryptographically secure random bytes and tokens, backed by
/// <see cref="RandomNumberGenerator"/>.
/// </summary>
public sealed class CryptoRandomGenerator : ISecureRandomGenerator
{
    /// <inheritdoc />
    public byte[] NextBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        return RandomNumberGenerator.GetBytes(length);
    }

    /// <inheritdoc />
    public string NextToken(int length = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        byte[] bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
