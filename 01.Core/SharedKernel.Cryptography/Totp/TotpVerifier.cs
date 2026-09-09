namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Composes <see cref="ITotpGenerator.ValidateCode(byte[], string, int, int, int, HotpAlgorithm)"/>
/// with an <see cref="ITotpReplayGuard"/> so a valid TOTP code cannot be accepted twice inside its
/// validity window.
/// </summary>
/// <remarks>
/// <see cref="ITotpGenerator"/> itself stays pure/stateless — this type is where replay
/// protection is actually enforced. <see cref="VerifyAsync"/> returns <see langword="false"/> on
/// an invalid code OR a previously-used code, and calls
/// <see cref="ITotpReplayGuard.TryMarkUsedAsync"/> only after a fresh, valid code — never for a
/// code that failed generator validation, which would let an attacker burn a legitimate code by
/// submitting garbage.
/// </remarks>
public sealed class TotpVerifier
{
    private readonly ITotpGenerator _totpGenerator;
    private readonly ITotpReplayGuard _replayGuard;

    /// <summary>Creates a new <see cref="TotpVerifier"/>.</summary>
    /// <param name="totpGenerator">The pure RFC 6238 generator this type validates codes against.</param>
    /// <param name="replayGuard">The consumer-supplied replay-tracking store.</param>
    public TotpVerifier(ITotpGenerator totpGenerator, ITotpReplayGuard replayGuard)
    {
        ArgumentNullException.ThrowIfNull(totpGenerator);
        ArgumentNullException.ThrowIfNull(replayGuard);
        _totpGenerator = totpGenerator;
        _replayGuard = replayGuard;
    }

    /// <summary>
    /// Verifies <paramref name="code"/> for <paramref name="identityKey"/>: returns
    /// <see langword="false"/> when the code is cryptographically invalid for the current time
    /// step (within the <paramref name="driftWindow"/>-step drift window) OR when it has already
    /// been accepted once before. Only a fresh, valid code is marked used and returns
    /// <see langword="true"/>.
    /// </summary>
    /// <param name="identityKey">The identity the code was presented for (e.g. a user id or enrollment id).</param>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="code">The candidate code.</param>
    /// <param name="digits">The expected number of decimal digits. Defaults to 6.</param>
    /// <param name="stepSeconds">The time-step size in seconds. Defaults to 30.</param>
    /// <param name="driftWindow">The number of time steps before/after "now" to also accept. Defaults to 1 (±30s at the default step size).</param>
    /// <param name="algorithm">The HMAC algorithm to use. Defaults to <see cref="HotpAlgorithm.Sha1"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> only for a fresh, valid, not-previously-used code.</returns>
    /// <remarks>
    /// <b>ATOMIC REPLAY MARKING (P-514/WO-083, BREAKING):</b> this method now calls
    /// <see cref="ITotpReplayGuard.TryMarkUsedAsync"/> directly after a successful
    /// <see cref="ITotpGenerator.ValidateCode(byte[], string, int, int, int, HotpAlgorithm)"/> —
    /// there is no longer a separate "check" step and "mark" step for two concurrent callers to
    /// interleave between, closing a TOCTOU where two concurrent submissions of the same valid
    /// code could both be accepted.
    /// <para>
    /// <b>CONFIG-CONSISTENT REPLAY WINDOW:</b> the replay window passed to
    /// <see cref="ITotpReplayGuard.TryMarkUsedAsync"/> is always computed from the SAME
    /// <paramref name="stepSeconds"/>/<paramref name="driftWindow"/> values actually passed to
    /// <see cref="ITotpGenerator.ValidateCode(byte[], string, int, int, int, HotpAlgorithm)"/> for
    /// this call — never a hardcoded assumption independent of what was actually validated. A
    /// caller validating against a non-default step/drift configuration gets a replay window that
    /// genuinely matches what it validated against, instead of the platform default.
    /// </para>
    /// </remarks>
    public async ValueTask<bool> VerifyAsync(
        string identityKey,
        byte[] secret,
        string code,
        int digits = 6,
        int stepSeconds = 30,
        int driftWindow = 1,
        HotpAlgorithm algorithm = HotpAlgorithm.Sha1,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(code);

        if (!_totpGenerator.ValidateCode(secret, code, digits, stepSeconds, driftWindow, algorithm))
        {
            return false;
        }

        TimeSpan validityWindow = TimeSpan.FromSeconds(stepSeconds * ((2 * driftWindow) + 1));

        return await _replayGuard.TryMarkUsedAsync(identityKey, code, validityWindow, ct).ConfigureAwait(false);
    }
}
