using System.Globalization;

namespace SharedKernel.Cryptography.Totp;

/// <summary>Builds the <c>otpauth://totp/</c> URI that authenticator apps scan from a QR code.</summary>
public static class TotpProvisioningUri
{
    /// <summary>Builds the enrollment URI.</summary>
    /// <param name="issuer">The service name shown in the app, for example <c>Contoso</c>. Must not contain a colon.</param>
    /// <param name="accountName">The account shown beside the issuer, for example an email address. Must not contain a colon.</param>
    /// <param name="secret">The shared secret. At least 16 bytes.</param>
    /// <param name="parameters">The enrollment settings, or <see langword="null"/> for <see cref="TotpParameters.Default"/>.</param>
    /// <returns><c>otpauth://totp/Issuer:account?secret=...&amp;issuer=...&amp;algorithm=...&amp;digits=...&amp;period=...</c></returns>
    /// <exception cref="ArgumentException">An argument is empty, contains a colon, or the secret is too short.</exception>
    public static Uri Build(string issuer, string accountName, ReadOnlySpan<byte> secret, TotpParameters? parameters = null)
    {
        ValidateLabelPart(issuer, nameof(issuer));
        ValidateLabelPart(accountName, nameof(accountName));

        if (secret.Length < HotpGenerator.MinimumSecretLength)
        {
            throw new ArgumentException($"The secret must be at least {HotpGenerator.MinimumSecretLength} bytes.", nameof(secret));
        }

        parameters ??= TotpParameters.Default;
        string algorithm = parameters.Algorithm switch
        {
            HotpAlgorithm.Sha1 => "SHA1",
            HotpAlgorithm.Sha256 => "SHA256",
            _ => "SHA512",
        };

        string escapedIssuer = Uri.EscapeDataString(issuer);
        return new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{escapedIssuer}:{Uri.EscapeDataString(accountName)}?secret={Base32.Encode(secret)}&issuer={escapedIssuer}&algorithm={algorithm}&digits={parameters.Digits}&period={parameters.StepSeconds}"));
    }

    private static void ValidateLabelPart(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        if (value.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("The value must not contain a colon, which separates issuer and account.", paramName);
        }
    }
}
