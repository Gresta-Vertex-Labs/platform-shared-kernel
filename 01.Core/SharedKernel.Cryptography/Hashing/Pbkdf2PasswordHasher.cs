using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Hashes and verifies passwords using PBKDF2-HMACSHA256
/// (<see cref="Rfc2898DeriveBytes.Pbkdf2(byte[], byte[], int, HashAlgorithmName, int)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The encoded output is self-describing: it embeds a format marker, the iteration count, the
/// salt, and the derived subkey, all in a single Base64 string. This means
/// <see cref="CryptographyOptions.Pbkdf2Iterations"/> can be raised later without invalidating
/// hashes already stored — <see cref="Verify"/> reads the iteration count back out of the hash
/// itself and reports <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> when the
/// stored count no longer matches the currently configured value.
/// </para>
/// <para>Binary layout (all integers big-endian):</para>
/// <code>
/// [1 byte format marker = 0x01]
/// [4 bytes iteration count]
/// [2 bytes salt length]
/// [salt bytes]
/// [subkey bytes (32 bytes, SHA-256 output size)]
/// </code>
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const byte FormatMarker = 0x01;
    private const int SaltSize = 16;
    private const int SubkeySize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    private readonly IOptionsMonitor<CryptographyOptions> _options;

    /// <summary>Creates a new <see cref="Pbkdf2PasswordHasher"/>.</summary>
    /// <param name="options">The monitored cryptography options supplying the iteration count.</param>
    public Pbkdf2PasswordHasher(IOptionsMonitor<CryptographyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        int iterations = _options.CurrentValue.Pbkdf2Iterations;
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, SubkeySize);

        return Encode(iterations, salt, subkey);
    }

    /// <inheritdoc />
    public PasswordVerificationResult Verify(string hash, string password)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(password);

        if (!TryDecode(hash, out int storedIterations, out byte[]? salt, out byte[]? expectedSubkey))
        {
            return PasswordVerificationResult.Failed;
        }

        byte[] actualSubkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, storedIterations, Algorithm, expectedSubkey.Length);

        bool matches = CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
        if (!matches)
        {
            return PasswordVerificationResult.Failed;
        }

        int currentIterations = _options.CurrentValue.Pbkdf2Iterations;
        return storedIterations == currentIterations
            ? PasswordVerificationResult.Success
            : PasswordVerificationResult.SuccessRehashNeeded;
    }

    private static string Encode(int iterations, byte[] salt, byte[] subkey)
    {
        int length = 1 + 4 + 2 + salt.Length + subkey.Length;
        byte[] buffer = new byte[length];
        Span<byte> span = buffer;

        span[0] = FormatMarker;
        BinaryPrimitives.WriteInt32BigEndian(span[1..5], iterations);
        BinaryPrimitives.WriteUInt16BigEndian(span[5..7], (ushort)salt.Length);
        salt.CopyTo(span[7..]);
        subkey.CopyTo(span[(7 + salt.Length)..]);

        return Convert.ToBase64String(buffer);
    }

    private static bool TryDecode(
        string encoded,
        out int iterations,
        [NotNullWhen(true)] out byte[]? salt,
        [NotNullWhen(true)] out byte[]? subkey)
    {
        iterations = 0;
        salt = null;
        subkey = null;

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (buffer.Length < 1 + 4 + 2 || buffer[0] != FormatMarker)
        {
            return false;
        }

        ReadOnlySpan<byte> span = buffer;
        iterations = BinaryPrimitives.ReadInt32BigEndian(span[1..5]);
        ushort saltLength = BinaryPrimitives.ReadUInt16BigEndian(span[5..7]);

        int saltStart = 7;
        int subkeyStart = saltStart + saltLength;
        if (buffer.Length <= subkeyStart)
        {
            return false;
        }

        salt = span[saltStart..subkeyStart].ToArray();
        subkey = span[subkeyStart..].ToArray();
        return true;
    }
}
