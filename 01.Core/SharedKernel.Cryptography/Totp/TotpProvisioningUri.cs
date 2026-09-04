using System.Globalization;

namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Builds a <c>otpauth://totp/...</c> Key Uri Format URI — the enrollment payload authenticator
/// apps (Google Authenticator, Microsoft Authenticator, 1Password, etc.) scan from a QR code or
/// accept as a manual-entry link.
/// </summary>
public static class TotpProvisioningUri
{
    /// <summary>
    /// Builds the provisioning URI for a new TOTP enrollment.
    /// </summary>
    /// <param name="issuer">The service/organization name shown in the authenticator app (e.g. "Contoso").</param>
    /// <param name="accountName">The account identifier shown alongside the issuer (e.g. the user's email address).</param>
    /// <param name="secret">The raw shared-secret bytes — encoded as Base32 in the resulting URI via <see cref="Base32.Encode"/>.</param>
    /// <param name="digits">The number of decimal digits the resulting code has. Defaults to 6.</param>
    /// <param name="stepSeconds">The time-step size in seconds. Defaults to 30.</param>
    /// <param name="algorithm">The HMAC algorithm the enrollment uses. Defaults to <see cref="HotpAlgorithm.Sha1"/> — the only value every authenticator app is guaranteed to support.</param>
    /// <returns>
    /// A URI of the shape
    /// <c>otpauth://totp/{Issuer}:{AccountName}?secret=...&amp;issuer=...&amp;digits=...&amp;period=...&amp;algorithm=...</c>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="issuer"/> or <paramref name="accountName"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="secret"/> is <see langword="null"/>.</exception>
    public static Uri Build(
        string issuer,
        string accountName,
        byte[] secret,
        int digits = 6,
        int stepSeconds = 30,
        HotpAlgorithm algorithm = HotpAlgorithm.Sha1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentNullException.ThrowIfNull(secret);

        string encodedSecret = Base32.Encode(secret);
        string label = $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}";
        string algorithmName = AlgorithmName(algorithm);

        string query = string.Create(
            CultureInfo.InvariantCulture,
            $"secret={encodedSecret}&issuer={Uri.EscapeDataString(issuer)}&digits={digits}&period={stepSeconds}&algorithm={algorithmName}");

        return new Uri($"otpauth://totp/{label}?{query}");
    }

    private static string AlgorithmName(HotpAlgorithm algorithm) =>
        algorithm switch
        {
            HotpAlgorithm.Sha1 => "SHA1",
            HotpAlgorithm.Sha256 => "SHA256",
            HotpAlgorithm.Sha512 => "SHA512",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported HOTP algorithm."),
        };
}
