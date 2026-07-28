using System.Security.Cryptography;
using SharedKernel.Cryptography.Random;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="ISecureRandomGenerator"/>.
/// </summary>
/// <remarks>
/// <para>
/// When constructed with <c>seed: null</c> (the default), both members delegate to the real
/// <see cref="RandomNumberGenerator"/> — byte-for-byte identical behavior to production's
/// <see cref="CryptoRandomGenerator"/>, so two calls never return equal output. This deliberately
/// preserves enough non-determinism to catch a hardcoded-token bug that a fully-deterministic fake
/// would silently hide.
/// </para>
/// <para>
/// When a seed is supplied, output is backed by a seeded <see cref="System.Random"/> instead — fully
/// reproducible across runs for the SAME seed.
/// </para>
/// <para>
/// <b>SEEDED MODE IS NOT CRYPTOGRAPHICALLY SECURE.</b> It must never be used outside deterministic
/// test assertions (e.g. snapshot-testing a generated token value).
/// </para>
/// </remarks>
public sealed class FakeSecureRandomGenerator : ISecureRandomGenerator
{
    private readonly System.Random? _seededRandom;

    /// <summary>
    /// Initialises a new <see cref="FakeSecureRandomGenerator"/>.
    /// </summary>
    /// <param name="seed">
    /// When <see langword="null"/> (the default), output is backed by the real
    /// <see cref="RandomNumberGenerator"/>. When supplied, output is backed by a seeded
    /// <see cref="System.Random"/> instead — deterministic, but NOT cryptographically secure.
    /// </param>
    public FakeSecureRandomGenerator(int? seed = null)
    {
        _seededRandom = seed.HasValue ? new System.Random(seed.Value) : null;
    }

    /// <inheritdoc />
    public byte[] NextBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        if (_seededRandom is null)
        {
            return RandomNumberGenerator.GetBytes(length);
        }

        var bytes = new byte[length];
        _seededRandom.NextBytes(bytes);
        return bytes;
    }

    /// <inheritdoc />
    public string NextToken(int length = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        byte[] bytes = NextBytes(length);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
