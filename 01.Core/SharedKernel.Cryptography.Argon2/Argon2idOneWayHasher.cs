using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Argon2.Options;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;

namespace SharedKernel.Cryptography.Argon2;

/// <summary>
/// Hashes and verifies one-way secrets using Argon2id (RFC 9106) via the
/// <c>Konscious.Security.Cryptography.Argon2</c> package — a pure-managed .NET implementation
/// with no native/P-Invoke dependency.
/// </summary>
/// <remarks>
/// <para>
/// The encoded output is the real, interoperable <b>PHC string format</b> —
/// <c>$argon2id$v=19$m=&lt;memoryKb&gt;,t=&lt;iterations&gt;,p=&lt;parallelism&gt;$&lt;salt&gt;$&lt;subkey&gt;</c>
/// (salt and subkey are unpadded, standard-alphabet Base64) — the industry-standard Argon2
/// hash-storage shape, chosen deliberately over inventing a bespoke encoding the way
/// <see cref="Pbkdf2OneWayHasher"/> does, because PBKDF2 has no equivalently universal standard
/// string format and Argon2 does. This means <see cref="Argon2CryptographyOptions"/> can be
/// raised later without invalidating hashes already stored — <see cref="Verify"/> reads the
/// parameters back out of the hash itself and reports
/// <see cref="HashVerificationResult.SuccessRehashNeeded"/> when the stored parameters no longer
/// match the currently configured values.
/// </para>
/// <para>
/// Registered as a <b>keyed</b> singleton ("Argon2id") by
/// <see cref="Extensions.Argon2CryptographyServiceCollectionExtensions.AddSharedKernelArgon2Cryptography"/>
/// — never as the unkeyed <see cref="IOneWayHasher"/> default. <see cref="Pbkdf2OneWayHasher"/>
/// remains the unkeyed default because PBKDF2, unlike Argon2id, is FIPS 140-3 approved; choose
/// this hasher instead everywhere FIPS-mode compliance is not a hard constraint.
/// </para>
/// </remarks>
public sealed class Argon2idOneWayHasher : IOneWayHasher
{
    private const string AlgorithmSegment = "argon2id";
    private const string VersionSegment = "v=19"; // Argon2 reference version 0x13 (19 decimal) — the only version this package targets.
    private const int SaltSize = 16;
    private const int SubkeySize = 32;

    private readonly IOptionsMonitor<Argon2CryptographyOptions> _options;
    private readonly ISecureRandomGenerator _random;

    /// <summary>Creates a new <see cref="Argon2idOneWayHasher"/>.</summary>
    /// <param name="options">The monitored Argon2id cost parameters.</param>
    /// <param name="random">
    /// The secure random source used to generate a fresh salt for every call to
    /// <see cref="Hash"/>. Never <see cref="System.Random"/> or <see cref="Guid.NewGuid"/>.
    /// </param>
    public Argon2idOneWayHasher(IOptionsMonitor<Argon2CryptographyOptions> options, ISecureRandomGenerator random)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(random);

        _options = options;
        _random = random;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>
    /// KEY-MATERIAL ZEROIZATION (P-524/WO-083): the <c>salt</c> and <c>subkey</c> buffers are
    /// zeroed via <see cref="CryptographicOperations.ZeroMemory"/> immediately after
    /// <see cref="Encode"/> has copied their contents into the returned string; the intermediate
    /// UTF-8 <c>secretBytes</c> buffer computed inside <see cref="ComputeSubkey"/> is zeroed there
    /// once Argon2id has finished using it — this package fully owns all three buffers' lifetime
    /// and never hands any of them back to the caller.
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

        Argon2CryptographyOptions current = _options.CurrentValue;
        byte[] salt = _random.NextBytes(SaltSize);
        byte[] subkey = ComputeSubkey(
            secret, salt, current.MemorySizeKb, current.Iterations, current.DegreeOfParallelism, SubkeySize);
        captureSubkeyForTesting?.Invoke(subkey);

        try
        {
            return Encode(current.MemorySizeKb, current.Iterations, current.DegreeOfParallelism, salt, subkey);
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

        if (!TryParse(hash, out int memoryKb, out int iterations, out int parallelism, out byte[]? salt, out byte[]? expectedSubkey))
        {
            return HashVerificationResult.Failed;
        }

        byte[]? actualSubkey = null;
        try
        {
            try
            {
                actualSubkey = ComputeSubkey(secret, salt, memoryKb, iterations, parallelism, expectedSubkey.Length);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // The PHC string parsed structurally, but the embedded cost parameters are not a
                // computable Argon2id configuration (Konscious.Security.Cryptography.Argon2id's own
                // ValidateParameters rejects, e.g., a memory size under its 4 KiB floor via
                // InvalidOperationException, or an unreasonable output length via
                // NotSupportedException). Treat exactly like any other malformed hash: report
                // failure, never let the exception escape this method.
                return HashVerificationResult.Failed;
            }

            bool matches = CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
            if (!matches)
            {
                return HashVerificationResult.Failed;
            }

            Argon2CryptographyOptions currentOptions = _options.CurrentValue;
            bool matchesCurrentParams =
                memoryKb == currentOptions.MemorySizeKb &&
                iterations == currentOptions.Iterations &&
                parallelism == currentOptions.DegreeOfParallelism;

            return matchesCurrentParams ? HashVerificationResult.Success : HashVerificationResult.SuccessRehashNeeded;
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

    /// <summary>
    /// Computes the Argon2id subkey. The intermediate UTF-8 <c>secretBytes</c> buffer is zeroed
    /// via <see cref="CryptographicOperations.ZeroMemory"/> once Argon2id has finished reading it
    /// (P-524/WO-083) — this method fully owns that buffer's lifetime and never hands it back to
    /// either caller (<see cref="Hash(string, Action{byte[]}?)"/> or <see cref="Verify"/>).
    /// </summary>
    private static byte[] ComputeSubkey(string secret, byte[] salt, int memoryKb, int iterations, int parallelism, int subkeyLength)
    {
        byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            using var argon2Id = new Argon2id(secretBytes)
            {
                Salt = salt,
                DegreeOfParallelism = parallelism,
                MemorySize = memoryKb,
                Iterations = iterations,
            };

            return argon2Id.GetBytes(subkeyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    private static string Encode(int memoryKb, int iterations, int parallelism, byte[] salt, byte[] subkey) =>
        FormattableString.Invariant(
            $"${AlgorithmSegment}${VersionSegment}$m={memoryKb},t={iterations},p={parallelism}${ToUnpaddedBase64(salt)}${ToUnpaddedBase64(subkey)}");

    private static bool TryParse(
        string encoded,
        out int memoryKb,
        out int iterations,
        out int parallelism,
        [NotNullWhen(true)] out byte[]? salt,
        [NotNullWhen(true)] out byte[]? subkey)
    {
        memoryKb = 0;
        iterations = 0;
        parallelism = 0;
        salt = null;
        subkey = null;

        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        string[] parts = encoded.Split('$');
        // "$argon2id$v=19$m=...,t=...,p=...$salt$subkey" splits into 6 parts, the first empty
        // (everything before the leading '$').
        if (parts.Length != 6 || parts[0].Length != 0)
        {
            return false;
        }

        if (!string.Equals(parts[1], AlgorithmSegment, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(parts[2], VersionSegment, StringComparison.Ordinal))
        {
            return false;
        }

        string[] paramParts = parts[3].Split(',');
        if (paramParts.Length != 3 ||
            !TryParseParam(paramParts[0], "m=", out memoryKb) ||
            !TryParseParam(paramParts[1], "t=", out iterations) ||
            !TryParseParam(paramParts[2], "p=", out parallelism) ||
            memoryKb <= 0 || iterations <= 0 || parallelism <= 0)
        {
            return false;
        }

        try
        {
            salt = FromUnpaddedBase64(parts[4]);
            subkey = FromUnpaddedBase64(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && subkey.Length > 0;
    }

    private static bool TryParseParam(string part, string prefix, out int value)
    {
        value = 0;
        return part.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(part.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string ToUnpaddedBase64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=');

    private static byte[] FromUnpaddedBase64(string text)
    {
        int padding = (4 - (text.Length % 4)) % 4;
        return Convert.FromBase64String(text + new string('=', padding));
    }
}
