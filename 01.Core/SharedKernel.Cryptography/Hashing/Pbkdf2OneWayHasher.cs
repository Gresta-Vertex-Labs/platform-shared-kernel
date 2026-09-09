using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Hashes and verifies one-way secrets using PBKDF2-HMACSHA256
/// (<see cref="Rfc2898DeriveBytes.Pbkdf2(byte[], byte[], int, HashAlgorithmName, int)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The encoded output is self-describing: it embeds a format marker, the iteration count, the
/// salt, and the derived subkey, all in a single Base64 string. This means
/// <see cref="CryptographyOptions.Pbkdf2Iterations"/> can be raised later without invalidating
/// hashes already stored — <see cref="Verify"/> reads the iteration count back out of the hash
/// itself and reports <see cref="HashVerificationResult.SuccessRehashNeeded"/> when the
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
public sealed class Pbkdf2OneWayHasher : IOneWayHasher
{
    private const byte FormatMarker = 0x01;
    private const int SaltSize = 16;
    private const int SubkeySize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// The maximum iteration count <see cref="Verify"/> will ever act on (P-512/WO-083) — a
    /// FIXED CONSTANT, deliberately independent of <see cref="CryptographyOptions.Pbkdf2Iterations"/>'s
    /// currently-configured value. A stored hash's embedded iteration count is
    /// attacker-influenceable (anyone who can write a hash row can write an absurd one), so this
    /// ceiling must never be derived from — or movable via — ordinary configuration: a future
    /// legitimate increase to the configured default must never require a simultaneous ceiling
    /// bump. 2,000,000 is generous (over 3x this package's own 600,000 shipped default) — no hash
    /// ever legitimately produced by <see cref="Hash"/> at any historically-plausible configured
    /// iteration count is ever rejected — while still bounding the CPU an attacker-supplied hash
    /// blob can force this method to spend. Checked BEFORE
    /// <see cref="Rfc2898DeriveBytes.Pbkdf2(string, byte[], int, HashAlgorithmName, int)"/> is
    /// ever called; checking afterward would defeat the purpose, since the expensive call would
    /// already have run.
    /// </summary>
    private const int MaxVerifiableIterations = 2_000_000;

    private readonly IOptionsMonitor<CryptographyOptions> _options;

    /// <summary>Creates a new <see cref="Pbkdf2OneWayHasher"/>.</summary>
    /// <param name="options">The monitored cryptography options supplying the iteration count.</param>
    public Pbkdf2OneWayHasher(IOptionsMonitor<CryptographyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): <paramref name="secret"/>'s derived <c>salt</c>
    /// and <c>subkey</c> buffers are zeroed via <see cref="CryptographicOperations.ZeroMemory"/>
    /// immediately after <see cref="Encode"/> has copied their contents into the returned string —
    /// this package fully owns both buffers' lifetime and never hands them back to the caller.
    /// </b>
    /// </remarks>
    public string Hash(string secret) => Hash(secret, captureSubkeyForTesting: null);

    /// <summary>
    /// Test-only seam (P-524/WO-083), gated via <c>InternalsVisibleTo</c> to this package's own
    /// <c>.Tests</c> project: identical to <see cref="Hash(string)"/>, except a caller-supplied
    /// callback is invoked with the freshly-derived <c>subkey</c> buffer BEFORE it is zeroed,
    /// letting a test capture the exact same array reference and assert it is genuinely all-zero
    /// bytes once this call returns. Never invoked by any production code path — the public
    /// <see cref="Hash(string)"/> overload always passes <see langword="null"/>.
    /// </summary>
    internal string Hash(string secret, Action<byte[]>? captureSubkeyForTesting)
    {
        ArgumentNullException.ThrowIfNull(secret);

        int iterations = _options.CurrentValue.Pbkdf2Iterations;
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, Algorithm, SubkeySize);
        captureSubkeyForTesting?.Invoke(subkey);

        try
        {
            return Encode(iterations, salt, subkey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(subkey);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// SECURITY (P-512/WO-083): BEFORE THE EXPENSIVE
    /// <see cref="Rfc2898DeriveBytes.Pbkdf2(string, byte[], int, HashAlgorithmName, int)"/> CALL
    /// EVER RUNS, THIS METHOD REJECTS A DECODED <paramref name="hash"/> WHOSE EMBEDDED ITERATION
    /// COUNT EXCEEDS <see cref="MaxVerifiableIterations"/>, WHOSE SUBKEY LENGTH IS NOT EXACTLY
    /// <see cref="SubkeySize"/> BYTES, OR WHOSE ITERATION COUNT IS LESS THAN 1 — ALL THREE ARE
    /// ATTACKER-INFLUENCEABLE CPU/MEMORY-EXHAUSTION OR CRASH VECTORS SINCE <paramref name="hash"/>
    /// ULTIMATELY ORIGINATES FROM STORED DATA, NOT FROM THIS PROCESS'S OWN CONFIGURATION.
    /// </b>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): <c>salt</c>, <c>expectedSubkey</c> (decoded from
    /// <paramref name="hash"/>), and <c>actualSubkey</c> (freshly derived from
    /// <paramref name="secret"/>) are all zeroed via <see cref="CryptographicOperations.ZeroMemory"/>
    /// once the <see cref="CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>
    /// comparison has run — every exit path below, not just the successful one.
    /// </b>
    /// </remarks>
    public HashVerificationResult Verify(string hash, string secret)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(secret);

        if (!TryDecode(hash, out int storedIterations, out byte[]? salt, out byte[]? expectedSubkey))
        {
            return HashVerificationResult.Failed;
        }

        byte[]? actualSubkey = null;
        try
        {
            if (storedIterations < 1 || storedIterations > MaxVerifiableIterations || expectedSubkey.Length != SubkeySize)
            {
                return HashVerificationResult.Failed;
            }

            actualSubkey = Rfc2898DeriveBytes.Pbkdf2(secret, salt, storedIterations, Algorithm, expectedSubkey.Length);

            bool matches = CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
            if (!matches)
            {
                return HashVerificationResult.Failed;
            }

            int currentIterations = _options.CurrentValue.Pbkdf2Iterations;
            return storedIterations == currentIterations
                ? HashVerificationResult.Success
                : HashVerificationResult.SuccessRehashNeeded;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expectedSubkey);
            if (actualSubkey is not null)
            {
                CryptographicOperations.ZeroMemory(actualSubkey);
            }
        }
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
