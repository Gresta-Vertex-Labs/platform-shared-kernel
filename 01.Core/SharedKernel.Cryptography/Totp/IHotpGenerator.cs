namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Generates and validates RFC 4226 HOTP (HMAC-based One-Time Password) codes from a shared
/// secret and an explicit moving-factor counter.
/// </summary>
/// <remarks>
/// This is a pure, stateless, side-effect-free RFC implementation — it has no notion of "now" and
/// no replay awareness. <see cref="Totp.ITotpGenerator"/> composes this internally to add a
/// time-derived counter; <see cref="TotpVerifier"/> composes replay protection one layer above
/// that. Nothing in this type persists state or performs I/O.
/// </remarks>
public interface IHotpGenerator
{
    /// <summary>
    /// Generates the HOTP code for <paramref name="counter"/> per RFC 4226 §5.3 (dynamic
    /// truncation).
    /// </summary>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="counter">The moving-factor counter value.</param>
    /// <param name="digits">The number of decimal digits in the returned code. Defaults to 6.</param>
    /// <param name="algorithm">The HMAC algorithm to use. Defaults to <see cref="HotpAlgorithm.Sha1"/> per the RFC.</param>
    /// <returns>The zero-padded decimal HOTP code, exactly <paramref name="digits"/> characters long.</returns>
    string GenerateCode(byte[] secret, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);

    /// <summary>
    /// Validates that <paramref name="code"/> is the correct HOTP code for <paramref name="counter"/>,
    /// using a fixed-time comparison to avoid leaking match progress through timing.
    /// </summary>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="code">The candidate code to validate.</param>
    /// <param name="counter">The moving-factor counter value to validate against.</param>
    /// <param name="digits">The expected number of decimal digits. Defaults to 6.</param>
    /// <param name="algorithm">The HMAC algorithm to use. Defaults to <see cref="HotpAlgorithm.Sha1"/> per the RFC.</param>
    /// <returns><see langword="true"/> when <paramref name="code"/> matches the code computed for <paramref name="counter"/>.</returns>
    bool ValidateCode(byte[] secret, string code, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);
}
