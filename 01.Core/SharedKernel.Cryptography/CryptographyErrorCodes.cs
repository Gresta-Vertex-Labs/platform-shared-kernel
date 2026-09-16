namespace SharedKernel.Cryptography;

/// <summary>Error codes carried by the failed results this package returns.</summary>
/// <remarks>
/// A failed decryption is reported by cause. <see cref="MalformedPayload"/> and <see cref="DecryptionFailed"/>
/// are <see cref="Primitives.Errors.ErrorType.Validation"/>: the input could not be read or did not
/// authenticate, which is usually a tampered, truncated or mismatched value supplied from outside.
/// <see cref="UnknownKeyId"/> is <see cref="Primitives.Errors.ErrorType.Unexpected"/>: a well-formed payload
/// names a key the provider does not have, which is usually a retired key or a key configuration problem.
/// </remarks>
public static class CryptographyErrorCodes
{
    /// <summary>
    /// The payload did not authenticate: it was altered, the associated data differs from the data used to encrypt
    /// it, or the key with its id no longer holds the same material. AES-GCM cannot tell these apart.
    /// </summary>
    public const string DecryptionFailed = "cryptography.decryption_failed";

    /// <summary>The payload names a key id the key provider does not know.</summary>
    public const string UnknownKeyId = "cryptography.unknown_key_id";

    /// <summary>The value is not a payload this package produced: wrong encoding, version, length or layout.</summary>
    public const string MalformedPayload = "cryptography.malformed_payload";

    /// <summary>The text is not valid RFC 4648 Base32.</summary>
    public const string InvalidBase32Encoding = "cryptography.invalid_base32_encoding";

    /// <summary>The envelope's wrapped data key could not be unwrapped by the envelope provider.</summary>
    public const string DataKeyUnwrapFailed = "cryptography.data_key_unwrap_failed";
}
