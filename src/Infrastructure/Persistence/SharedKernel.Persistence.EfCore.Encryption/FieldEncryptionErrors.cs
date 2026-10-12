using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Builds <see cref="FieldEncryptionException"/> values. The <see cref="Error"/> message is fixed and client-safe; the
/// exception message may name the entity property and the remedy, never a value or a provider's key id or text.
/// </summary>
internal static class FieldEncryptionErrors
{
    public static FieldEncryptionException MalformedPayload(string propertyName) =>
        new(
            $"The stored value of encrypted property '{propertyName}' is not an encrypted payload: it was written " +
            "unencrypted, truncated or altered. To encrypt existing plaintext, run the maintenance job with " +
            "'EncryptionMaintenanceMode.EncryptPlaintext'.",
            Error.Unexpected(FieldEncryptionException.MalformedPayloadCode, "A stored encrypted value is not a valid encrypted payload."));

    public static FieldEncryptionException DecryptionFailed(string propertyName) =>
        new(
            $"Decryption failed for encrypted property '{propertyName}': the value was altered, or copied from another row, " +
            "column or tenant.",
            Error.Unexpected(FieldEncryptionException.DecryptionFailedCode, "A stored encrypted value could not be decrypted."));

    public static FieldEncryptionException DataKeyInvalid(int generatedLength, int minimumLength) =>
        new(
            $"The envelope provider generated a {generatedLength}-byte data key; tenant data keys need at least {minimumLength} bytes. " +
            "Check the provider's data key size.",
            Error.Unexpected(FieldEncryptionException.DataKeyInvalidCode, "The tenant data key is not valid."));

    /// <summary>
    /// The provider could not unwrap the stored data key. Only the provider's <see cref="ErrorType"/> is kept (an outage
    /// or a timeout stays one); its message and the master key id never reach the text.
    /// </summary>
    public static FieldEncryptionException DataKeyUnwrapFailed(ErrorType providerErrorType)
    {
        const string message = "The tenant data key could not be unwrapped.";
        var error = providerErrorType switch
        {
            ErrorType.Unavailable => Error.Unavailable(FieldEncryptionException.DataKeyUnwrapFailedCode, message),
            ErrorType.Timeout => Error.Timeout(FieldEncryptionException.DataKeyUnwrapFailedCode, message),
            _ => Error.Unexpected(FieldEncryptionException.DataKeyUnwrapFailedCode, message),
        };

        return new FieldEncryptionException(
            "The envelope provider could not unwrap the tenant data key. Check that the provider and its master key are available.",
            error);
    }
}
