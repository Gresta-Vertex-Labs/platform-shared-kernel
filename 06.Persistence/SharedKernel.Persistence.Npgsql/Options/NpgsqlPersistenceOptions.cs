using System.ComponentModel.DataAnnotations;
using global::Npgsql;
using SharedKernel.Configuration;

namespace SharedKernel.Persistence.Npgsql.Options;

/// <summary>
/// Configuration for the platform's one options-bound <see cref="NpgsqlDataSource"/> per database.
/// </summary>
/// <remarks>
/// <para>
/// Bound from <c>SharedKernel:Persistence:Npgsql</c> (or a caller-supplied named section
/// for a second/keyed database) via
/// <c>AddSharedKernelNpgsql(IServiceCollection, IConfiguration,...)</c> and validated at host
/// startup through <see cref="NpgsqlPersistenceOptionsValidator"/> — never bind this type with a bare
/// <c>services.Configure&lt;NpgsqlPersistenceOptions&gt;(...)</c>.
/// </para>
/// <para>
/// <strong>Secure by default.</strong> <see cref="SslMode"/> defaults to
/// <see cref="global::Npgsql.SslMode.VerifyFull"/> — the connection is refused unless the server
/// presents a certificate chaining to a trusted root AND whose hostname matches. Opting down (e.g.
/// for a local development database with a self-signed certificate) requires BOTH setting
/// <see cref="SslMode"/> to a weaker value AND explicitly setting
/// <see cref="AcknowledgeInsecureSslMode"/> to <see langword="true"/> — the validator rejects a
/// downgraded <see cref="SslMode"/> that has not been explicitly acknowledged, and
/// <c>AddSharedKernelNpgsql</c> logs a <c>Warning</c> once at startup whenever the acknowledgement is
/// exercised, so the opt-down is never silent.
/// </para>
/// <para>
/// <strong><c>Persist Security Info</c> is never configurable through this type</strong> — it is
/// unconditionally forced to <see langword="false"/> on the connection string
/// <c>AddSharedKernelNpgsql</c> builds from <see cref="ConnectionString"/>, regardless of what the
/// supplied connection string says. Leaving it enabled would let the driver echo the
/// password back out of <see cref="NpgsqlConnection.ConnectionString"/> after the connection opens —
/// a needless secret-retention surface this platform never wants.
/// </para>
/// </remarks>
public sealed class NpgsqlPersistenceOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Persistence:Npgsql";

    /// <summary>
    /// The PostgreSQL connection string. Required.
    /// </summary>
    /// <remarks>
    /// Its <c>SSL Mode</c>/<c>Persist Security Info</c> keywords, if present, are overridden by
    /// <see cref="SslMode"/> and the hardcoded <c>Persist Security Info=false</c> respectively —
    /// this property carries host/port/database/credentials, not TLS policy.
    /// </remarks>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The minimum acceptable TLS trust level. Defaults to <see cref="global::Npgsql.SslMode.VerifyFull"/>.
    /// </summary>
    public SslMode SslMode { get; set; } = SslMode.VerifyFull;

    /// <summary>
    /// Must be <see langword="true"/> when <see cref="SslMode"/> is set below
    /// <see cref="global::Npgsql.SslMode.VerifyFull"/> — the explicit, auditable opt-down for local
    /// development against a database with no verifiable certificate. Startup validation fails when
    /// <see cref="SslMode"/> is downgraded without this flag.
    /// </summary>
    public bool AcknowledgeInsecureSslMode { get; set; }

    /// <summary>
    /// The PostgreSQL <c>statement_timeout</c> server setting (milliseconds), applied to every
    /// connection opened from this data source, or <see langword="null"/> to leave the server
    /// default in effect.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? StatementTimeoutMilliseconds { get; set; }

    /// <summary>
    /// The PostgreSQL <c>lock_timeout</c> server setting (milliseconds), applied to every connection
    /// opened from this data source, or <see langword="null"/> to leave the server default in effect.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? LockTimeoutMilliseconds { get; set; }

    /// <summary>
    /// The PostgreSQL <c>idle_in_transaction_session_timeout</c> server setting (milliseconds),
    /// applied to every connection opened from this data source, or <see langword="null"/> to leave
    /// the server default in effect.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? IdleInTransactionSessionTimeoutMilliseconds { get; set; }
}
