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
/// Existing values move to tenant keys with the maintenance job in <c>ReEncrypt</c> mode; run it (and check that
/// its report shows no value of the tenant outside the tenant's key) before shredding a tenant.
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
    /// Permanently destroys the tenant's data key and clears the tenant's blind indexes, making every value of the
    /// tenant encrypted under that key unreadable, by anyone, forever.
    /// </summary>
    /// <param name="tenantId">The tenant to erase.</param>
    /// <param name="options">
    /// <see langword="null"/> for the default: refuse (change nothing) when a value of the tenant is not under its data key.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was done, and whether the erasure is complete.</returns>
    /// <exception cref="InvalidOperationException">
    /// No cross-tenant scope is active (enter one around the call: shredding reaches the tenant's rows in every
    /// table and is attributed to the caller that entered the scope), or tenant data keys are not enabled.
    /// </exception>
    /// <exception cref="TenantShredIncompleteException">
    /// Some of the tenant's encrypted values are still under a root key or stored as plaintext, which destroying the
    /// tenant's key would not erase; nothing was changed. Move them to the tenant's key first (the maintenance job in
    /// <c>ReEncrypt</c> and <c>EncryptPlaintext</c> modes), delete them, or pass
    /// <see cref="TenantShredOptions.AllowIncompleteErasure"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>What is erased.</strong> The wrapped key is deleted and a tombstone kept, so the tenant can never be
    /// given a new key (and new data) under the same id. Blind indexes are cleared in the same transaction: an index
    /// is a keyed hash of the plaintext that, left behind, would still let anyone holding the blind-index key test
    /// guesses against the erased data. The ciphertext itself stays until you delete the rows; reading it throws
    /// <see cref="TenantKeyShreddedException"/>. Columns that are not encrypted are not touched: delete or anonymize
    /// them yourself.
    /// </para>
    /// <para>
    /// <strong>Other processes.</strong> Writes: every save that encrypts a value of the tenant re-reads the tombstone
    /// inside its own transaction, locking the tenant's key row, so no value is written under the tenant's key by a
    /// transaction that commits after the shred, and the shred waits for a transaction that locked the row first (and
    /// then clears what it wrote). A save without an explicit transaction reads the tombstone just before EF Core's own
    /// transaction starts, so it can still commit a value during a concurrent shred; the platform unit of work always
    /// runs saves in a transaction. Reads: this process forgets the key at once; other processes can still decrypt from
    /// their cached copy for up to <c>EncryptionOptions.TenantKeyCacheDuration</c>. SQL that bypasses EF Core (Dapper,
    /// raw SQL) is not checked.
    /// </para>
    /// <para>
    /// The wrapped key may persist in database backups: shredding is complete once those backups expire, or when the
    /// master key that wrapped it is destroyed. Record the erasure in your audit trail; this call logs it without the
    /// tenant id.
    /// </para>
    /// </remarks>
    Task<TenantShredResult> ShredTenantAsync(
        Guid tenantId,
        TenantShredOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Options of <see cref="ITenantEncryptionKeyManager.ShredTenantAsync"/>.</summary>
public sealed record TenantShredOptions
{
    /// <summary>
    /// Shred even when some of the tenant's encrypted values are under a root key or stored as plaintext, which the
    /// shred cannot erase: the platform then refuses to read them, but anyone holding the root key (or the database)
    /// still can. The result reports them (<see cref="TenantShredResult.IsComplete"/> is <see langword="false"/>);
    /// delete those rows to complete the erasure. Default <see langword="false"/>.
    /// </summary>
    public bool AllowIncompleteErasure { get; init; }
}

/// <summary>The result of <see cref="ITenantEncryptionKeyManager.ShredTenantAsync"/>.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="BlindIndexValuesCleared">Blind-index values set to null across the model.</param>
/// <param name="RootKeyValues">
/// Encrypted values of the tenant under a root key instead of the tenant's data key: not erased by the shred.
/// </param>
/// <param name="PlaintextValues">Values of the tenant's encrypted columns stored as plaintext: not erased by the shred.</param>
public sealed record TenantShredResult(Guid TenantId, long BlindIndexValuesCleared, long RootKeyValues, long PlaintextValues)
{
    /// <summary>
    /// Whether every encrypted value of the tenant was under its data key, so destroying the key erased all of them.
    /// </summary>
    public bool IsComplete => RootKeyValues == 0 && PlaintextValues == 0;
}
