namespace SharedKernel.Persistence.EfCore.Encryption.Maintenance;

/// <summary>What an <see cref="IEncryptionRotationJob"/> run does to each stored value. Combine the writing modes freely.</summary>
[Flags]
public enum EncryptionMaintenanceMode
{
    /// <summary>
    /// Decrypts every value and counts it by key, without writing anything. Run it before retiring a key: the key is
    /// safe to retire when the report is complete, shows no undecryptable values and no value on that key.
    /// </summary>
    VerifyOnly = 1,

    /// <summary>
    /// Re-encrypts every value that is not on the current key (the tenant's data key when tenant data keys are on,
    /// otherwise the key source's current key) and recomputes its blind index.
    /// </summary>
    ReEncrypt = 2,

    /// <summary>
    /// Recomputes every blind index under the current blind-index key version and normalization; use after rotating
    /// the blind-index key, changing a property's normalization, or adding <c>.WithBlindIndex()</c> to existing data.
    /// </summary>
    RecomputeBlindIndexes = 4,

    /// <summary>
    /// Encrypts values stored as plaintext, for a column that was just marked <c>.Encrypt(...)</c>. Every other mode
    /// leaves plaintext alone and reports it.
    /// </summary>
    EncryptPlaintext = 8,
}

/// <summary>One <see cref="IEncryptionRotationJob"/> run.</summary>
public sealed record EncryptionMaintenanceRequest
{
    /// <summary>What to do; <see cref="EncryptionMaintenanceMode.VerifyOnly"/> cannot be combined with another mode.</summary>
    public required EncryptionMaintenanceMode Mode { get; init; }

    /// <summary>
    /// The key id the key source's current key must have. Required for <see cref="EncryptionMaintenanceMode.ReEncrypt"/>
    /// and <see cref="EncryptionMaintenanceMode.EncryptPlaintext"/>: a run never writes values under a key the operator
    /// did not expect, for example because the new key was not made current yet.
    /// </summary>
    public string? ExpectedCurrentKeyId { get; init; }

    /// <summary>The token of an earlier, cancelled run, to continue where it stopped.</summary>
    public string? CheckpointToken { get; init; }

    /// <summary>Rows read and written per transaction. Default 500.</summary>
    public int BatchSize { get; init; } = 500;
}

/// <summary>Progress of a run, reported after each batch.</summary>
/// <param name="Target">The column being processed, <c>schema.table.column</c>.</param>
/// <param name="TargetIndex">The 1-based position of <paramref name="Target"/>.</param>
/// <param name="TargetCount">The number of encrypted columns in the model.</param>
/// <param name="RowsScanned">Rows scanned in <paramref name="Target"/> so far.</param>
/// <param name="EstimatedRows">PostgreSQL's estimate of the table's rows.</param>
public sealed record EncryptionMaintenanceProgress(
    string Target, int TargetIndex, int TargetCount, long RowsScanned, long EstimatedRows);

/// <summary>What an <see cref="IEncryptionRotationJob"/> run found and did.</summary>
/// <param name="Mode">The mode of the run.</param>
/// <param name="Completed"><see langword="true"/> when every encrypted column was scanned; otherwise continue with <paramref name="CheckpointToken"/>.</param>
/// <param name="CheckpointToken">Continues a cancelled run; <see langword="null"/> when complete.</param>
/// <param name="RowsScanned">Non-null values examined.</param>
/// <param name="ValuesReEncrypted">Values moved to the current key.</param>
/// <param name="ValuesEncryptedFromPlaintext">Plaintext values encrypted.</param>
/// <param name="BlindIndexesRecomputed">Blind indexes rewritten.</param>
/// <param name="ValuesConcurrentlyModified">Values changed by another writer between read and write; left untouched for the next run.</param>
/// <param name="PlaintextValues">Values still stored as plaintext.</param>
/// <param name="UndecryptableValues">Values that did not decrypt: unknown key, altered, or copied from another row or column. Needs investigation.</param>
/// <param name="ShreddedValues">Values of tenants whose data key was shredded.</param>
/// <param name="StaleBlindIndexes">Blind indexes missing or computed under a version other than the current one (verify mode).</param>
/// <param name="ValuesByKeyId">
/// The key each scanned value is encrypted with after the run, by key id; values under tenant data keys are counted
/// under <see cref="TenantDataKeysLabel"/>.
/// </param>
public sealed record EncryptionMaintenanceReport(
    EncryptionMaintenanceMode Mode,
    bool Completed,
    string? CheckpointToken,
    long RowsScanned,
    long ValuesReEncrypted,
    long ValuesEncryptedFromPlaintext,
    long BlindIndexesRecomputed,
    long ValuesConcurrentlyModified,
    long PlaintextValues,
    long UndecryptableValues,
    long ShreddedValues,
    long StaleBlindIndexes,
    IReadOnlyDictionary<string, long> ValuesByKeyId)
{
    /// <summary>The <see cref="ValuesByKeyId"/> entry that counts values under tenant data keys.</summary>
    public const string TenantDataKeysLabel = "(tenant data keys)";

    /// <summary>
    /// Whether this report proves <paramref name="keyId"/> can be removed from the key source: the run was complete,
    /// every value decrypted, and none uses the key. Only meaningful for a <see cref="EncryptionMaintenanceMode.VerifyOnly"/>
    /// run (or a writing run) over the whole model.
    /// </summary>
    /// <param name="keyId">The key id to retire.</param>
    public bool IsSafeToRetire(string keyId) =>
        Completed && UndecryptableValues == 0 && !ValuesByKeyId.ContainsKey(keyId);
}

/// <summary>
/// Migrates stored encrypted values: key rotation, plaintext migration, blind-index recomputation and verification.
/// </summary>
/// <remarks>
/// <para>
/// Registered (scoped) for every context that calls <c>UseFieldEncryption</c>. Run it from a hosted service, a
/// scheduled job or a workflow activity, never from request handling (SK0303).
/// </para>
/// <para>
/// Works column by column in primary-key order, in short transactions of <see cref="EncryptionMaintenanceRequest.BatchSize"/>
/// rows, reading and writing with plain SQL: no query filter hides soft-deleted or other tenants' rows, and no
/// interceptor stamps audit columns. Every write is a batched compare-and-swap on the value read, so a value changed
/// concurrently is left alone and counted, never overwritten. TPH columns shared by several types are processed
/// once; TPT and TPC columns are processed in the table that holds them.
/// </para>
/// <para>
/// Row-level security: by default each transaction runs with <c>row_security = off</c>, so a policy that would hide
/// rows fails the run instead of letting it report completion over the visible rows only
/// (<see cref="EncryptionOptions.RequireRowSecurityBypass"/>).
/// </para>
/// </remarks>
public interface IEncryptionRotationJob
{
    /// <summary>Runs <paramref name="request"/> over every encrypted column of the context's model.</summary>
    /// <param name="request">What to do.</param>
    /// <param name="progress">Receives progress after each batch.</param>
    /// <param name="cancellationToken">
    /// Checked between batches: a cancelled run returns what it did with <see cref="EncryptionMaintenanceReport.Completed"/>
    /// <see langword="false"/> and a checkpoint token, instead of throwing.
    /// </param>
    /// <returns>The report.</returns>
    Task<EncryptionMaintenanceReport> RunAsync(
        EncryptionMaintenanceRequest request,
        IProgress<EncryptionMaintenanceProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
