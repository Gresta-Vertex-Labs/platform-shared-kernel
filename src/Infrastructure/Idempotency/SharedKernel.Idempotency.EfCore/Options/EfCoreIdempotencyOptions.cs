namespace SharedKernel.Idempotency.EfCore.Options;

/// <summary>Configuration options for <see cref="Store.EfCoreIdempotencyStore"/>.</summary>
/// <remarks>
/// How long a reservation holds and how long a completed key is retained are chosen by the caller on every call
/// (the application pipeline's <c>IdempotencyBehaviorOptions</c>, messaging's <c>IdempotencyOptions</c>), not here:
/// the store only decides what happens when PostgreSQL cannot be reached.
/// </remarks>
public sealed class EfCoreIdempotencyOptions
{
    /// <summary>The configuration section name for <see cref="EfCoreIdempotencyOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Idempotency:EfCore";

    /// <summary>
    /// When <see langword="true"/>, a genuine PostgreSQL connectivity or timeout failure lets the guarded work proceed
    /// as if the key were new, instead of throwing. Defaults to <see langword="false"/> (fail closed).
    /// </summary>
    /// <remarks>
    /// ENABLING THIS OPTION INCREASES DUPLICATE-EXECUTION RISK: while the database is unreachable, every call —
    /// including genuine duplicates — is treated as new. Only enable it where running twice is safer than not running
    /// at all. It applies to every purpose this registration serves.
    /// </remarks>
    public bool AllowExecutionOnStoreUnavailable { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the package creates its own <c>idempotency_keys</c> table on startup by applying its
    /// shipped migration through <c>AddMigrationOnStartup&lt;IdempotencyDbContext&gt;</c>
    /// (<c>SharedKernel.Persistence.EfCore</c>). Defaults to <see langword="false"/>: the consumer creates the table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The migration runs on the migration data source (<c>SharedKernel:Persistence:{name}:MigrationConnectionString</c>)
    /// when one is configured, else on the context's own connection, under the cross-replica migration lock; the store
    /// itself keeps using the connection passed to <c>AddEfCoreIdempotency</c>. The host needs
    /// <c>AddSharedKernelPostgres</c> or <c>AddSharedKernelNpgsql</c> for the lock.
    /// </para>
    /// <para>
    /// Turning this on records the migration in the history table <c>__ef_migrations_history_idempotency</c>. The
    /// migration does nothing when <c>idempotency_keys</c> already exists. This option is read once, when
    /// <c>AddEfCoreIdempotency</c> runs; it cannot be flipped later by configuration binding.
    /// </para>
    /// </remarks>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// How long startup waits for the cross-replica migration lock when <see cref="MigrateOnStartup"/> is on.
    /// <see langword="null"/> (the default) uses the persistence default of 2 minutes. Must be positive when set.
    /// </summary>
    public TimeSpan? MigrationLockTimeout { get; set; }
}
