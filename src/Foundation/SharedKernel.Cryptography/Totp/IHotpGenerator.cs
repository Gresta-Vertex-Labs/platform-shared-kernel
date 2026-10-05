namespace SharedKernel.Cryptography.Totp;

/// <summary>Generates and validates RFC 4226 HOTP codes for an explicit counter.</summary>
/// <remarks>Stateless. <see cref="ITotpGenerator"/> derives the counter from the time.</remarks>
public interface IHotpGenerator
{
    /// <summary>Generates the code for <paramref name="counter"/>.</summary>
    /// <param name="secret">The shared secret. At least 16 bytes.</param>
    /// <param name="counter">The counter. Must not be negative.</param>
    /// <param name="digits">The number of digits, 6 to 8.</param>
    /// <param name="algorithm">The HMAC algorithm.</param>
    /// <returns>The zero-padded code.</returns>
    /// <exception cref="ArgumentException">The secret is too short, or an argument is out of range.</exception>
    string GenerateCode(ReadOnlySpan<byte> secret, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);

    /// <summary>Validates a code for <paramref name="counter"/> in fixed time.</summary>
    /// <param name="secret">The shared secret. At least 16 bytes.</param>
    /// <param name="code">The code. Spaces and hyphens are ignored; any other non-digit fails.</param>
    /// <param name="counter">The counter. Must not be negative.</param>
    /// <param name="digits">The number of digits, 6 to 8.</param>
    /// <param name="algorithm">The HMAC algorithm.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    /// <exception cref="ArgumentException">The secret is too short, or an argument is out of range.</exception>
    bool ValidateCode(ReadOnlySpan<byte> secret, string code, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);
}
