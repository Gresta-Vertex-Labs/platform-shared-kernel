using SharedKernel.Primitives.Errors;

namespace SharedKernel.Cryptography.Internal;

/// <summary>The failed results this package returns; the one place an <see cref="Error"/> is built.</summary>
internal static class CryptographyErrors
{
    public static Error DecryptionFailed() => Error.Validation(
        CryptographyErrorCodes.DecryptionFailed,
        "The payload could not be decrypted: it was altered, or the associated data or key does not match.");

    // Rule 30: the key id comes from an untrusted payload, so it is never echoed. Recorded exception to "missing is
    // NotFound": a retired or wrong key is a deployment defect, not a missing resource the caller can ask for.
    public static Error UnknownKeyId() => Error.Unexpected(
        CryptographyErrorCodes.UnknownKeyId,
        "The payload was encrypted with a key that is not available.");

    public static Error MalformedPayload() => Error.Validation(
        CryptographyErrorCodes.MalformedPayload,
        "The value is not an encrypted payload.");

    public static Error DataKeyUnwrapFailed() => Error.Validation(
        CryptographyErrorCodes.DataKeyUnwrapFailed,
        "The payload's data key could not be unwrapped.");

    public static Error DataKeyWrongLength() => Error.Validation(
        CryptographyErrorCodes.DataKeyUnwrapFailed,
        "The payload's data key has the wrong length.");

    public static Error InvalidBase32() => Error.Validation(
        CryptographyErrorCodes.InvalidBase32Encoding,
        "The text is not valid Base32.");
}
