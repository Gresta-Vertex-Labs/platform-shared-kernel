namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// The result of <see cref="IEnvelopeEncryptionProvider.GenerateDataKeyAsync(CancellationToken)"/>
/// — a fresh symmetric data key in both its immediately-usable plaintext form and its
/// safe-to-persist wrapped form.
/// </summary>
/// <param name="PlaintextKey">
/// The raw data key material. USE THIS IMMEDIATELY AND THEN DISCARD IT — NEVER PERSIST
/// <see cref="PlaintextKey"/> ANYWHERE (disk, database, cache, log). Persisting the plaintext data
/// key defeats the entire purpose of envelope encryption.
/// </param>
/// <param name="WrappedKey">
/// The data key wrapped by the provider's master key. THIS IS THE ONLY FORM SAFE TO PERSIST —
/// store this alongside the data it protects, and later recover the plaintext key via
/// <see cref="IEnvelopeEncryptionProvider.UnwrapDataKeyAsync(byte[], string, CancellationToken)"/>.
/// </param>
/// <param name="MasterKeyId">The identifier of the master key that produced <see cref="WrappedKey"/>, required to later unwrap it.</param>
public sealed record EnvelopeDataKey(byte[] PlaintextKey, byte[] WrappedKey, string MasterKeyId);
