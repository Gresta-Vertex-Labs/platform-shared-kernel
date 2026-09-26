using SharedKernel.Execution.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Totp.Internal;

namespace SharedKernel.Security.Totp;

/// <summary>Checks TOTP codes and recovery codes, and records a step-up for the caller's session.</summary>
/// <remarks>
/// A step-up is keyed by the user's subject id and session id, and lasts <see cref="TotpStepUpOptions.FreshnessWindow"/>.
/// Replay protection and throttling are keyed by the subject id, so they apply across all of the user's sessions.
/// </remarks>
public sealed class TotpChallengeService
{
    private readonly ITotpVerifier _verifier;
    private readonly ITotpStepUpStore _stepUps;
    private readonly IRecoveryCodeStore _recoveryCodes;
    private readonly IOneWayHasher _hasher;
    private readonly IClock _clock;
    private readonly IOptions<TotpStepUpOptions> _options;
    private readonly ILogger<TotpChallengeService> _logger;
    private readonly ITotpAttemptThrottle? _throttle;

    /// <summary>Creates the service.</summary>
    /// <param name="verifier">Checks codes and rejects a reused time step.</param>
    /// <param name="stepUps">Records step-ups.</param>
    /// <param name="recoveryCodes">Holds hashed recovery codes.</param>
    /// <param name="hasher">Verifies recovery codes.</param>
    /// <param name="clock">The time source.</param>
    /// <param name="options">The step-up options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="throttle">Limits attempts, when registered.</param>
    public TotpChallengeService(
        ITotpVerifier verifier,
        ITotpStepUpStore stepUps,
        IRecoveryCodeStore recoveryCodes,
        IOneWayHasher hasher,
        IClock clock,
        IOptions<TotpStepUpOptions> options,
        ILogger<TotpChallengeService> logger,
        ITotpAttemptThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(stepUps);
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _verifier = verifier;
        _stepUps = stepUps;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
        _clock = clock;
        _options = options;
        _logger = logger;
        _throttle = throttle;
    }

    /// <summary>Checks a code from the user's authenticator app and, when correct, records a step-up for the session.</summary>
    /// <param name="user">The caller; must be a user with a session id.</param>
    /// <param name="secret">The user's decrypted secret.</param>
    /// <param name="code">The code the user entered.</param>
    /// <param name="parameters">The parameters the secret was enrolled with, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>The outcome; only <see cref="TotpChallengeResult.Verified"/> records a step-up.</returns>
    public async ValueTask<TotpChallengeResult> VerifyCodeAsync(
        IUserContext user,
        ReadOnlyMemory<byte> secret,
        string code,
        TotpParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(code);

        if (!TryGetSession(user, out string? subjectId, out string? sessionId))
        {
            return NotAccepted(TotpChallengeResult.NoSession, "Code");
        }

        if (await TotpAttempts.IsThrottledAsync(_throttle, subjectId, cancellationToken).ConfigureAwait(false))
        {
            return NotAccepted(TotpChallengeResult.Throttled, "Code");
        }

        TotpVerificationResult verification = await _verifier
            .VerifyAsync(subjectId, secret, code, parameters, cancellationToken)
            .ConfigureAwait(false);

        switch (verification)
        {
            case TotpVerificationResult.Valid:
                await RecordStepUpAsync(subjectId, sessionId, "Code", cancellationToken).ConfigureAwait(false);
                return TotpChallengeResult.Verified;
            case TotpVerificationResult.Replayed:
                return NotAccepted(TotpChallengeResult.Replayed, "Code");
            default:
                return NotAccepted(TotpChallengeResult.Invalid, "Code");
        }
    }

    /// <summary>Redeems a recovery code once and, when it matches, records a step-up for the session.</summary>
    /// <param name="user">The caller; must be a user with a session id.</param>
    /// <param name="code">The recovery code the user entered; case, spaces and hyphens are ignored.</param>
    /// <param name="cancellationToken">A token to cancel the redemption.</param>
    /// <returns>The outcome; only <see cref="TotpChallengeResult.Verified"/> records a step-up and uses up the code.</returns>
    /// <remarks>Tell the user how many codes remain and offer new ones when few are left.</remarks>
    public async ValueTask<TotpChallengeResult> RedeemRecoveryCodeAsync(
        IUserContext user,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(code);

        if (!TryGetSession(user, out string? subjectId, out string? sessionId))
        {
            return NotAccepted(TotpChallengeResult.NoSession, "RecoveryCode");
        }

        if (await TotpAttempts.IsThrottledAsync(_throttle, subjectId, cancellationToken).ConfigureAwait(false))
        {
            return NotAccepted(TotpChallengeResult.Throttled, "RecoveryCode");
        }

        string normalized = RecoveryCodeGenerator.Normalize(code);
        string lookup = RecoveryCodeLookup.For(normalized);
        IReadOnlyList<StoredRecoveryCode> unused = await _recoveryCodes
            .GetUnusedAsync(subjectId, cancellationToken)
            .ConfigureAwait(false);

        StoredRecoveryCode? match = null;
        if (lookup.Length > 0)
        {
            foreach (StoredRecoveryCode candidate in unused)
            {
                if (string.Equals(candidate.Lookup, lookup, StringComparison.Ordinal)
                    && _hasher.Verify(candidate.Hash, normalized) is HashVerificationResult.Success or HashVerificationResult.SuccessRehashNeeded)
                {
                    match = candidate;
                    break;
                }
            }
        }

        if (match is null
            || !await _recoveryCodes.TryMarkUsedAsync(subjectId, match.Id, _clock.UtcNow, cancellationToken).ConfigureAwait(false))
        {
            return NotAccepted(TotpChallengeResult.Invalid, "RecoveryCode");
        }

        TotpLog.RecoveryCodeRedeemed(_logger, unused.Count - 1);
        await RecordStepUpAsync(subjectId, sessionId, "RecoveryCode", cancellationToken).ConfigureAwait(false);
        return TotpChallengeResult.Verified;
    }

    private static bool TryGetSession(
        IUserContext user,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? subjectId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? sessionId)
    {
        subjectId = user.SubjectId;
        sessionId = user.SessionId;
        return user.ActorKind == ActorKind.User && subjectId is not null && sessionId is not null;
    }

    private async ValueTask RecordStepUpAsync(string subjectId, string sessionId, string operation, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        await _stepUps
            .RecordAsync(subjectId, sessionId, now, now + _options.Value.FreshnessWindow, cancellationToken)
            .ConfigureAwait(false);
        TotpLog.StepUpRecorded(_logger, operation);
    }

    private TotpChallengeResult NotAccepted(TotpChallengeResult result, string operation)
    {
        TotpLog.ChallengeNotAccepted(_logger, result, operation);
        return result;
    }
}
