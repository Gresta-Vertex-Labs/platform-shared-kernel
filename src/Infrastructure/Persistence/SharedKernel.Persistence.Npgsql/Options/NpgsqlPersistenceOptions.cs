using System.ComponentModel.DataAnnotations;
using global::Npgsql;
using SharedKernel.Configuration;

namespace SharedKernel.Persistence.Npgsql.Options;

/// <summary>
/// Configuration for the platform's options-bound <see cref="NpgsqlDataSource"/> per database.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Where it is read from.</strong> A database registered under a connection name — by
/// <c>AddSharedKernelPostgres&lt;TContext&gt;("orders")</c> or <c>AddSharedKernelNpgsql(configuration, "orders")</c> —
/// reads these settings from <c>SharedKernel:Persistence:{name}</c> and its connection string from
/// <c>ConnectionStrings:{name}</c>. Only the unnamed <c>AddSharedKernelNpgsql(configuration)</c> reads
/// <see cref="SectionName"/> (<c>SharedKernel:Persistence:Npgsql</c>), connection string included. The sections of
/// the persistence packages themselves (<c>Encryption</c>, <c>Auditing</c>, <c>Dapper</c>) sit next to the connection
/// sections, so those names cannot be used as connection names. Validated at host startup.
/// </para>
/// <para>
/// <strong>Connection string.</strong> <see cref="ConnectionString"/>, or — when that is empty —
/// <c>ConnectionStrings:{<see cref="ConnectionStringName"/>}</c>, the shape .NET Aspire and most hosting
/// platforms inject.
/// </para>
/// <para>
/// <strong>TLS.</strong> The effective SSL mode is <see cref="SslMode"/> when set, otherwise the
/// connection string's own <c>SSL Mode</c> keyword when present, otherwise <c>Disable</c> for a loopback host
/// (local containers publish plain-text ports) and <c>VerifyFull</c> for any other host. A mode below
/// <c>VerifyFull</c> is accepted without further ceremony for a loopback host (<c>localhost</c>,
/// <c>127.0.0.1</c>, <c>::1</c>, a Unix socket) or in the <c>Development</c> environment; anywhere else it
/// also needs <see cref="AcknowledgeInsecureSslMode"/>, and a warning is logged at startup. For managed
/// databases (AWS RDS, Azure Database for PostgreSQL) install the provider's root certificate
/// (<c>Root Certificate=/path/global-bundle.pem</c> in the connection string, or the OS trust store) and
/// keep <c>VerifyFull</c>.
/// </para>
/// <para>
/// <c>Persist Security Info</c> is always forced off, so the password is never echoed back from an open
/// connection's connection string.
/// </para>
/// </remarks>
public sealed class NpgsqlPersistenceOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    /// <remarks>The section of the unnamed registration only; a named database reads <c>SharedKernel:Persistence:{name}</c>.</remarks>
    public static string SectionName => "SharedKernel:Persistence:Npgsql";

    /// <summary>The configuration path these options were bound from, for messages. Set by the registration.</summary>
    internal string SectionPath { get; set; } = SectionName;

    /// <summary>The settings section of the database registered under <paramref name="connectionName"/>.</summary>
    internal static string SectionFor(string connectionName) => "SharedKernel:Persistence:" + connectionName;

    /// <summary>The PostgreSQL connection string. Required unless <see cref="ConnectionStringName"/> resolves one.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The name under <c>ConnectionStrings</c> to read the connection string from when
    /// <see cref="ConnectionString"/> is empty (e.g. <c>"orders"</c> reads <c>ConnectionStrings:orders</c>).
    /// </summary>
    public string? ConnectionStringName { get; set; }

    /// <summary>
    /// The TLS mode, overriding the connection string's <c>SSL Mode</c>. <see langword="null"/> (default)
    /// keeps the connection string's value; with none, <c>Disable</c> for a loopback host and <c>VerifyFull</c> otherwise.
    /// </summary>
    public SslMode? SslMode { get; set; }

    /// <summary>
    /// Explicit acknowledgement of a TLS mode below <c>VerifyFull</c> for a non-loopback host outside the
    /// <c>Development</c> environment. Startup validation fails without it.
    /// </summary>
    public bool AcknowledgeInsecureSslMode { get; set; }

    /// <summary>The <c>statement_timeout</c> (milliseconds) of every connection, or <see langword="null"/> for the server default.</summary>
    [Range(1, int.MaxValue)]
    public int? StatementTimeoutMilliseconds { get; set; }

    /// <summary>The <c>lock_timeout</c> (milliseconds) of every connection, or <see langword="null"/> for the server default.</summary>
    [Range(1, int.MaxValue)]
    public int? LockTimeoutMilliseconds { get; set; }

    /// <summary>The <c>idle_in_transaction_session_timeout</c> (milliseconds) of every connection, or <see langword="null"/> for the server default.</summary>
    [Range(1, int.MaxValue)]
    public int? IdleInTransactionSessionTimeoutMilliseconds { get; set; }

    /// <summary>
    /// Enables pgvector's type mapping on the data source (Npgsql's <c>UseVector()</c>), so
    /// <c>Pgvector.Vector</c> values can be read and written through EF Core and Dapper. Off by default.
    /// </summary>
    public bool UseVector { get; set; }

    /// <summary>
    /// Enables Npgsql's reflection-based dynamic JSON mapping (<c>EnableDynamicJson()</c>). Off by default;
    /// not needed for <c>string</c>/<c>JsonDocument</c> values, EF Core's <c>HasJsonbColumn</c> or Dapper's
    /// <c>AddJsonb&lt;T&gt;</c>.
    /// </summary>
    public bool EnableDynamicJson { get; set; }

    /// <summary>
    /// Optional direct connection string (bypassing a transaction-mode pooler such as PgBouncer) for
    /// migrations and session-level advisory locks, which need one server session for their whole
    /// duration. Registered as the keyed data source <c>NpgsqlDataSourceKeys.Migration</c>.
    /// </summary>
    public string? MigrationConnectionString { get; set; }

    /// <summary>
    /// Optional connection string of a read replica, used by read-only Dapper sessions (keyed
    /// <c>NpgsqlDataSourceKeys.ReadOnly</c>). Without it, a multi-host connection string's standby is used
    /// (<c>TargetSessionAttributes=PreferStandby</c>), otherwise the primary.
    /// </summary>
    /// <remarks>Replicas lag: a read-only session may not see the caller's own recent writes.</remarks>
    public string? ReadOnlyConnectionString { get; set; }

    /// <summary>Row-level security settings.</summary>
    public NpgsqlRowLevelSecurityOptions RowLevelSecurity { get; set; } = new();
}
