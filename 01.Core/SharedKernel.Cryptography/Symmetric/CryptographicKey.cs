namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// A versioned symmetric key resolved by <see cref="IEncryptionKeyProvider"/>.
/// </summary>
/// <param name="Id">The key's stable version identifier (embedded in every <see cref="EncryptedPayload"/> it produces).</param>
/// <param name="Material">The raw key material — 32 bytes for AES-256.</param>
public sealed record CryptographicKey(string Id, byte[] Material);
