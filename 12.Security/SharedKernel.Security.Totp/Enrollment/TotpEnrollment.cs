using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Security.Totp.Enrollment;

/// <summary>
/// The result of generating a new TOTP enrollment: everything needed to show a user a QR code /
/// manual-entry secret and a one-time set of recovery codes.
/// </summary>
/// <remarks>
/// <para>
/// Produced by <see cref="TotpEnrollmentService.GenerateEnrollment"/>. Every field here except
/// <see cref="Parameters"/> is <b>sensitive, one-time-display material</b> — this package never persists
/// any of it. <see cref="Secret"/> must be encrypted at rest (e.g. via
/// <c>01.Core/SharedKernel.Cryptography</c>'s <c>ISymmetricEncryptionService</c>), and each entry of
/// <see cref="RecoveryCodes"/> hashed at rest (e.g. via that same package's <c>IOneWayHasher</c>) in its
/// <see cref="RecoveryCodeGenerator.Normalize"/>-normalized form, so a code typed later in lowercase or
/// without its hyphen still verifies — see this package's README for the full recipe.
/// </para>
/// </remarks>
/// <param name="Secret">The raw TOTP shared-secret bytes.</param>
/// <param name="SecretBase32">
/// <see cref="Secret"/> encoded as unpadded RFC 4648 Base32 text, for manual entry when a user
/// cannot scan the <see cref="ProvisioningUri"/> as a QR code.
/// </param>
/// <param name="ProvisioningUri">
/// The <c>otpauth://totp/...</c> Key Uri Format URI an authenticator app scans or accepts as a
/// manual-entry link.
/// </param>
/// <param name="Parameters">
/// The digits, step, drift and algorithm the secret was enrolled with. Store them with the secret and pass
/// them to <see cref="Challenge.TotpChallengeService.VerifyAsync"/>; authenticator apps keep generating codes
/// with the parameters they scanned.
/// </param>
/// <param name="RecoveryCodes">
/// The plaintext one-time backup codes, shown to the user exactly once at enrollment time.
/// </param>
public sealed record TotpEnrollment(
    byte[] Secret,
    string SecretBase32,
    Uri ProvisioningUri,
    TotpParameters Parameters,
    IReadOnlyList<string> RecoveryCodes);
