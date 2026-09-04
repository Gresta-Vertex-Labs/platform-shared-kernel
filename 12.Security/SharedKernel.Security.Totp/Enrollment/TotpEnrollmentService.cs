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
/// <see cref="ISecureRandomGenerator"/> (secret bytes), <see cref="Base32.Encode"/>,
/// <see cref="TotpProvisioningUri.Build"/>, and <see cref="RecoveryCodeGenerator.GenerateCodes"/> —
/// it never reimplements any RFC 6238/4226 primitive itself (WO-069, P-452).
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
    private readonly RecoveryCodeGenerator _recoveryCodeGenerator;

    /// <summary>Creates a new <see cref="TotpEnrollmentService"/>.</summary>
    /// <param name="randomGenerator">
    /// The source of cryptographically secure randomness backing every generated secret and recovery
    /// code. Registered by <c>01.Core</c>'s <c>AddSharedKernelCryptography</c>.
    /// </param>
    public TotpEnrollmentService(ISecureRandomGenerator randomGenerator)
    {
        ArgumentNullException.ThrowIfNull(randomGenerator);
        _randomGenerator = randomGenerator;
        _recoveryCodeGenerator = new RecoveryCodeGenerator(randomGenerator);
    }

    /// <summary>
    /// Generates a brand-new TOTP enrollment: a fresh shared secret plus a set of one-time recovery
    /// codes.
    /// </summary>
    /// <param name="issuer">The service/organization name shown in the authenticator app (e.g. "Contoso").</param>
    /// <param name="accountName">The account identifier shown alongside the issuer (e.g. the user's email address).</param>
    /// <param name="secretLengthBytes">The number of cryptographically secure random bytes underlying the secret. Defaults to 20 (160 bits, the RFC 4226 recommended minimum).</param>
    /// <param name="recoveryCodeCount">The number of one-time recovery codes to generate. Defaults to 10.</param>
    /// <returns>
    /// A new <see cref="TotpEnrollment"/> carrying the raw secret, its encodings, and the recovery
    /// codes — none of which this method persists.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="issuer"/> or <paramref name="accountName"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="secretLengthBytes"/> or <paramref name="recoveryCodeCount"/> is zero or negative.</exception>
    public TotpEnrollment GenerateEnrollment(
        string issuer,
        string accountName,
        int secretLengthBytes = 20,
        int recoveryCodeCount = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(secretLengthBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recoveryCodeCount);

        byte[] secret = _randomGenerator.NextBytes(secretLengthBytes);
        string secretBase32 = Base32.Encode(secret);
        Uri provisioningUri = TotpProvisioningUri.Build(issuer, accountName, secret);
        IReadOnlyList<string> recoveryCodes = _recoveryCodeGenerator.GenerateCodes(recoveryCodeCount);

        return new TotpEnrollment(secret, secretBase32, provisioningUri, recoveryCodes);
    }
}
