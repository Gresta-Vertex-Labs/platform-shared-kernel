using System.Buffers.Text;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Random;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// A test double for <see cref="ISecureRandomGenerator"/>: cryptographically random by default, or reproducible when
/// given a seed.
/// </summary>
/// <remarks>A seeded instance is predictable by design. Never use it outside tests.</remarks>
public sealed class FakeSecureRandomGenerator : ISecureRandomGenerator
{
    private readonly System.Random? _seeded;
    private readonly Lock _gate = new();

    /// <summary>Creates the generator.</summary>
    /// <param name="seed">A seed for reproducible values, or <see langword="null"/> for real randomness.</param>
    public FakeSecureRandomGenerator(int? seed = null) =>
        _seeded = seed is int value ? new System.Random(value) : null;

    /// <inheritdoc />
    public byte[] GetBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        byte[] bytes = new byte[length];
        Fill(bytes);
        return bytes;
    }

    /// <inheritdoc />
    public void Fill(Span<byte> destination)
    {
        if (_seeded is null)
        {
            RandomNumberGenerator.Fill(destination);
            return;
        }

        lock (_gate)
        {
            _seeded.NextBytes(destination);
        }
    }

    /// <inheritdoc />
    public int GetInt32(int toExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toExclusive);
        if (_seeded is null)
        {
            return RandomNumberGenerator.GetInt32(toExclusive);
        }

        lock (_gate)
        {
            return _seeded.Next(toExclusive);
        }
    }

    /// <inheritdoc />
    public string GetString(ReadOnlySpan<char> alphabet, int length)
    {
        if (alphabet.IsEmpty)
        {
            throw new ArgumentException("The alphabet must contain at least one character.", nameof(alphabet));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        char[] result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = alphabet[GetInt32(alphabet.Length)];
        }

        return new string(result);
    }

    /// <inheritdoc />
    public string GetToken(int byteCount = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteCount, SecureRandomGenerator.MinimumTokenBytes);
        return Base64Url.EncodeToString(GetBytes(byteCount));
    }
}
