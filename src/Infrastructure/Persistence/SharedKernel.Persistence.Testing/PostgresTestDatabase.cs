using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SharedKernel.Persistence.EfCore;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// One database on a <see cref="PostgresTestServer"/>, owned by <see cref="PostgresTestRoles.Migrator"/>, with a
/// connection string per canonical role and the configuration <c>AddSharedKernelPostgres</c> reads.
/// </summary>
/// <remarks>
/// <para>
/// Create the schema as the migrator, as production does: let <c>MigrateOnStartup()</c> run your migrations (the
/// configuration from <see cref="ConfigurationFor"/> sets <c>MigrationConnectionString</c>), or call
/// <see cref="CreateSchemaAsync"/> for a model without migrations. The runtime role cannot create tables, so
/// <c>Database.EnsureCreated()</c> on a context connected as the application fails — by design.
/// </para>
/// <para>
/// Then apply what your migrations would: <see cref="EnableRowLevelSecurityAsync"/>, <see cref="CreateAuditLedgerAsync"/>,
/// <see cref="CreateTenantEncryptionKeyTableAsync"/>, or any <c>MigrationBuilder</c> call through
/// <see cref="ApplyMigrationAsync"/>. Disposing the database drops it.
/// </para>
/// </remarks>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    /// <summary>The data-source name <see cref="ConfigurationFor"/> gives the audit sealer.</summary>
    public const string AuditSealerDataSourceName = "audit-sealer";

    private readonly PostgresTestServer _server;

    internal PostgresTestDatabase(PostgresTestServer server, string name)
    {
        _server = server;
        Name = name;
    }

    /// <summary>Gets the database name.</summary>
    public string Name { get; }

    /// <summary>Gets a superuser connection string for this database (extensions, inspection).</summary>
    public string AdminConnectionString => _server.ConnectionString(Name, role: null);

    /// <summary>Gets the connection string of <see cref="PostgresTestRoles.Migrator"/>, the owner of every table.</summary>
    public string MigratorConnectionString => _server.ConnectionString(Name, PostgresTestRoles.Migrator);

    /// <summary>Gets the connection string of <see cref="PostgresTestRoles.Runtime"/>, the application's login.</summary>
    public string RuntimeConnectionString => _server.ConnectionString(Name, PostgresTestRoles.Runtime);

    /// <summary>Gets the connection string of <see cref="PostgresTestRoles.CrossTenant"/> (BYPASSRLS).</summary>
    public string CrossTenantConnectionString => _server.ConnectionString(Name, PostgresTestRoles.CrossTenant);

    /// <summary>Gets the connection string of <see cref="PostgresTestRoles.AuditSealer"/>.</summary>
    public string AuditSealerConnectionString => _server.ConnectionString(Name, PostgresTestRoles.AuditSealer);

    /// <summary>
    /// The configuration of connection <paramref name="connectionName"/> as <c>AddSharedKernelPostgres</c> and
    /// <c>AddSharedKernelNpgsql</c> read it: <c>ConnectionStrings:{name}</c> (runtime role),
    /// <c>SharedKernel:Persistence:{name}:MigrationConnectionString</c> (migrator) and
    /// <c>SharedKernel:Persistence:{name}:RowLevelSecurity:CrossTenantConnectionString</c> (cross-tenant role).
    /// </summary>
    /// <param name="connectionName">The connection name passed to <c>AddSharedKernelPostgres</c>.</param>
    /// <param name="separateAuditSealer">
    /// Also configure the audit sealer on its own role: <c>SharedKernel:Persistence:Auditing:Sealer:DataSourceName</c>
    /// = <see cref="AuditSealerDataSourceName"/> and that data source's <c>ConnectionString</c>. The service then
    /// registers it with <c>AddSharedKernelNpgsql(configuration.GetSection("SharedKernel:Persistence:audit-sealer"),
    /// "audit-sealer")</c>, as in production.
    /// </param>
    /// <returns>Configuration keys and values; add them to an in-memory configuration source.</returns>
    public IReadOnlyDictionary<string, string?> ConfigurationFor(string connectionName, bool separateAuditSealer = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var section = $"SharedKernel:Persistence:{connectionName}";
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"ConnectionStrings:{connectionName}"] = RuntimeConnectionString,
            [$"{section}:MigrationConnectionString"] = MigratorConnectionString,
            [$"{section}:RowLevelSecurity:CrossTenantConnectionString"] = CrossTenantConnectionString,
        };

        if (separateAuditSealer)
        {
            values["SharedKernel:Persistence:Auditing:Sealer:DataSourceName"] = AuditSealerDataSourceName;
            values[$"SharedKernel:Persistence:{AuditSealerDataSourceName}:ConnectionString"] = AuditSealerConnectionString;
        }

        return values;
    }

    /// <summary>Builds an <see cref="IConfiguration"/> from <see cref="ConfigurationFor"/> plus <paramref name="additional"/> values.</summary>
    /// <param name="connectionName">The connection name.</param>
    /// <param name="additional">Further keys (they win over the generated ones).</param>
    /// <param name="separateAuditSealer">See <see cref="ConfigurationFor"/>.</param>
    /// <returns>The configuration.</returns>
    public IConfiguration BuildConfiguration(
        string connectionName,
        IEnumerable<KeyValuePair<string, string?>>? additional = null,
        bool separateAuditSealer = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(ConfigurationFor(connectionName, separateAuditSealer))
            .AddInMemoryCollection(additional ?? [])
            .Build();

    /// <summary>Runs <paramref name="sql"/> as the migrator (the table owner).</summary>
    /// <param name="sql">The statements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the statements ran.</returns>
    public Task ExecuteAsMigratorAsync(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return PostgresTestServer.ExecuteAsync(MigratorConnectionString, sql, cancellationToken);
    }

    /// <summary>Runs <paramref name="sql"/> as the superuser (for example <c>CREATE EXTENSION vector</c>).</summary>
    /// <param name="sql">The statements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the statements ran.</returns>
    public Task ExecuteAsAdminAsync(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return PostgresTestServer.ExecuteAsync(AdminConnectionString, sql, cancellationToken);
    }

    /// <summary>
    /// Applies <paramref name="migration"/> as the migrator, exactly as <c>dotnet ef database update</c> would apply
    /// the same calls in a migration's <c>Up</c> — any <see cref="MigrationBuilder"/> operation, including the
    /// platform's helpers.
    /// </summary>
    /// <param name="migration">Builds the operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every statement ran.</returns>
    public async Task ApplyMigrationAsync(Action<MigrationBuilder> migration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migration);

        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration(builder);
        if (builder.Operations.Count == 0)
            return;

        var options = new DbContextOptionsBuilder().UseNpgsql(MigratorConnectionString).Options;
        await using var context = new DbContext(options);
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(builder.Operations);

        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var command in commands)
        {
            await using var sql = new NpgsqlCommand(command.CommandText, connection);
            await sql.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates the tables of <paramref name="context"/>'s model as the migrator — the equivalent of
    /// <c>EnsureCreated</c> for a model without migrations, with the ownership production has.
    /// </summary>
    /// <param name="context">A context of the model (its own connection is not used).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the tables exist.</returns>
    public Task CreateSchemaAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteAsMigratorAsync(context.Database.GenerateCreateScript(), cancellationToken);
    }

    /// <summary>
    /// Enables row-level security with the tenant policy on every tenant table of <paramref name="context"/>'s model
    /// (<c>EnableTenantRowLevelSecurityForModel</c>), as the migrator.
    /// </summary>
    /// <param name="context">A context of the model.</param>
    /// <param name="crossTenantRole">
    /// A NOBYPASSRLS cross-tenant role that gets its own policy, or <see langword="null"/> (the test server's
    /// <see cref="PostgresTestRoles.CrossTenant"/> has BYPASSRLS and needs none).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the policies exist.</returns>
    public Task EnableRowLevelSecurityAsync(DbContext context, string? crossTenantRole = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyModel model;
        try
        {
            model = context.GetService<IDesignTimeModel>().Model;
        }
        catch (InvalidOperationException)
        {
            model = context.Model;
        }

        return ApplyMigrationAsync(m => m.EnableTenantRowLevelSecurityForModel(model, crossTenantRole), cancellationToken);
    }

    /// <summary>
    /// Creates the audit ledger with the grants of the canonical role script: <c>CreateAuditLedgerTable</c> for the
    /// runtime role (and the sealer role), then revokes writes from the cross-tenant role.
    /// </summary>
    /// <param name="separateAuditSealer">Grant the link and checkpoint tables to <see cref="PostgresTestRoles.AuditSealer"/> instead of the runtime role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the ledger exists.</returns>
    public Task CreateAuditLedgerAsync(bool separateAuditSealer = false, CancellationToken cancellationToken = default) =>
        ApplyMigrationAsync(
            m =>
            {
                m.CreateAuditLedgerTable(
                    runtimeRole: PostgresTestRoles.Runtime,
                    sealerRole: separateAuditSealer ? PostgresTestRoles.AuditSealer : null);
                m.Sql(
                    "REVOKE UPDATE, DELETE, TRUNCATE ON audit_records, audit_record_payloads, audit_chain_links, audit_checkpoints "
                    + $"FROM {PostgresTestRoles.CrossTenant};");
            },
            cancellationToken);

    /// <summary>
    /// Creates the tenant data-key table of field encryption (<c>CreateTenantEncryptionKeyTable</c>) and, as the role
    /// script does, makes its tombstones undeletable for the runtime and cross-tenant roles.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the table exists.</returns>
    public Task CreateTenantEncryptionKeyTableAsync(CancellationToken cancellationToken = default) =>
        ApplyMigrationAsync(
            m =>
            {
                m.CreateTenantEncryptionKeyTable();
                m.Sql($"REVOKE DELETE, TRUNCATE ON sk_tenant_encryption_keys FROM {PostgresTestRoles.Runtime}, {PostgresTestRoles.CrossTenant};");
            },
            cancellationToken);

    /// <summary>Drops the database, closing every connection to it.</summary>
    /// <returns>A task that completes when the database is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        await PostgresTestServer.ExecuteAsync(
                _server.ConnectionString("postgres", role: null),
                $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE);",
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    internal static void ValidateIdentifier(string value, string parameterName)
    {
        if (value.Length is 0 or > 63 || !value.All(c => c is '_' or (>= 'a' and <= 'z') or (>= '0' and <= '9')) || char.IsDigit(value[0]))
            throw new ArgumentException("Use a plain lowercase identifier: letters, digits and '_', at most 63 characters.", parameterName);
    }
}
