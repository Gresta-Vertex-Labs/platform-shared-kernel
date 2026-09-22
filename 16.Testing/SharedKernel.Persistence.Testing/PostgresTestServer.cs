using Npgsql;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// A PostgreSQL server for tests — a Testcontainers container, or a server that already runs (a CI service
/// container) — with the canonical roles provisioned (<see cref="PostgresTestRoles"/>). Hands out one database per
/// test or test class through <see cref="CreateDatabaseAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Framework-neutral: wrap it in the test framework's shared fixture (an xUnit collection fixture, an NUnit
/// <c>OneTimeSetUp</c>) and start one server per test run, not per test.
/// </para>
/// <para>
/// The roles are server-wide and created once, idempotently. The administrator login must be a superuser (the
/// container's is), because creating a <c>BYPASSRLS</c> role requires one.
/// </para>
/// </remarks>
public sealed class PostgresTestServer : IAsyncDisposable
{
    /// <summary>The default image. Use <c>pgvector/pgvector:pg16</c> for vector columns.</summary>
    public const string DefaultImage = "postgres:16.4";

    private readonly PostgreSqlContainer? _container;
    private readonly SemaphoreSlim _rolesLock = new(1, 1);
    private bool _rolesProvisioned;

    private PostgresTestServer(PostgreSqlContainer? container, string adminConnectionString)
    {
        _container = container;
        AdminConnectionString = adminConnectionString;
    }

    /// <summary>Gets the superuser connection string (database <c>postgres</c>).</summary>
    public string AdminConnectionString { get; }

    /// <summary>Starts a PostgreSQL container.</summary>
    /// <param name="image">The image; <see cref="DefaultImage"/> by default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The started server; dispose it to remove the container.</returns>
    public static async Task<PostgresTestServer> StartAsync(string image = DefaultImage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var container = new PostgreSqlBuilder(image)
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await container.StartAsync(cancellationToken).ConfigureAwait(false);
        return new PostgresTestServer(container, container.GetConnectionString());
    }

    /// <summary>Uses a server that already runs; <see cref="DisposeAsync"/> leaves it running.</summary>
    /// <param name="adminConnectionString">A superuser connection string.</param>
    /// <returns>The server.</returns>
    public static PostgresTestServer FromExistingServer(string adminConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminConnectionString);
        return new PostgresTestServer(null, adminConnectionString);
    }

    /// <summary>
    /// Creates a database owned by <see cref="PostgresTestRoles.Migrator"/>, with the grants and default privileges
    /// of the canonical role script, and returns its connection strings.
    /// </summary>
    /// <param name="name">A plain identifier (letters, digits, <c>_</c>); a unique name by default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The database; dispose it to drop it.</returns>
    public async Task<PostgresTestDatabase> CreateDatabaseAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        name ??= $"sk_test_{Guid.NewGuid():N}";
        PostgresTestDatabase.ValidateIdentifier(name, nameof(name));

        await EnsureRolesAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(AdminConnectionString, $"CREATE DATABASE \"{name}\" OWNER {PostgresTestRoles.Migrator};", cancellationToken)
            .ConfigureAwait(false);

        var database = new PostgresTestDatabase(this, name);
        await ExecuteAsync(
                database.AdminConnectionString,
                $"""
                GRANT CONNECT ON DATABASE "{name}" TO {PostgresTestRoles.Runtime}, {PostgresTestRoles.CrossTenant}, {PostgresTestRoles.AuditSealer};
                GRANT USAGE ON SCHEMA public TO {PostgresTestRoles.Runtime}, {PostgresTestRoles.CrossTenant}, {PostgresTestRoles.AuditSealer};
                ALTER DEFAULT PRIVILEGES FOR ROLE {PostgresTestRoles.Migrator} IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {PostgresTestRoles.Runtime}, {PostgresTestRoles.CrossTenant};
                ALTER DEFAULT PRIVILEGES FOR ROLE {PostgresTestRoles.Migrator} IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO {PostgresTestRoles.Runtime}, {PostgresTestRoles.CrossTenant};
                """,
                cancellationToken)
            .ConfigureAwait(false);

        return database;
    }

    /// <summary>Stops and removes the container (a server passed to <see cref="FromExistingServer"/> keeps running).</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        _rolesLock.Dispose();
        if (_container is not null)
            await _container.DisposeAsync().ConfigureAwait(false);
    }

    internal string ConnectionString(string database, string? role)
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = database };
        if (role is not null)
        {
            builder.Username = role;
            builder.Password = role;
        }

        return builder.ConnectionString;
    }

    internal static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        await _rolesLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_rolesProvisioned)
                return;

            await ExecuteAsync(
                    AdminConnectionString,
                    $"""
                    DO $$
                    BEGIN
                        {CreateRole(PostgresTestRoles.Migrator, "NOSUPERUSER")}
                        {CreateRole(PostgresTestRoles.Runtime, "NOSUPERUSER NOBYPASSRLS")}
                        {CreateRole(PostgresTestRoles.CrossTenant, "NOSUPERUSER BYPASSRLS")}
                        {CreateRole(PostgresTestRoles.AuditSealer, "NOSUPERUSER NOBYPASSRLS")}
                    END
                    $$;
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            _rolesProvisioned = true;
        }
        finally
        {
            _rolesLock.Release();
        }

        static string CreateRole(string role, string attributes) =>
            $"IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{role}') THEN "
            + $"CREATE ROLE {role} LOGIN PASSWORD '{role}' {attributes}; END IF;";
    }
}
