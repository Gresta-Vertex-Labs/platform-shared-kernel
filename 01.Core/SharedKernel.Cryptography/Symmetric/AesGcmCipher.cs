using System.Security.Cryptography;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>The AES-256-GCM operations shared by the synchronous, asynchronous and envelope services.</summary>
internal static class AesGcmCipher
{
    public const int KeySize = 32;

    public static EncryptedPayload Encrypt(CryptographicKey key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        EnsureKeySize(key);

        Span<byte> nonce = stackalloc byte[EncryptedPayload.NonceSize];
        Span<byte> tag = stackalloc byte[EncryptedPayload.TagSize];
        byte[] ciphertext = new byte[plaintext.Length];
        RandomNumberGenerator.Fill(nonce);

        using (var aes = new AesGcm(key.Material, EncryptedPayload.TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        return new EncryptedPayload(key.Id, nonce, ciphertext, tag);
    }

    public static Result<byte[]> Decrypt(CryptographicKey? key, EncryptedPayload payload, ReadOnlySpan<byte> associatedData)
    {
        if (key is null)
        {
            return CryptographyErrors.UnknownKeyId();
        }

        EnsureKeySize(key);

        byte[] plaintext = new byte[payload.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key.Material, EncryptedPayload.TagSize);
            aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            return CryptographyErrors.DecryptionFailed();
        }
    }

    /// <summary>
    /// Rejects key material that is not 32 bytes. <see cref="AesGcm"/> would otherwise accept 16 or 24 bytes and
    /// silently run AES-128 or AES-192. A wrong length is a provider misconfiguration, so it throws.
    /// </summary>
    private static void EnsureKeySize(CryptographicKey key)
    {
        if (key.Material.Length != KeySize)
        {
            throw new CryptographicException(
                $"The key '{key.Id}' is {key.Material.Length} bytes; AES-256-GCM requires {KeySize} bytes.");
        }
    }
}

/// <summary>The failed results this package returns.</summary>
internal static class CryptographyErrors
{
    public static Error DecryptionFailed() => Error.Validation(
        CryptographyErrorCodes.DecryptionFailed,
        "The payload could not be decrypted: it was altered, or the associated data or key does not match.");

    public static Error UnknownKeyId() => Error.Unexpected(
        CryptographyErrorCodes.UnknownKeyId,
        "The payload was encrypted with a key that is not available.");

    public static Error MalformedPayload() => Error.Validation(
        CryptographyErrorCodes.MalformedPayload,
        "The value is not an encrypted payload.");
}
