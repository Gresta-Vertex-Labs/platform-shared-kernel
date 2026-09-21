namespace SharedKernel.Persistence.EfCore.Encryption.TenantKeys;

/// <summary>
/// Manages per-tenant data keys: each tenant's encrypted values are encrypted under a data key of its own, wrapped
/// by a KMS master key, so erasing a tenant is destroying one key (crypto-shredding).
/// </summary>
/// <remarks>
/// <para>
/// Enabled with <c>UseFieldEncryption(k =&gt; k.UseTenantDataKeys())</c>, which needs an
/// <c>IEnvelopeEncryptionProvider</c> (for example Azure Key Vault's) and the tenant key table
/// (<c>migrationBuilder.CreateTenantEncryptionKeyTable()</c>). Keys are created on a tenant's first encrypted write.
/// Registered (scoped) for every context that calls <c>UseFieldEncryption</c>.
/// </para>
/// <para>
/// Existing values move to tenant keys with the maintenance job in <c>ReEncrypt</c> mode.
/// </para>
/// </remarks>
public interface ITenantEncryptionKeyManager
{
    /// <summary>Creates the tenant's data key now instead of on its first encrypted write.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TenantKeyShreddedException">The tenant was shredded; a shredded tenant never gets a new key.</exception>
    Task EnsureTenantKeyAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently destroys the tenant's data key and clears the tenant's blind indexes, making every encrypted value
    /// of the tenant unreadable, by anyone, forever.
    /// </summary>
    /// <param name="tenantId">The tenant to erase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was done.</returns>
    /// <remarks>
    /// <para>
    /// The wrapped key is deleted and a tombstone kept, so the tenant can never be given a new key (and new data)
    /// under the same id. Blind indexes are cleared in the same transaction: an index is a keyed hash of the
    /// plaintext that, left behind, would still let anyone holding the blind-index key test guesses against the
    /// erased data. The ciphertext itself stays until you delete the rows; reading it throws
    /// <see cref="TenantKeyShreddedException"/>.
    /// </para>
    /// <para>
    /// This process forgets the key at once; other processes stop being able to decrypt when their cached copy
    /// expires, within <c>EncryptionOptions.TenantKeyCacheDuration</c>. The wrapped key may persist in database
    /// backups: shredding is complete once those backups expire, or when the master key that wrapped it is destroyed.
    /// Record the erasure in your audit trail; this call logs it without the tenant id.
    /// </para>
    /// </remarks>
    Task<TenantShredResult> ShredTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>The result of <see cref="ITenantEncryptionKeyManager.ShredTenantAsync"/>.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="BlindIndexValuesCleared">Blind-index values set to null across the model.</param>
public sealed record TenantShredResult(Guid TenantId, long BlindIndexValuesCleared);
