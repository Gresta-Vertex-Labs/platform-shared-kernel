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
/// <see cref="ITotpReplayGuard.MarkUsedAsync"/> only after a fresh, valid code — never for a code
/// that failed generator validation, which would let an attacker burn a legitimate code by
/// submitting garbage.
/// </remarks>
public sealed class TotpVerifier
{
    // Matches the ITotpGenerator default (digits=6, stepSeconds=30, driftWindow=1) used by the
    // single-parameter-set VerifyAsync signature below (D-57's exact contract takes no
    // digits/stepSeconds/driftWindow parameters of its own).
    private const int DefaultStepSeconds = 30;
    private const int DefaultDriftWindow = 1;

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
    /// step (within the default ±1-step drift window) OR when it has already been accepted once
    /// before. Only a fresh, valid code is marked used and returns <see langword="true"/>.
    /// </summary>
    /// <param name="identityKey">The identity the code was presented for (e.g. a user id or enrollment id).</param>
    /// <param name="secret">The shared secret key.</param>
    /// <param name="code">The candidate code.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> only for a fresh, valid, not-previously-used code.</returns>
    public async ValueTask<bool> VerifyAsync(string identityKey, byte[] secret, string code, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(code);

        if (!_totpGenerator.ValidateCode(secret, code))
        {
            return false;
        }

        if (await _replayGuard.HasBeenUsedAsync(identityKey, code, ct).ConfigureAwait(false))
        {
            return false;
        }

        TimeSpan validityWindow = TimeSpan.FromSeconds(DefaultStepSeconds * ((2 * DefaultDriftWindow) + 1));
        await _replayGuard.MarkUsedAsync(identityKey, code, validityWindow, ct).ConfigureAwait(false);

        return true;
    }
}
