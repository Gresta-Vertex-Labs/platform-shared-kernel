using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IOneWayHasher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="Pbkdf2OneWayHasher"/>'s exact self-describing binary layout (format marker +
/// iteration count + salt + subkey, Base64-encoded) and PBKDF2-HMACSHA256 mechanism byte-for-byte —
/// the only difference is where the iteration count comes from: a plain settable
/// <see cref="Iterations"/> property instead of <c>IOptionsMonitor&lt;CryptographyOptions&gt;</c>,
/// defaulting to a tiny value instead of the real 600,000-iteration production default. A hash
/// produced by this fake round-trips byte-for-byte through <see cref="Verify"/> exactly like a real
/// PBKDF2-produced hash would.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> The whole point of this fake is to skip the real
/// 600,000-iteration cost. Wiring it into a production DI container by accident would make one-way
/// secret hashing trivially brute-forceable.
/// </para>
/// </remarks>
public sealed class FakeOneWayHasher : IOneWayHasher
{
    private const byte FormatMarker = 0x01;
    private const int SaltSize = 16;
    private const int SubkeySize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Gets or sets the PBKDF2 iteration count used by <see cref="Hash"/> and embedded in its
    /// output. Defaults to a tiny fixed value (never the real 600,000-iteration production
    /// default) so tests run fast. Mutate this between a <see cref="Hash"/> call and a
    /// <see cref="Verify"/> call to deliberately simulate a rehash-needed scenario.
    /// </summary>
    public int Iterations { get; set; } = 4;

    /// <inheritdoc />
    public string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        int iterations = Iterations;
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, Algorithm, SubkeySize);

        return Encode(iterations, salt, subkey);
    }

    /// <inheritdoc />
    public HashVerificationResult Verify(string hash, string secret)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(secret);

        if (!TryDecode(hash, out int storedIterations, out byte[]? salt, out byte[]? expectedSubkey))
        {
            return HashVerificationResult.Failed;
        }

        byte[] actualSubkey = Rfc2898DeriveBytes.Pbkdf2(secret, salt, storedIterations, Algorithm, expectedSubkey.Length);

        bool matches = CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
        if (!matches)
        {
            return HashVerificationResult.Failed;
        }

        return storedIterations == Iterations
            ? HashVerificationResult.Success
            : HashVerificationResult.SuccessRehashNeeded;
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
