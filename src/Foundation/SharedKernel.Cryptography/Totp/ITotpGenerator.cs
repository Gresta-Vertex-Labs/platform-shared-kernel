namespace SharedKernel.Cryptography.Totp;

/// <summary>Generates and validates RFC 6238 TOTP codes.</summary>
/// <remarks>
/// Stateless: validation does not stop a code being used twice. Use <see cref="ITotpVerifier"/> to accept codes from
/// users, which adds replay protection.
/// </remarks>
public interface ITotpGenerator
{
    /// <summary>Generates the code for the current time.</summary>
    /// <param name="secret">The shared secret. At least 16 bytes.</param>
    /// <param name="parameters">The enrollment settings, or <see langword="null"/> for <see cref="TotpParameters.Default"/>.</param>
    /// <returns>The code.</returns>
    string GenerateCode(ReadOnlySpan<byte> secret, TotpParameters? parameters = null);

    /// <summary>Generates the code for <paramref name="timestamp"/>.</summary>
    /// <param name="secret">The shared secret. At least 16 bytes.</param>
    /// <param name="timestamp">The time to generate for.</param>
    /// <param name="parameters">The enrollment settings, or <see langword="null"/> for <see cref="TotpParameters.Default"/>.</param>
    /// <returns>The code.</returns>
    string GenerateCode(ReadOnlySpan<byte> secret, DateTimeOffset timestamp, TotpParameters? parameters = null);

    /// <summary>
    /// Validates a code against the current time step and the drift steps around it, checking every step in fixed
    /// time.
    /// </summary>
    /// <param name="secret">The shared secret. At least 16 bytes.</param>
    /// <param name="code">The code. Spaces and hyphens are ignored.</param>
    /// <param name="timeStep">The time step the code matched, when valid.</param>
    /// <param name="parameters">The enrollment settings, or <see langword="null"/> for <see cref="TotpParameters.Default"/>.</param>
    /// <returns><see langword="true"/> when the code matches a step in the window.</returns>
    bool TryValidateCode(ReadOnlySpan<byte> secret, string code, out long timeStep, TotpParameters? parameters = null);

    /// <summary>Validates a code as of <paramref name="timestamp"/>.</summary>
    /// <param name="secret">The shared secret. At least 16 bytes.</param>
    /// <param name="code">The code. Spaces and hyphens are ignored.</param>
    /// <param name="timestamp">The time to validate at.</param>
    /// <param name="timeStep">The time step the code matched, when valid.</param>
    /// <param name="parameters">The enrollment settings, or <see langword="null"/> for <see cref="TotpParameters.Default"/>.</param>
    /// <returns><see langword="true"/> when the code matches a step in the window.</returns>
    bool TryValidateCode(
        ReadOnlySpan<byte> secret,
        string code,
        DateTimeOffset timestamp,
        out long timeStep,
        TotpParameters? parameters = null);
}
