using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Cryptography.Hashing;

/// <summary>PBKDF2-HMAC-SHA256, written as <c>$pbkdf2-sha256$i=iterations$salt$hash</c>.</summary>
/// <remarks>
/// <para>
/// The default <see cref="IOneWayHashAlgorithm"/>: FIPS 140-3 approved and built into .NET. New hashes use a 16-byte
/// salt, a 32-byte output and <see cref="Pbkdf2Options.Iterations"/>.
/// </para>
/// <para>
/// Verification accepts 1 to <see cref="Pbkdf2Options.MaximumIterations"/> iterations, a 16- to
/// 64-byte salt and exactly a 32-byte output, and rejects anything else before deriving.
/// </para>
/// </remarks>
public sealed class Pbkdf2OneWayHashAlgorithm : IOneWayHashAlgorithm
{
    /// <summary>The PHC algorithm identifier: <c>pbkdf2-sha256</c>.</summary>
    public const string Id = "pbkdf2-sha256";

    private const string IterationsParameter = "i";
    private const int SaltSize = 16;
    private const int MaxSaltSize = 64;
    private const int HashSize = 32;

    private readonly IOptionsMonitor<Pbkdf2Options> _options;

    /// <summary>Creates the algorithm.</summary>
    /// <param name="options">Supplies <see cref="Pbkdf2Options.Iterations"/>.</param>
    public Pbkdf2OneWayHashAlgorithm(IOptionsMonitor<Pbkdf2Options> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string AlgorithmId => Id;

    /// <inheritdoc />
    public PhcHashString Hash(ReadOnlySpan<byte> secret)
    {
        int iterations = _options.CurrentValue.Iterations;
        Span<byte> salt = stackalloc byte[SaltSize];
        Span<byte> hash = stackalloc byte[HashSize];
        RandomNumberGenerator.Fill(salt);

        try
        {
            Rfc2898DeriveBytes.Pbkdf2(secret, salt, hash, iterations, HashAlgorithmName.SHA256);
            return new PhcHashString(
                Id,
                version: null,
                [new(IterationsParameter, iterations.ToString(CultureInfo.InvariantCulture))],
                salt,
                hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    /// <inheritdoc />
    public bool Verify(PhcHashString hash, ReadOnlySpan<byte> secret)
    {
        ArgumentNullException.ThrowIfNull(hash);

        if (!string.Equals(hash.AlgorithmId, Id, StringComparison.Ordinal)
            || hash.Version is not null
            || hash.Parameters.Count != 1
            || !hash.TryGetInt32Parameter(IterationsParameter, out int iterations)
            || iterations is < 1 or > Pbkdf2Options.MaximumIterations
            || hash.Salt.Length is < SaltSize or > MaxSaltSize
            || hash.Hash.Length != HashSize)
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[HashSize];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(secret, hash.Salt, actual, iterations, HashAlgorithmName.SHA256);
            return CryptographicOperations.FixedTimeEquals(actual, hash.Hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    /// <inheritdoc />
    public bool RequiresRehash(PhcHashString hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        return !hash.TryGetInt32Parameter(IterationsParameter, out int iterations)
            || iterations != _options.CurrentValue.Iterations
            || hash.Salt.Length != SaltSize;
    }

    /// <summary>
    /// Reads the Base64 binary layout that pre-release builds of this package wrote:
    /// <c>[0x01][iterations: int32 BE][salt length: uint16 BE][salt][32-byte subkey]</c>.
    /// </summary>
    internal static bool TryParseLegacy(string encoded, [NotNullWhen(true)] out PhcHashString? hash)
    {
        hash = null;
        if (encoded.Length is < 8 or > 256)
        {
            return false;
        }

        byte[] buffer = new byte[encoded.Length * 3 / 4];
        if (!Convert.TryFromBase64String(encoded, buffer, out int written) || written < 7 || buffer[0] != 0x01)
        {
            return false;
        }

        ReadOnlySpan<byte> data = buffer.AsSpan(0, written);
        int iterations = BinaryPrimitives.ReadInt32BigEndian(data[1..5]);
        int saltLength = BinaryPrimitives.ReadUInt16BigEndian(data[5..7]);
        if (iterations < 1 || saltLength < SaltSize || data.Length != 7 + saltLength + HashSize)
        {
            return false;
        }

        hash = new PhcHashString(
            Id,
            version: null,
            [new(IterationsParameter, iterations.ToString(CultureInfo.InvariantCulture))],
            data.Slice(7, saltLength),
            data[(7 + saltLength)..]);
        return true;
    }
}
