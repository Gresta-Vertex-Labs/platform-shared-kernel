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

    /// <summary>
    /// Enables pgvector's <c>vector</c>/<c>halfvec</c>/<c>sparsevec</c> type mapping on the shared data
    /// source (Npgsql's ADO-level <c>UseVector()</c>), so <c>Pgvector.Vector</c> values can be read and
    /// written through EF Core and Dapper alike. Off by default.
    /// </summary>
    /// <remarks>
    /// The EF Core setup (<c>UsePostgreSQL(serviceProvider)</c> in <c>SharedKernel.Persistence.EfCore</c>)
    /// turns on its own vector mapping automatically when this is <see langword="true"/>, and refuses to
    /// start when vectors are requested there while this is <see langword="false"/> — the ADO-level
    /// mapping lives on the data source and cannot be added afterwards.
    /// </remarks>
    public bool UseVector { get; set; }

    /// <summary>
    /// Enables Npgsql's dynamic JSON serialization (<c>EnableDynamicJson()</c>): arbitrary CLR types
    /// written to and read from <c>json</c>/<c>jsonb</c> parameters and columns through reflection-based
    /// <c>System.Text.Json</c>. Off by default.
    /// </summary>
    /// <remarks>
    /// Not needed for <c>HasJsonbColumn</c> (which converts through a value converter) or for
    /// <c>string</c>/<c>JsonDocument</c>/<c>JsonElement</c> values. Turn it on only for a service that maps
    /// POCOs to JSON columns directly through Npgsql.
    /// </remarks>
    public bool EnableDynamicJson { get; set; }

    /// <summary>
    /// The value <c>NpgsqlTenantSessionBinder</c> writes to the <c>app.cross_tenant</c> session
    /// setting for an active <see cref="Abstractions.Context.ICrossTenantScope"/>, and the value a
    /// matching row-level security policy's escape clause must compare against — see
    /// <c>RowLevelSecurityMigrationBuilderExtensions.EnableTenantRowLevelSecurity</c>'s
    /// <c>crossTenantEscapeToken</c> parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Defaults to the literal <c>"on"</c> when left unset</strong> — the ORIGINAL, guessable
    /// value, kept only so an existing deployment's migration and running configuration stay in sync
    /// without this property being set at all. <c>"on"</c> gives NO protection against an attacker who
    /// can execute arbitrary SQL as the application's own database role: <c>SELECT
    /// set_config('app.cross_tenant', 'on', false)</c> is an ordinary, unprivileged statement that role
    /// can always run, disabling row-level security on every protected table platform-wide with no
    /// need to go through <see cref="Abstractions.Context.ICrossTenantScope"/> at all.
    /// </para>
    /// <para>
    /// Setting this to a long (32+ character), cryptographically random, per-deployment secret —
    /// provisioned the same way a database credential or an encryption key is, never checked into
    /// source control — closes that gap: an attacker with only SQL execution rights, and no access to
    /// this configuration value, cannot guess it. The SAME value must be passed to
    /// <c>EnableTenantRowLevelSecurity</c> when authoring the migration that creates the policy —
    /// the two are compared against each other, and a mismatch fails closed exactly like a wrong
    /// value would (the escape clause simply never matches).
    /// </para>
    /// </remarks>
    [MinLength(32)]
    public string? CrossTenantEscapeToken { get; set; }
}
