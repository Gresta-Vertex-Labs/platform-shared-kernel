using System.Buffers.Text;
using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Random;

/// <summary><see cref="ISecureRandomGenerator"/> backed by <see cref="RandomNumberGenerator"/>.</summary>
/// <remarks>Stateless and thread-safe.</remarks>
public sealed class SecureRandomGenerator : ISecureRandomGenerator
{
    /// <summary>The smallest byte count <see cref="GetToken"/> accepts: 128 bits.</summary>
    public const int MinimumTokenBytes = 16;

    /// <inheritdoc />
    public byte[] GetBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        return RandomNumberGenerator.GetBytes(length);
    }

    /// <inheritdoc />
    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);

    /// <inheritdoc />
    public int GetInt32(int toExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toExclusive);
        return RandomNumberGenerator.GetInt32(toExclusive);
    }

    /// <inheritdoc />
    public string GetString(ReadOnlySpan<char> alphabet, int length)
    {
        if (alphabet.IsEmpty)
        {
            throw new ArgumentException("The alphabet must contain at least one character.", nameof(alphabet));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        return RandomNumberGenerator.GetString(alphabet, length);
    }

    /// <inheritdoc />
    public string GetToken(int byteCount = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteCount, MinimumTokenBytes);

        Span<byte> bytes = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        try
        {
            RandomNumberGenerator.Fill(bytes);
            return Base64Url.EncodeToString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
