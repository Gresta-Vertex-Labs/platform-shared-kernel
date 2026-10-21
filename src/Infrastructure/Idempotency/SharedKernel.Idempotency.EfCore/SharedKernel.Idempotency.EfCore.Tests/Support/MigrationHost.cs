using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Testing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Idempotency.EfCore.Tests.Support;

/// <summary>
/// A started "host" for the migration tests: the options validators, then every hosted service in registration order,
/// over a real database with the canonical roles. The store runs on the runtime role unless a test says otherwise;
/// migrations run on the migration data source when the configuration carries one.
/// </summary>
internal sealed class MigrationHost : IAsyncDisposable
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    private MigrationHost(ServiceProvider provider, DdlRecorder commands)
    {
        Provider = provider;
        Commands = commands;
    }

    public ServiceProvider Provider { get; }

    public DdlRecorder Commands { get; }

    /// <summary>Builds a host over <paramref name="db"/> that registers the idempotency store with <paramref name="configure"/>.</summary>
    /// <param name="db">The database.</param>
    /// <param name="configure">The package options; <see langword="null"/> is the default (no migration).</param>
    /// <param name="withMigrator">Whether the configuration carries <c>MigrationConnectionString</c>.</param>
    /// <param name="storeConnectionString">The connection of the store's context; the runtime role by default.</param>
    public static MigrationHost Create(
        PostgresTestDatabase db,
        Action<EfCoreIdempotencyOptions>? configure = null,
        bool withMigrator = true,
        string? storeConnectionString = null)
    {
        var storeConnection = storeConnectionString ?? db.RuntimeConnectionString;
        var values = new Dictionary<string, string?> { ["ConnectionStrings:idem"] = storeConnection };
        if (withMigrator)
            values["SharedKernel:Persistence:idem:MigrationConnectionString"] = db.MigratorConnectionString;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var commands = new DdlRecorder();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClock();
        services.AddSharedKernelNpgsql(configuration, "idem");
        services.AddEfCoreIdempotency(
            options =>
            {
                options.UsePostgres(TestNpgsqlDataSources.Get(storeConnection));
                options.AddInterceptors(commands);
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            },
            p => p.ForRequests().ForMessages(),
            configure);

        return new MigrationHost(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), commands);
    }

    public async Task StartAsync()
    {
        Provider.GetService<IStartupValidator>()?.Validate();
        foreach (var hosted in Provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    /// <summary>Runs <paramref name="action"/> with the store of a fresh scope.</summary>
    public async Task<T> WithStoreAsync<T>(Func<IIdempotencyStore, Task<T>> action, IdempotencyPurpose purpose = IdempotencyPurpose.Request)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredIdempotencyStore(purpose));
    }

    public Task<IdempotencyReservation> BeginAsync(string key, string fingerprint = "fp") =>
        WithStoreAsync(s => s.TryBeginAsync(IdempotencyPurpose.Request, key, fingerprint, Ttl, CancellationToken.None));

    public Task<bool> CompleteAsync(string key, string token, string? response) =>
        WithStoreAsync(s => s.CompleteAsync(IdempotencyPurpose.Request, key, token, response, Retention, CancellationToken.None));

    public Task<bool> ReleaseAsync(string key, string token) =>
        WithStoreAsync(s => s.ReleaseAsync(IdempotencyPurpose.Request, key, token, CancellationToken.None));

    public ValueTask DisposeAsync() => Provider.DisposeAsync();

    /// <summary>Records every non-query command of the idempotency context with the role it ran as.</summary>
    internal sealed class DdlRecorder : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<(string? User, string Sql)> _commands = new();

        public IReadOnlyList<(string? User, string Sql)> All => [.. _commands];

        public IReadOnlyList<string?> UsersWhere(string fragment) =>
            [.. _commands.Where(c => c.Sql.Contains(fragment, StringComparison.Ordinal)).Select(c => c.User)];

        public int Count(string fragment) => _commands.Count(c => c.Sql.Contains(fragment, StringComparison.Ordinal));

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command) =>
            _commands.Enqueue((new NpgsqlConnectionStringBuilder(command.Connection!.ConnectionString).Username, command.CommandText));
    }
}

/// <summary>Reads and writes the test database directly, as a given role.</summary>
internal static class Sql
{
    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    public static async Task<int> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>The shape of <c>idempotency_keys</c> as the catalog reports it: columns, constraints, indexes and owner.</summary>
    public static Task<string?> CatalogOfAsync(string adminConnectionString) =>
        ScalarAsync<string>(
            adminConnectionString,
            """
            SELECT
              (SELECT string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '') || ':' || is_nullable || ':' || ordinal_position, ',' ORDER BY ordinal_position)
                 FROM information_schema.columns WHERE table_name = 'idempotency_keys')
              || '|' ||
              (SELECT coalesce(string_agg(conname || '=' || pg_get_constraintdef(oid), ';' ORDER BY conname), '')
                 FROM pg_constraint WHERE conrelid = 'idempotency_keys'::regclass)
              || '|' ||
              (SELECT coalesce(string_agg(indexdef, ';' ORDER BY indexname), '')
                 FROM pg_indexes WHERE tablename = 'idempotency_keys')
              || '|' ||
              (SELECT tableowner FROM pg_tables WHERE tablename = 'idempotency_keys')
            """);

    /// <summary>A digest of every row of <c>idempotency_keys</c>.</summary>
    public static Task<string?> RowsDigestAsync(string adminConnectionString) =>
        ScalarAsync<string>(
            adminConnectionString,
            "SELECT coalesce(md5(string_agg(t::text, '|' ORDER BY tenant_scope, purpose, key)), 'empty') FROM idempotency_keys t");

    public static async Task<long> HistoryCountAsync(string adminConnectionString, string table) =>
        await ScalarAsync<long>(adminConnectionString, $"SELECT count(*) FROM \"{table}\"");

    public static async Task<bool> ExistsAsync(string adminConnectionString, string relation) =>
        await ScalarAsync<string>(adminConnectionString, $"SELECT to_regclass('\"{relation}\"')::text") is not null;
}
