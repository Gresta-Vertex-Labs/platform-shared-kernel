using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Security.Totp;

/// <summary>A new TOTP enrollment, before the user confirms it.</summary>
/// <remarks>
/// <para>
/// Show <see cref="ProvisioningUri"/> as a QR code (or <see cref="SecretBase32"/> for manual entry) and
/// <see cref="RecoveryCodes"/> once. Keep the enrollment pending until
/// <see cref="TotpEnrollmentService.ConfirmAsync"/> accepts a code from the authenticator app, then store the
/// encrypted <see cref="Secret"/>, the <see cref="Parameters"/> and <see cref="StoredRecoveryCodes"/>.
/// </para>
/// <para><see cref="ToString"/> omits the secret and the codes.</para>
/// </remarks>
public sealed class TotpEnrollment
{
    internal TotpEnrollment(
        byte[] secret,
        string secretBase32,
        Uri provisioningUri,
        TotpParameters parameters,
        IReadOnlyList<string> recoveryCodes,
        IReadOnlyList<StoredRecoveryCode> storedRecoveryCodes)
    {
        Secret = secret;
        SecretBase32 = secretBase32;
        ProvisioningUri = provisioningUri;
        Parameters = parameters;
        RecoveryCodes = recoveryCodes;
        StoredRecoveryCodes = storedRecoveryCodes;
    }

    /// <summary>Gets the shared secret. Encrypt it at rest, for example with <c>ISymmetricEncryptionService</c>.</summary>
    public byte[] Secret { get; }

    /// <summary>Gets <see cref="Secret"/> as unpadded Base32, for manual entry in an authenticator app.</summary>
    public string SecretBase32 { get; }

    /// <summary>Gets the <c>otpauth://totp/…</c> URI to show as a QR code.</summary>
    public Uri ProvisioningUri { get; }

    /// <summary>Gets the digits, period and algorithm the app will use. Store them with the secret.</summary>
    public TotpParameters Parameters { get; }

    /// <summary>Gets the recovery codes to show the user once. Never store them.</summary>
    public IReadOnlyList<string> RecoveryCodes { get; }

    /// <summary>Gets the hashed recovery codes to store.</summary>
    public IReadOnlyList<StoredRecoveryCode> StoredRecoveryCodes { get; }

    /// <summary>Returns a description without the secret or codes.</summary>
    /// <returns>The parameters and the number of recovery codes.</returns>
    public override string ToString() =>
        $"TotpEnrollment {{ Parameters = {Parameters}, RecoveryCodes = {RecoveryCodes.Count} }}";
}
