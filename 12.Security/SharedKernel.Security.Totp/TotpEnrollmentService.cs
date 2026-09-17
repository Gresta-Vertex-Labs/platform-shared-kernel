using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Totp.Internal;

namespace SharedKernel.Security.Totp;

/// <summary>Creates TOTP enrollments and confirms them with the first code from the authenticator app.</summary>
public sealed class TotpEnrollmentService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int CodeIdLength = 16;

    private readonly ISecureRandomGenerator _random;
    private readonly IRecoveryCodeGenerator _recoveryCodes;
    private readonly IOneWayHasher _hasher;
    private readonly ITotpVerifier _verifier;
    private readonly ITotpAttemptThrottle? _throttle;
    private readonly ILogger<TotpEnrollmentService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="random">The random source for secrets and code ids.</param>
    /// <param name="recoveryCodes">Generates recovery codes.</param>
    /// <param name="hasher">Hashes recovery codes.</param>
    /// <param name="verifier">Checks the confirmation code and records its time step.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="throttle">Limits confirmation attempts, when registered.</param>
    public TotpEnrollmentService(
        ISecureRandomGenerator random,
        IRecoveryCodeGenerator recoveryCodes,
        IOneWayHasher hasher,
        ITotpVerifier verifier,
        ILogger<TotpEnrollmentService> logger,
        ITotpAttemptThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(logger);
        _random = random;
        _recoveryCodes = recoveryCodes;
        _hasher = hasher;
        _verifier = verifier;
        _logger = logger;
        _throttle = throttle;
    }

    /// <summary>Creates an enrollment: a secret, its provisioning URI and recovery codes with their hashes.</summary>
    /// <param name="issuer">The service name shown in the authenticator app. Must not contain a colon.</param>
    /// <param name="accountName">The account shown in the app, such as the email address. Must not contain a colon.</param>
    /// <param name="parameters">The digits, period and algorithm, or <see langword="null"/> for 6 digits every 30 seconds with SHA-1, which every app supports.</param>
    /// <param name="secretLengthBytes">The secret length, from 16 to 64 bytes. Defaults to 20.</param>
    /// <param name="recoveryCodeCount">The number of recovery codes, from 1 to 50. Defaults to 10.</param>
    /// <returns>The enrollment. Nothing is stored.</returns>
    /// <remarks>Hashes every recovery code with the configured password hash, which takes a noticeable amount of CPU.</remarks>
    /// <exception cref="ArgumentException"><paramref name="issuer"/> or <paramref name="accountName"/> is empty or contains a colon.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A length or count is out of range.</exception>
    public TotpEnrollment Create(
        string issuer,
        string accountName,
        TotpParameters? parameters = null,
        int secretLengthBytes = TotpSecret.DefaultLength,
        int recoveryCodeCount = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        parameters ??= TotpParameters.Default;

        byte[] secret = TotpSecret.Generate(_random, secretLengthBytes);
        Uri provisioningUri = TotpProvisioningUri.Build(issuer, accountName, secret, parameters);
        IReadOnlyList<string> codes = _recoveryCodes.GenerateCodes(recoveryCodeCount);

        var stored = new StoredRecoveryCode[codes.Count];
        Parallel.For(0, codes.Count, i =>
        {
            string normalized = RecoveryCodeGenerator.Normalize(codes[i]);
            stored[i] = new StoredRecoveryCode(
                _random.GetString(Base32Alphabet, CodeIdLength),
                RecoveryCodeLookup.For(normalized),
                _hasher.Hash(normalized));
        });

        return new TotpEnrollment(secret, Base32.Encode(secret), provisioningUri, parameters, codes, stored);
    }

    /// <summary>
    /// Checks the first code from the authenticator app, proving the user scanned the secret. Activate the enrollment
    /// only on <see cref="TotpChallengeResult.Verified"/>.
    /// </summary>
    /// <param name="user">The user enrolling.</param>
    /// <param name="secret">The secret from <see cref="TotpEnrollment.Secret"/>.</param>
    /// <param name="code">The code the user entered.</param>
    /// <param name="parameters">The parameters from <see cref="TotpEnrollment.Parameters"/>.</param>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>
    /// <see cref="TotpChallengeResult.Verified"/>, <see cref="TotpChallengeResult.Invalid"/>,
    /// <see cref="TotpChallengeResult.Replayed"/>, <see cref="TotpChallengeResult.Throttled"/>, or
    /// <see cref="TotpChallengeResult.NoSession"/> when the caller is not a user.
    /// </returns>
    /// <remarks>Does not record a step-up. The accepted time step cannot be used again for a challenge.</remarks>
    public async ValueTask<TotpChallengeResult> ConfirmAsync(
        IUserContext user,
        ReadOnlyMemory<byte> secret,
        string code,
        TotpParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(code);

        TotpChallengeResult result;
        if (user.IdentityKind != IdentityKind.User || user.SubjectId is not { } subjectId)
        {
            result = TotpChallengeResult.NoSession;
        }
        else if (await TotpAttempts.IsThrottledAsync(_throttle, subjectId, cancellationToken).ConfigureAwait(false))
        {
            result = TotpChallengeResult.Throttled;
        }
        else
        {
            TotpVerificationResult verification = await _verifier
                .VerifyAsync(subjectId, secret, code, parameters, cancellationToken)
                .ConfigureAwait(false);
            result = verification switch
            {
                TotpVerificationResult.Valid => TotpChallengeResult.Verified,
                TotpVerificationResult.Replayed => TotpChallengeResult.Replayed,
                _ => TotpChallengeResult.Invalid,
            };
        }

        if (result != TotpChallengeResult.Verified)
        {
            TotpLog.ChallengeNotAccepted(_logger, result, "ConfirmEnrollment");
        }

        return result;
    }
}
