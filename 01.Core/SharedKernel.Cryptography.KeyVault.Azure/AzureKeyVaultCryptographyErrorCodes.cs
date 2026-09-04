namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// Well-known error code constants used by <c>SharedKernel.Cryptography.KeyVault.Azure</c>
/// failure results.
/// </summary>
/// <remarks>
/// Defined locally per the platform convention that consuming packages may add their own
/// <c>ErrorCodes</c>-style constants without forking <c>SharedKernel.Primitives</c> — mirrors
/// <c>SharedKernel.Cryptography</c>'s own <see cref="global::SharedKernel.Cryptography.CryptographyErrorCodes"/>.
/// </remarks>
public static class AzureKeyVaultCryptographyErrorCodes
{
    /// <summary>
    /// <c>masterKeyId</c> passed to <c>UnwrapDataKeyAsync</c> is not a well-formed Azure Key
    /// Vault key identifier URI of the shape this provider produces
    /// (<c>https://{vault}.vault.azure.net/keys/{name}/{version}</c>). This is detected purely
    /// locally, before any call reaches Azure — genuine Azure SDK failures (unreachable vault,
    /// permission denied, a wrapped key rejected as tampered by the vault itself) are never
    /// caught here and always propagate as a thrown exception instead, per this provider's
    /// fail-closed contract.
    /// </summary>
    public const string MalformedMasterKeyId = "cryptography.keyvault.azure.malformed_master_key_id";
}
