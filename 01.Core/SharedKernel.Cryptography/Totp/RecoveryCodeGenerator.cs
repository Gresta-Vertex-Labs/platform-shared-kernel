using SharedKernel.Cryptography.Random;

namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Generates one-time TOTP/HOTP backup ("recovery") codes — for when the user has lost access to
/// their authenticator device.
/// </summary>
/// <remarks>
/// This type only ever generates plaintext codes, shown once to the user at enrollment time. It
/// never persists or hashes them — hashing the codes at rest via the existing
/// <see cref="Hashing.IOneWayHasher"/> before storage (exactly like any other secret: a recovery
/// code IS a secret) is the consuming service's own responsibility.
/// </remarks>
public sealed class RecoveryCodeGenerator
{
    private readonly ISecureRandomGenerator _randomGenerator;

    /// <summary>Creates a new <see cref="RecoveryCodeGenerator"/>.</summary>
    /// <param name="randomGenerator">The source of cryptographically secure randomness backing every generated code.</param>
    public RecoveryCodeGenerator(ISecureRandomGenerator randomGenerator)
    {
        ArgumentNullException.ThrowIfNull(randomGenerator);
        _randomGenerator = randomGenerator;
    }

    /// <summary>
    /// Generates <paramref name="count"/> independent recovery codes, each rendered as unpadded
    /// Base32 text over <paramref name="lengthBytes"/> cryptographically secure random bytes.
    /// </summary>
    /// <param name="count">The number of codes to generate. Defaults to 10.</param>
    /// <param name="lengthBytes">The number of underlying random bytes per code, before Base32 encoding. Defaults to 5 (an 8-character Base32 code).</param>
    /// <returns>A list of <paramref name="count"/> plaintext recovery codes.</returns>
    public IReadOnlyList<string> GenerateCodes(int count = 10, int lengthBytes = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lengthBytes);

        var codes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            byte[] bytes = _randomGenerator.NextBytes(lengthBytes);
            codes.Add(Base32.Encode(bytes));
        }

        return codes;
    }
}
