namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Carries the components produced by <see cref="ISymmetricEncryptionService.Encrypt(byte[], byte[])"/>
/// needed to later decrypt the ciphertext.
/// </summary>
/// <param name="KeyId">The identifier of the key version that encrypted this payload.</param>
/// <param name="Nonce">The 96-bit (12-byte) nonce used for this encryption call. Never reused.</param>
/// <param name="Ciphertext">The encrypted bytes.</param>
/// <param name="Tag">The 128-bit (16-byte) AES-GCM authentication tag.</param>
public sealed record EncryptedPayload(string KeyId, byte[] Nonce, byte[] Ciphertext, byte[] Tag);
