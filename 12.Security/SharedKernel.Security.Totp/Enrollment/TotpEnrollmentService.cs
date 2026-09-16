using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Security.Totp.Enrollment;

/// <summary>
/// Generates a new TOTP enrollment: a shared secret, its Base32/provisioning-URI encodings, and a
/// set of one-time recovery codes.
/// </summary>
/// <remarks>
/// <para>
/// Synchronous and side-effect-free — composes <c>01.Core/SharedKernel.Cryptography</c>'s
/// <see cref="TotpSecret.Generate"/>, <see cref="Base32.Encode"/>, <see cref="TotpProvisioningUri.Build"/> and
/// <see cref="IRecoveryCodeGenerator"/> — it never reimplements any RFC 6238/4226 primitive itself
/// (WO-069, P-452).
/// </para>
/// <para>
/// <b>This type never persists anything.</b> Encrypting the raw secret at rest and hashing each
/// recovery code at rest is entirely the consuming service's own responsibility — see this
/// package's README "Enrollment recipe" for the worked pattern using
/// <c>01.Core/SharedKernel.Cryptography</c>'s <c>ISymmetricEncryptionService</c>/<c>IOneWayHasher</c>.
/// This package has zero persistence coupling by design.
/// </para>
/// </remarks>
public sealed class TotpEnrollmentService
{
    private readonly ISecureRandomGenerator _randomGenerator;
    private readonly IRecoveryCodeGenerator _recoveryCodeGenerator;

    /// <summary>Creates a new <see cref="TotpEnrollmentService"/>.</summary>
    /// <param name="randomGenerator">
    /// The source of cryptographically secure randomness backing every generated secret. Registered by
    /// <c>01.Core</c>'s <c>AddSharedKernelCryptography</c>.
    /// </param>
    /// <param name="recoveryCodeGenerator">
    /// Generates the one-time recovery codes. Registered by <c>01.Core</c>'s <c>AddSharedKernelCryptography</c>.
    /// </param>
    public TotpEnrollmentService(ISecureRandomGenerator randomGenerator, IRecoveryCodeGenerator recoveryCodeGenerator)
    {
        ArgumentNullException.ThrowIfNull(randomGenerator);
        ArgumentNullException.ThrowIfNull(recoveryCodeGenerator);
        _randomGenerator = randomGenerator;
        _recoveryCodeGenerator = recoveryCodeGenerator;
    }

    /// <summary>
    /// Generates a brand-new TOTP enrollment: a fresh shared secret plus a set of one-time recovery
    /// codes.
    /// </summary>
    /// <param name="issuer">The service/organization name shown in the authenticator app (e.g. "Contoso"). Must not contain a colon.</param>
    /// <param name="accountName">The account identifier shown alongside the issuer (e.g. the user's email address). Must not contain a colon.</param>
    /// <param name="parameters">
    /// The digits, step, drift and algorithm to enroll with, or <see langword="null"/> for
    /// <see cref="TotpParameters.Default"/> (6 digits, 30 seconds, SHA-1), which every mainstream authenticator
    /// app supports.
    /// </param>
    /// <param name="secretLengthBytes">
    /// The number of cryptographically secure random bytes underlying the secret, from 16 to 64. Defaults to
    /// <see cref="TotpSecret.DefaultLength"/> (20 bytes, 160 bits, as RFC 4226 recommends).
    /// </param>
    /// <param name="recoveryCodeCount">The number of one-time recovery codes to generate, from 1 to 50. Defaults to 10.</param>
    /// <returns>
    /// A new <see cref="TotpEnrollment"/> carrying the raw secret, its encodings, the parameters, and the
    /// recovery codes — none of which this method persists.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="issuer"/> or <paramref name="accountName"/> is <see langword="null"/>, empty, whitespace,
    /// or contains a colon.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="secretLengthBytes"/> or <paramref name="recoveryCodeCount"/> is out of range.
    /// </exception>
    public TotpEnrollment GenerateEnrollment(
        string issuer,
        string accountName,
        TotpParameters? parameters = null,
        int secretLengthBytes = TotpSecret.DefaultLength,
        int recoveryCodeCount = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        parameters ??= TotpParameters.Default;

        byte[] secret = TotpSecret.Generate(_randomGenerator, secretLengthBytes);
        Uri provisioningUri = TotpProvisioningUri.Build(issuer, accountName, secret, parameters);
        string secretBase32 = Base32.Encode(secret);
        IReadOnlyList<string> recoveryCodes = _recoveryCodeGenerator.GenerateCodes(recoveryCodeCount);

        return new TotpEnrollment(secret, secretBase32, provisioningUri, parameters, recoveryCodes);
    }
}
