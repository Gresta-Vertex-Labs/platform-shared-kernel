using SharedKernel.Cryptography.Random;

namespace SharedKernel.Cryptography.Totp;

/// <summary>Generates TOTP and HOTP shared secrets.</summary>
/// <remarks>
/// Store the secret encrypted, for example with <c>ISymmetricEncryptionService</c>, never in plain text: anyone who
/// reads it can generate valid codes. Show it to the user once, as a QR code of <see cref="TotpProvisioningUri"/>.
/// </remarks>
public static class TotpSecret
{
    /// <summary>The recommended secret length: 20 bytes (160 bits), as RFC 4226 recommends for HMAC-SHA1.</summary>
    public const int DefaultLength = 20;

    /// <summary>Generates a random secret.</summary>
    /// <param name="random">The random source.</param>
    /// <param name="length">The secret length, from 16 to 64 bytes. Defaults to <see cref="DefaultLength"/>.</param>
    /// <returns>The secret.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is outside 16 to 64.</exception>
    public static byte[] Generate(ISecureRandomGenerator random, int length = DefaultLength)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, HotpGenerator.MinimumSecretLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 64);
        return random.GetBytes(length);
    }
}
