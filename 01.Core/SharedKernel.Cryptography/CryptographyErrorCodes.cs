namespace SharedKernel.Cryptography;

/// <summary>
/// Well-known error code constants used by <c>SharedKernel.Cryptography</c> failure results.
/// </summary>
/// <remarks>
/// Defined locally per the platform convention that consuming packages may add their own
/// <c>ErrorCodes</c>-style constants without forking <c>SharedKernel.Primitives</c>.
/// </remarks>
public static class CryptographyErrorCodes
{
    /// <summary>The ciphertext or authentication tag failed integrity verification, or the wrong key was used.</summary>
    public const string DecryptionFailed = "cryptography.decryption_failed";

    /// <summary>The <see cref="Symmetric.EncryptedPayload.KeyId"/> on a payload could not be resolved to a known key.</summary>
    public const string UnknownKeyId = "cryptography.unknown_key_id";

    /// <summary>The supplied encoded string was not a valid self-describing payload produced by this service.</summary>
    public const string MalformedPayload = "cryptography.malformed_payload";
}
