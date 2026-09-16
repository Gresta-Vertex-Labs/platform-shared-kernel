using System.Globalization;
using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Argon2;

/// <summary>Argon2id, written as <c>$argon2id$v=19$m=memory,t=iterations,p=parallelism$salt$hash</c>.</summary>
/// <remarks>
/// <para>
/// New hashes use a 16-byte salt, a 32-byte output and the costs in <see cref="Argon2Options"/>.
/// </para>
/// <para>
/// Verification requires version 19 and rejects, before any work, a memory cost above
/// <see cref="Argon2Options.MaximumMemorySizeKb"/>, more than <see cref="Argon2Options.MaximumIterations"/> iterations
/// or <see cref="Argon2Options.MaximumDegreeOfParallelism"/> lanes, a salt outside 8 to 64 bytes and an output
/// outside 16 to 64 bytes. A stored hash can be written by an attacker, and its costs decide how much memory and CPU
/// verification uses.
/// </para>
/// </remarks>
public sealed class Argon2idOneWayHashAlgorithm : IOneWayHashAlgorithm
{
    /// <summary>The PHC algorithm identifier: <c>argon2id</c>.</summary>
    public const string Id = "argon2id";

    private const int Version = 19;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly IOptionsMonitor<Argon2Options> _options;

    /// <summary>Creates the algorithm.</summary>
    /// <param name="options">Supplies the costs for new hashes.</param>
    public Argon2idOneWayHashAlgorithm(IOptionsMonitor<Argon2Options> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string AlgorithmId => Id;

    /// <inheritdoc />
    public PhcHashString Hash(ReadOnlySpan<byte> secret)
    {
        Argon2Options costs = _options.CurrentValue;
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Derive(secret, salt, costs.MemorySizeKb, costs.Iterations, costs.DegreeOfParallelism, HashSize);

        try
        {
            return new PhcHashString(
                Id,
                Version,
                [
                    new("m", costs.MemorySizeKb.ToString(CultureInfo.InvariantCulture)),
                    new("t", costs.Iterations.ToString(CultureInfo.InvariantCulture)),
                    new("p", costs.DegreeOfParallelism.ToString(CultureInfo.InvariantCulture)),
                ],
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

        if (!TryReadCosts(hash, out int memory, out int iterations, out int parallelism)
            || hash.Salt.Length is < 8 or > 64
            || hash.Hash.Length is < 16 or > 64)
        {
            return false;
        }

        byte[] actual = Derive(secret, hash.Salt.ToArray(), memory, iterations, parallelism, hash.Hash.Length);
        try
        {
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

        Argon2Options costs = _options.CurrentValue;
        return !TryReadCosts(hash, out int memory, out int iterations, out int parallelism)
            || memory != costs.MemorySizeKb
            || iterations != costs.Iterations
            || parallelism != costs.DegreeOfParallelism
            || hash.Salt.Length != SaltSize
            || hash.Hash.Length != HashSize;
    }

    private static bool TryReadCosts(PhcHashString hash, out int memory, out int iterations, out int parallelism)
    {
        memory = 0;
        iterations = 0;
        parallelism = 0;

        return string.Equals(hash.AlgorithmId, Id, StringComparison.Ordinal)
            && hash.Version == Version
            && hash.Parameters.Count == 3
            && hash.TryGetInt32Parameter("m", out memory)
            && hash.TryGetInt32Parameter("t", out iterations)
            && hash.TryGetInt32Parameter("p", out parallelism)
            && parallelism is >= 1 and <= Argon2Options.MaximumDegreeOfParallelism
            && iterations is >= 1 and <= Argon2Options.MaximumIterations
            && memory >= 8 * parallelism
            && memory <= Argon2Options.MaximumMemorySizeKb;
    }

    private static byte[] Derive(ReadOnlySpan<byte> secret, byte[] salt, int memory, int iterations, int parallelism, int length)
    {
        byte[] password = secret.ToArray();
        try
        {
            using var argon2 = new Argon2id(password)
            {
                Salt = salt,
                MemorySize = memory,
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
            };

            return argon2.GetBytes(length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }
}
