using SharedKernel.Cryptography.Hashing;
using SharedKernel.Security.Totp.Challenge;

namespace SharedKernel.Security.Totp.Samples;

/// <summary>
/// A worked, NON-PRODUCTION example wiring <see cref="TotpChallengeService"/> to a consuming
/// service's own decrypted-secret lookup, with a recovery-code fallback path.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE IS A DOCUMENTATION RECIPE, NOT A SHIPPED PRODUCTION IMPLEMENTATION.</b> It exists to
/// back this package's README challenge recipe with real, compiled, exercised code rather than
/// hand-typed prose — mirrors <c>SharedKernel.Security.ApiKey</c>'s
/// <c>Samples/RotationWindowApiKeyValidatorSample.cs</c> non-production-sample precedent exactly
/// (WO-060, C-46; WO-069, P-452/DOC-23).
/// </para>
/// <para>
/// <b>The pattern this sample demonstrates:</b> <see cref="TotpChallengeService"/> never resolves a
/// user's decrypted secret or recovery-code hashes itself — this package has zero persistence
/// coupling by design. A real integration point (here, <see cref="_enrollmentLookup"/>) supplies the
/// decrypted secret; a primary TOTP code goes through <see cref="TotpChallengeService.VerifyAsync"/>,
/// while a recovery code is verified entirely by the consumer's OWN <see cref="IOneWayHasher.Verify"/>
/// check against its own stored recovery-code hashes, then reported to the SAME freshness mechanism
/// via <see cref="TotpChallengeService.RecordStepUpAsync"/>.
/// </para>
/// </remarks>
internal sealed class TotpChallengeRecipeSample
{
    // Stand-in for a real lookup (database, secret store, ...) returning the user's decrypted TOTP
    // secret and the hashes of their still-unused recovery codes. A real implementation decrypts the
    // secret via 01.Core/SharedKernel.Cryptography's ISymmetricEncryptionService immediately before
    // use — never store or pass around a decrypted secret longer than necessary.
    private readonly IReadOnlyDictionary<Guid, (byte[] Secret, IReadOnlyList<string> RecoveryCodeHashes)> _enrollmentLookup;
    private readonly TotpChallengeService _challengeService;
    private readonly IOneWayHasher _oneWayHasher;

    /// <summary>Initializes a new instance of <see cref="TotpChallengeRecipeSample"/> for demonstration purposes.</summary>
    /// <param name="enrollmentLookup">
    /// A map of user id to that user's decrypted secret and recovery-code hashes. In a real
    /// implementation this is loaded from the consuming service's own storage, never hardcoded.
    /// </param>
    /// <param name="challengeService">The real <see cref="TotpChallengeService"/> this sample composes.</param>
    /// <param name="oneWayHasher">The real <see cref="IOneWayHasher"/> this sample verifies recovery codes with.</param>
    internal TotpChallengeRecipeSample(
        IReadOnlyDictionary<Guid, (byte[] Secret, IReadOnlyList<string> RecoveryCodeHashes)> enrollmentLookup,
        TotpChallengeService challengeService,
        IOneWayHasher oneWayHasher)
    {
        ArgumentNullException.ThrowIfNull(enrollmentLookup);
        ArgumentNullException.ThrowIfNull(challengeService);
        ArgumentNullException.ThrowIfNull(oneWayHasher);
        _enrollmentLookup = enrollmentLookup;
        _challengeService = challengeService;
        _oneWayHasher = oneWayHasher;
    }

    /// <summary>
    /// Verifies a primary TOTP code presented by <paramref name="userId"/>.
    /// </summary>
    /// <param name="userId">The user attempting the step-up.</param>
    /// <param name="presentedCode">The candidate TOTP code.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> only for a fresh, valid, not-previously-used code for an enrolled user.</returns>
    internal async Task<bool> VerifyPrimaryCodeAsync(Guid userId, string presentedCode, CancellationToken ct)
    {
        if (!_enrollmentLookup.TryGetValue(userId, out var enrollment))
        {
            return false;
        }

        // TotpChallengeService.VerifyAsync itself records the successful challenge — no further
        // action needed here for the primary-code path.
        return await _challengeService.VerifyAsync(userId, enrollment.Secret, presentedCode, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies a recovery code presented by <paramref name="userId"/> when they have lost access to
    /// their authenticator device, and — on success — reports the step-up to the SAME freshness
    /// mechanism a primary TOTP code would.
    /// </summary>
    /// <param name="userId">The user attempting the step-up.</param>
    /// <param name="presentedRecoveryCode">The candidate recovery code.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns><see langword="true"/> only when the presented code matches one of the user's stored recovery-code hashes.</returns>
    /// <remarks>
    /// Recovery-code matching/consumption bookkeeping (marking the specific code used so it cannot be
    /// replayed) is entirely this recipe's own concern — <c>TotpChallengeService</c> is never involved
    /// in it, only in recording that a step-up occurred.
    /// </remarks>
    internal async Task<bool> VerifyRecoveryCodeAsync(Guid userId, string presentedRecoveryCode, CancellationToken ct)
    {
        if (!_enrollmentLookup.TryGetValue(userId, out var enrollment))
        {
            return false;
        }

        bool matched = enrollment.RecoveryCodeHashes.Any(hash =>
            _oneWayHasher.Verify(hash, presentedRecoveryCode) is HashVerificationResult.Success or HashVerificationResult.SuccessRehashNeeded);

        if (!matched)
        {
            return false;
        }

        // A real implementation also marks this specific recovery code consumed in its own storage
        // here, so it cannot be reused — out of scope for this sample.
        await _challengeService.RecordStepUpAsync(userId, ct).ConfigureAwait(false);
        return true;
    }
}
