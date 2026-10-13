using SharedKernel.Persistence.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Builds the <see cref="Error"/> values and the <see cref="FieldEncryptionException"/> values of field encryption. The
/// codes are declared once in <see cref="PersistenceErrorCodes.Encryption"/>. The <see cref="Error"/> message is fixed and
/// client-safe; the exception message may name the entity property and the remedy, never a value or a provider's key id
/// or text.
/// </summary>
internal static class EncryptionErrors
{
    /// <summary>A key id the configured key source does not know.</summary>
    /// <param name="keyId">The field-encryption key id (the service's own, never a provider master key).</param>
    public static Error KeyNotFound(string keyId) =>
        Error.Unexpected(
            PersistenceErrorCodes.Encryption.KeyNotFound,
            $"Encryption key '{keyId}' is not available from the configured key source.");

    /// <summary>A shredded tenant's data was read or written.</summary>
    public static Error TenantKeyShredded() =>
        Error.NotFound(
            PersistenceErrorCodes.Encryption.TenantKeyShredded,
            "The tenant's data was erased and is no longer readable or writable.");

    /// <summary>A tenant shred was refused because some of the tenant's values are not under the tenant's key.</summary>
    public static Error TenantShredIncomplete() =>
        Error.Conflict(
            PersistenceErrorCodes.Encryption.TenantShredIncomplete,
            "The tenant's data is not fully erased yet, because some of it is not under the tenant's key.");

    public static FieldEncryptionException MalformedPayload(string propertyName) =>
        new(
            $"The stored value of encrypted property '{propertyName}' is not an encrypted payload: it was written " +
            "unencrypted, truncated or altered. To encrypt existing plaintext, run the maintenance job with " +
            "'EncryptionMaintenanceMode.EncryptPlaintext'.",
            Error.Unexpected(PersistenceErrorCodes.Encryption.MalformedPayload, "A stored encrypted value is not a valid encrypted payload."));

    public static FieldEncryptionException DecryptionFailed(string propertyName) =>
        new(
            $"Decryption failed for encrypted property '{propertyName}': the value was altered, or copied from another row, " +
            "column or tenant.",
            Error.Unexpected(PersistenceErrorCodes.Encryption.DecryptionFailed, "A stored encrypted value could not be decrypted."));

    public static FieldEncryptionException DataKeyInvalid(int generatedLength, int minimumLength) =>
        new(
            $"The envelope provider generated a {generatedLength}-byte data key, and tenant data keys need at least {minimumLength} bytes. " +
            "Check the provider's data key size.",
            Error.Unexpected(PersistenceErrorCodes.Encryption.DataKeyInvalid, "The tenant data key is not valid."));

    /// <summary>
    /// The provider could not unwrap the stored data key. Only the provider's <see cref="ErrorType"/> is kept (an outage
    /// or a timeout stays one); its message and the master key id never reach the text.
    /// </summary>
    public static FieldEncryptionException DataKeyUnwrapFailed(ErrorType providerErrorType)
    {
        const string message = "The tenant data key could not be unwrapped.";
        const string code = PersistenceErrorCodes.Encryption.DataKeyUnwrapFailed;
        var error = providerErrorType switch
        {
            ErrorType.Unavailable => Error.Unavailable(code, message),
            ErrorType.Timeout => Error.Timeout(code, message),
            _ => Error.Unexpected(code, message),
        };

        return new FieldEncryptionException(
            "The envelope provider could not unwrap the tenant data key. Check that the provider and its master key are available.",
            error);
    }
}
