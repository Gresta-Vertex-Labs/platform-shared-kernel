namespace SharedKernel.Persistence.Abstractions.Coordination;

/// <summary>
/// A cross-replica coordination lock used to serialize startup migration/seeding across multiple
/// instances of the same service racing to migrate the same database.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>SharedKernel.Persistence.EfCore</c>'s <c>MigrationAndSeedHostedService</c>,
/// which previously issued raw PostgreSQL <c>pg_advisory_lock</c> SQL directly — a hard violation of
/// this domain's package graph (<c>SharedKernel.Persistence.EfCore</c> must never contain
/// provider-specific SQL). The concrete PostgreSQL implementation
/// (<c>NpgsqlAdvisoryMigrationLock</c>) lives in <c>SharedKernel.Persistence.Npgsql</c>, which already
/// owns the platform's one shared <see cref="Connections.IDbConnectionFactory"/> implementation and is
/// the layer legally positioned to issue provider-specific SQL.
/// </para>
/// <para>
/// <strong>No lock registered:</strong> <c>MigrationAndSeedHostedService</c> resolves this interface
/// as an optional DI service. When migrations-on-startup or a seeder is configured and no
/// <see cref="IMigrationLock"/> is registered, it logs an Error-level warning — startup coordination
/// across replicas is NOT guaranteed in that configuration — then proceeds without a lock (never a
/// hard crash-loop for a deliberately single-replica or non-PostgreSQL deployment). Register a real
/// implementation, or gate startup entirely on a single migration Job/init container, to close this
/// gap.
/// </para>
/// </remarks>
public interface IMigrationLock
{
    /// <summary>
    /// Acquires a named, cross-replica exclusive lock, waiting up to <paramref name="timeout"/>.
    /// </summary>
    /// <param name="lockKey">
    /// The lock's name. Callers typically use the migrated <c>DbContext</c>'s full type name so
    /// distinct contexts never contend on the same lock.
    /// </param>
    /// <param name="timeout">The maximum time to wait for the lock before giving up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A handle that releases the lock when disposed. Callers must dispose it — typically via
    /// <see langword="await using"/> — as soon as the guarded work completes.
    /// </returns>
    /// <exception cref="TimeoutException">
    /// Thrown when the lock is not acquired within <paramref name="timeout"/>.
    /// </exception>
    Task<IAsyncDisposable> AcquireAsync(
        string lockKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
