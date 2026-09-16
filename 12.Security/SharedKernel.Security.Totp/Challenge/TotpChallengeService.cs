using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Logging;

namespace SharedKernel.Security.Totp.Challenge;

/// <summary>
/// Verifies a presented TOTP code (or records an out-of-band recovery-code step-up) for a given
/// user, and records a successful outcome so the step-up claims transformation can observe it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VerifyAsync"/> composes <c>01.Core</c>'s <see cref="ITotpVerifier"/> — this type never
/// reimplements RFC 6238 validation or replay protection itself (WO-069, P-452).
/// </para>
/// <para>
/// Both members hard-reject <see cref="Guid.Empty"/> BEFORE any store or verifier call — an empty
/// identity is never a valid step-up subject. This is also the mechanism that structurally excludes
/// <c>.ApiKey</c>/<c>.Mtls</c> machine-credential identities (always <c>UserId == Guid.Empty</c>)
/// from ever triggering a store lookup, since neither this type nor
/// <c>StepUp.TotpStepUpClaimsTransformation</c> is ever invoked with such an id in the first place.
/// </para>
/// <para>
/// Neither member limits how many codes a caller may try. RFC 4226 section 7.3 requires a limit; check
/// <c>01.Core</c>'s <see cref="ITotpAttemptThrottle"/> (implemented by the consuming service) before calling
/// <see cref="VerifyAsync"/>.
/// </para>
/// </remarks>
public sealed class TotpChallengeService
{
    private readonly ITotpVerifier _totpVerifier;
    private readonly ITotpChallengeStore _challengeStore;
    private readonly ILogger<TotpChallengeService> _logger;

    /// <summary>Creates a new <see cref="TotpChallengeService"/>.</summary>
    /// <param name="totpVerifier">The RFC 6238-plus-replay-protection verifier this type delegates code validation to.</param>
    /// <param name="challengeStore">The consumer-supplied challenge-freshness store.</param>
    /// <param name="logger">The structured security-audit logger.</param>
    public TotpChallengeService(ITotpVerifier totpVerifier, ITotpChallengeStore challengeStore, ILogger<TotpChallengeService> logger)
    {
        ArgumentNullException.ThrowIfNull(totpVerifier);
        ArgumentNullException.ThrowIfNull(challengeStore);
        ArgumentNullException.ThrowIfNull(logger);
        _totpVerifier = totpVerifier;
        _challengeStore = challengeStore;
        _logger = logger;
    }

    /// <summary>
    /// Verifies <paramref name="code"/> for <paramref name="userId"/> against <paramref name="secret"/>.
    /// On success, records the successful challenge so the step-up claims transformation can observe
    /// it within its configured freshness window.
    /// </summary>
    /// <param name="userId">The user id the code is being verified for. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="secret">The user's decrypted TOTP shared secret.</param>
    /// <param name="code">The presented candidate code.</param>
    /// <param name="parameters">
    /// The parameters the secret was enrolled with (<see cref="Enrollment.TotpEnrollment.Parameters"/>), or
    /// <see langword="null"/> for <see cref="TotpParameters.Default"/>.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>
    /// <see cref="TotpVerificationResult.Valid"/> only for a fresh, valid code whose time step was not already
    /// accepted; <see cref="TotpVerificationResult.Replayed"/> for a valid code whose time step (or a later one)
    /// was already accepted for this user; otherwise <see cref="TotpVerificationResult.Invalid"/>, including for
    /// <see cref="Guid.Empty"/>. Only <see cref="TotpVerificationResult.Valid"/> records a challenge.
    /// </returns>
    public async Task<TotpVerificationResult> VerifyAsync(
        Guid userId,
        ReadOnlyMemory<byte> secret,
        string code,
        TotpParameters? parameters = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (userId == Guid.Empty)
        {
            SecurityLogEvents.TotpChallengeRejected(_logger, "EmptyIdentity");
            return TotpVerificationResult.Invalid;
        }

        string identityKey = TotpIdentityKeyFormatter.Format(userId);

        TotpVerificationResult result = await _totpVerifier
            .VerifyAsync(identityKey, secret, code, parameters, ct)
            .ConfigureAwait(false);

        switch (result)
        {
            case TotpVerificationResult.Valid:
                await _challengeStore.RecordSuccessfulChallengeAsync(identityKey, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
                break;
            case TotpVerificationResult.Replayed:
                SecurityLogEvents.TotpChallengeRejected(_logger, "ReplayedCode");
                break;
            default:
                SecurityLogEvents.TotpChallengeRejected(_logger, "InvalidCode");
                break;
        }

        return result;
    }

    /// <summary>
    /// Records a successful step-up for <paramref name="userId"/> WITHOUT a live TOTP code — for a
    /// consumer's own out-of-band recovery-code verification (e.g. <c>IOneWayHasher.Verify</c> against
    /// its own stored hashes of <c>RecoveryCodeGenerator.Normalize</c>-normalized recovery codes, entirely
    /// the consuming service's concern).
    /// </summary>
    /// <param name="userId">The user id that just completed a recovery-code step-up. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the step-up is recorded.</returns>
    /// <remarks>
    /// Feeds the SAME freshness mechanism <see cref="VerifyAsync"/> does — a caller that has already
    /// verified a recovery code its own way calls this to make that step-up observable through
    /// <c>StepUp.TotpStepUpClaimsTransformation</c>, exactly as a live TOTP code would.
    /// </remarks>
    public async Task RecordStepUpAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            SecurityLogEvents.TotpChallengeRejected(_logger, "EmptyIdentity");
            return;
        }

        string identityKey = TotpIdentityKeyFormatter.Format(userId);
        await _challengeStore.RecordSuccessfulChallengeAsync(identityKey, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
    }
}
