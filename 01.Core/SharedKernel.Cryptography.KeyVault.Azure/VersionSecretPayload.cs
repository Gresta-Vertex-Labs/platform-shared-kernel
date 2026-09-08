namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// The durable content of one <see cref="AzureKeyVaultEncryptionKeyProvider"/> data-key version's
/// Key Vault Secret — everything <see cref="AzureKeyVaultEncryptionKeyProvider.GetKeyAsync"/> needs
/// to unwrap that version's plaintext data key, without this provider holding any persistent store
/// of its own beyond Key Vault itself.
/// </summary>
/// <param name="MasterKeyId">
/// The short Azure Key Vault master-key identifier (<c>"{azureKeyName}/{version}"</c>) that
/// produced <see cref="WrappedKeyBase64"/> — see
/// <see cref="Symmetric.EnvelopeDataKey.MasterKeyId"/>'s docs (P-496/WO-081) for why this is short
/// rather than a full key identifier URI.
/// </param>
/// <param name="WrappedKeyBase64">
/// The wrapped (never plaintext) data key, Base64-encoded — the only form of this version's data
/// key that is ever persisted to Key Vault Secrets.
/// </param>
internal sealed record VersionSecretPayload(string MasterKeyId, string WrappedKeyBase64);
