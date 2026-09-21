namespace SharedKernel.Persistence.EfCore.Options;

/// <summary>
/// Options for <c>UsePostgreSQL(...)</c>, the platform's PostgreSQL provider setup.
/// </summary>
/// <remarks>
/// Everything not listed here is always on: snake_case naming, the <c>xmin</c> concurrency token, PostgreSQL's
/// 63-byte identifier limit, and SQLSTATE exception classification.
/// </remarks>
public sealed class PostgreSqlProviderOptions
{
    /// <summary>
    /// Gets or sets whether <c>Pgvector.Vector</c> columns are mapped and the <c>vector</c> extension is added
    /// to migrations. Off by default.
    /// </summary>
    /// <remarks>
    /// With the shared data source (<c>UsePostgreSQL(serviceProvider)</c>) vector support follows
    /// <c>NpgsqlPersistenceOptions.UseVector</c> automatically; setting this to <see langword="true"/> while
    /// the data source was built without it fails at startup, because the ADO-level type mapping lives on the
    /// data source. With an explicit <c>NpgsqlDataSource</c>, build that data source with
    /// <c>dataSourceBuilder.UseVector()</c> as well.
    /// </remarks>
    public bool UseVector { get; set; }

    /// <summary>
    /// Gets the retry-on-failure settings. Retry is <strong>on by default</strong>; set
    /// <see cref="PostgreSqlRetryOptions.Enabled"/> to <see langword="false"/> to turn it off.
    /// </summary>
    public PostgreSqlRetryOptions Retry { get; } = new();
}

/// <summary>
/// Npgsql's retrying execution strategy, as configured by <c>UsePostgreSQL(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// With retry on, EF Core re-runs an operation that failed with a transient error (connection loss, failover,
/// serialization failure, deadlock,...). A transaction must therefore run through the execution strategy as a
/// whole: use <c>IUnitOfWork.ExecuteInTransactionAsync</c>, which does exactly that and re-runs the whole
/// delegate on a retry. A raw <c>Database.BeginTransaction()</c> outside the strategy is rejected by EF Core.
/// </para>
/// <para>
/// This is the only place retry is configured; there is no separate builder switch.
/// </para>
/// </remarks>
public sealed class PostgreSqlRetryOptions
{
    /// <summary>Gets or sets whether transient failures are retried. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the maximum number of retries. Defaults to 6 (Npgsql's default).</summary>
    public int MaxRetryCount { get; set; } = 6;

    /// <summary>Gets or sets the longest delay between two attempts. Defaults to 30 seconds (Npgsql's default).</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets additional SQLSTATE codes to treat as transient, on top of Npgsql's own list. Empty by default.
    /// </summary>
    public ICollection<string> AdditionalTransientErrorCodes { get; } = new List<string>();
}
