namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Generates and validates RFC 6238 TOTP (Time-based One-Time Password) codes — an
/// <see cref="IHotpGenerator"/> composed with a time-derived moving-factor counter.
/// </summary>
/// <remarks>
/// This type stays pure/stateless — no replay awareness. <see cref="TotpVerifier"/> composes
/// <see cref="ValidateCode(byte[], string, int, int, int, HotpAlgorithm)"/> with an
/// <see cref="Totp.ITotpReplayGuard"/> one layer up to prevent a valid code being accepted twice.
/// </remarks>
public interface ITotpGenerator
{
    /// <summary>
    /// Generates the TOTP code for the current time, sourced from the injected
    /// <c>SharedKernel.Primitives.Clocks.IClock</c> — never <see cref="DateTime.UtcNow"/>.
    /// </summary>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="digits">The number of decimal digits in the returned code. Defaults to 6.</param>
    /// <param name="stepSeconds">The time-step size in seconds. Defaults to 30 per the RFC's typical usage.</param>
    /// <param name="algorithm">The HMAC algorithm to use. Defaults to <see cref="HotpAlgorithm.Sha1"/>.</param>
    /// <returns>The zero-padded decimal TOTP code for the current time step.</returns>
    string GenerateCode(byte[] secret, int digits = 6, int stepSeconds = 30, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);

    /// <summary>
    /// Generates the TOTP code for an explicit <paramref name="timestamp"/> — the testable
    /// overload; bypasses the injected clock entirely.
    /// </summary>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="timestamp">The instant to compute the time-step counter from.</param>
    /// <param name="digits">The number of decimal digits in the returned code. Defaults to 6.</param>
    /// <param name="stepSeconds">The time-step size in seconds. Defaults to 30.</param>
    /// <param name="algorithm">The HMAC algorithm to use. Defaults to <see cref="HotpAlgorithm.Sha1"/>.</param>
    /// <returns>The zero-padded decimal TOTP code for <paramref name="timestamp"/>'s time step.</returns>
    string GenerateCode(byte[] secret, DateTimeOffset timestamp, int digits = 6, int stepSeconds = 30, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);

    /// <summary>
    /// Validates <paramref name="code"/> against the current time, accepting a code generated up
    /// to <paramref name="driftWindow"/> steps before or after "now" (sourced from the injected
    /// clock) to tolerate clock skew between the server and the authenticator device.
    /// </summary>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="code">The candidate code to validate.</param>
    /// <param name="digits">The expected number of decimal digits. Defaults to 6.</param>
    /// <param name="stepSeconds">The time-step size in seconds. Defaults to 30.</param>
    /// <param name="driftWindow">The number of time steps before/after "now" to also accept. Defaults to 1 (±30s at the default step size).</param>
    /// <param name="algorithm">The HMAC algorithm to use. Defaults to <see cref="HotpAlgorithm.Sha1"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="code"/> matches any step inside the drift window.</returns>
    bool ValidateCode(byte[] secret, string code, int digits = 6, int stepSeconds = 30, int driftWindow = 1, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);

    /// <summary>
    /// Validates <paramref name="code"/> against an explicit <paramref name="timestamp"/> — the
    /// testable overload; bypasses the injected clock entirely.
    /// </summary>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="code">The candidate code to validate.</param>
    /// <param name="timestamp">The instant "now" represents for drift-window computation.</param>
    /// <param name="digits">The expected number of decimal digits. Defaults to 6.</param>
    /// <param name="stepSeconds">The time-step size in seconds. Defaults to 30.</param>
    /// <param name="driftWindow">The number of time steps before/after <paramref name="timestamp"/> to also accept. Defaults to 1.</param>
    /// <param name="algorithm">The HMAC algorithm to use. Defaults to <see cref="HotpAlgorithm.Sha1"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="code"/> matches any step inside the drift window.</returns>
    bool ValidateCode(byte[] secret, string code, DateTimeOffset timestamp, int digits = 6, int stepSeconds = 30, int driftWindow = 1, HotpAlgorithm algorithm = HotpAlgorithm.Sha1);
}
