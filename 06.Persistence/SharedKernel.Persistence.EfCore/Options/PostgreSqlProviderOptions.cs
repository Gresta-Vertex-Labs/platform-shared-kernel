namespace SharedKernel.Persistence.EfCore.Options;

/// <summary>
/// Options for the platform's PostgreSQL provider setup (<c>UsePostgreSQL(...)</c>, or
/// <c>ConfigureProvider(...)</c> on the <c>AddSharedKernelPostgres</c> builder).
/// </summary>
/// <remarks>
/// <para>
/// Everything not listed here is always on: snake_case naming, the <c>xmin</c> concurrency token, PostgreSQL's
/// 63-byte identifier limit, and SQLSTATE exception classification.
/// </para>
/// <para>
/// <strong>Retry is on by default</strong> (Npgsql's retrying execution strategy). EF Core re-runs an operation
/// that failed with a transient error (connection loss, failover, serialization failure, deadlock, ...), so a
/// transaction must run through the execution strategy as a whole: use <c>IUnitOfWork.ExecuteInTransactionAsync</c>,
/// which re-runs the whole delegate on a retry. Set <see cref="MaxRetryCount"/> to 0 to turn retry off.
/// </para>
/// </remarks>
public sealed class PostgreSqlProviderOptions
{
    /// <summary>
    /// Gets or sets whether <c>Pgvector.Vector</c> columns are mapped and the <c>vector</c> extension is added
    /// to migrations. Off by default.
    /// </summary>
    /// <remarks>
    /// With the shared data source vector support follows <c>NpgsqlPersistenceOptions.UseVector</c> automatically;
    /// setting this to <see langword="true"/> while the data source was built without it fails at startup, because
    /// the ADO-level type mapping lives on the data source. With an explicit <c>NpgsqlDataSource</c>, build that data
    /// source with <c>dataSourceBuilder.UseVector()</c> as well.
    /// </remarks>
    public bool UseVector { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retries of a transient failure. Defaults to 6 (Npgsql's default);
    /// 0 turns retry off.
    /// </summary>
    public int MaxRetryCount { get; set; } = 6;

    /// <summary>Gets or sets the longest delay between two attempts. Defaults to 30 seconds (Npgsql's default).</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets additional SQLSTATE codes to treat as transient, on top of Npgsql's own list. Empty by default.
    /// </summary>
    public ICollection<string> AdditionalTransientErrorCodes { get; } = new List<string>();
}
