using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel.Execution.Context;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Support;

[CollectionDefinition("AuditPostgres")]
public sealed class AuditPostgresCollection : ICollectionFixture<PostgreSqlContainerFixture>;

/// <summary>A settable request context, local to these tests.</summary>
public sealed class TestRequestContext : IRequestContext
{
    public static readonly Guid TenantA = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = new("bbbbbbbb-0000-0000-0000-000000000002");

    public bool IsAuthenticated { get; set; } = true;
    public string? UserId { get; set; } = "user-1";
    public Guid? TenantId { get; set; } = TenantA;
    public ActorKind ActorKind { get; set; } = ActorKind.User;
    public string? ClientId { get; set; }
    public string? SessionId { get; set; }
    public string? ImpersonatorId { get; set; }

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}

/// <summary>A cross-tenant scope whose state a test sets directly.</summary>
public sealed class TestCrossTenantScope : ICrossTenantScope
{
    public bool IsActive { get; set; }

    public IDisposable Enter(string reason)
    {
        IsActive = true;
        return new Exit(this);
    }

    private sealed class Exit(TestCrossTenantScope scope) : IDisposable
    {
        public void Dispose() => scope.IsActive = false;
    }
}

/// <summary>An ambient transaction a test opens and publishes by hand.</summary>
public sealed class TestAmbientTransaction : IAmbientDbTransaction
{
    public (DbConnection Connection, DbTransaction Transaction)? Current { get; set; }
}

/// <summary>Opens plain connections to one test database.</summary>
public sealed class TestConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public string ConnectionString => connectionString;

    public async Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

/// <summary>A clock a test can move.</summary>
public sealed class TestClock : IClock
{
    private DateTimeOffset _fixed = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets a value indicating whether the clock follows the system clock instead of a fixed time.</summary>
    public bool Live { get; set; }

    public DateTimeOffset UtcNow
    {
        get => Live ? DateTimeOffset.UtcNow : _fixed;
        set => _fixed = value;
    }

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

/// <summary>Creates a fresh database with the ledger schema for each test.</summary>
public static class LedgerTestDatabase
{
    public static async Task<string> CreateAsync(PostgreSqlContainerFixture fixture, bool withLedger = true)
    {
        var name = "sk_audit_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE {name}";
            await create.ExecuteNonQueryAsync();
        }

        // Unpooled: every test has its own database, and pooled idle connections per database would exhaust max_connections.
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        if (withLedger)
            await ExecuteAsync(connectionString, AuditLedgerSchema.CreateScript);
        return connectionString;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Runs <paramref name="sql"/> as the superuser with the ledger's triggers disabled — simulates an attacker with DDL rights.</summary>
    public static Task TamperAsync(string connectionString, string sql) =>
        ExecuteAsync(connectionString,
            $"""
            ALTER TABLE {AuditLedgerSchema.RecordsTable} DISABLE TRIGGER USER;
            ALTER TABLE {AuditLedgerSchema.PayloadsTable} DISABLE TRIGGER USER;
            ALTER TABLE {AuditLedgerSchema.LinksTable} DISABLE TRIGGER USER;
            ALTER TABLE {AuditLedgerSchema.CheckpointsTable} DISABLE TRIGGER USER;
            {sql};
            {AuditLedgerSchema.CreateScript}
            """);
}

/// <summary>Keys used across the tests.</summary>
public static class TestKeys
{
    public static readonly string K1 = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray());
    public static readonly string K2 = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, 32).ToArray());
}

/// <summary>Options for <see cref="LedgerHost.Build"/>.</summary>
public sealed class LedgerHostOptions
{
    public Dictionary<string, (string Material, int Order)> Keys { get; } = new() { ["k1"] = (TestKeys.K1, 1) };
    public string CurrentKeyId { get; set; } = "k1";
    public string? CheckpointSigningKeyId { get; set; }
    public List<string> AcceptedCheckpointSigningKeyIds { get; } = [];
    public int BatchSize { get; set; } = 500;
    public bool SealerEnabled { get; set; }
    public string? SealerDataSourceName { get; set; }
    public TimeSpan CheckpointInterval { get; set; } = TimeSpan.FromHours(1);
    public ISigningKeyProvider? SigningKeys { get; set; }
    public Action<IServiceCollection>? ConfigureServices { get; set; }
}

/// <summary>A DI container with the ledger registered against a test database (no host, sealer driven by hand).</summary>
public sealed class LedgerHost : IAsyncDisposable
{
    private LedgerHost(ServiceProvider provider, string connectionString, TestRequestContext context, TestCrossTenantScope scope, TestAmbientTransaction ambient, TestClock clock)
    {
        Provider = provider;
        ConnectionString = connectionString;
        Context = context;
        Scope = scope;
        Ambient = ambient;
        Clock = clock;
    }

    public ServiceProvider Provider { get; }
    public string ConnectionString { get; }
    public TestRequestContext Context { get; }
    public TestCrossTenantScope Scope { get; }
    public TestAmbientTransaction Ambient { get; }
    public TestClock Clock { get; }

    public static LedgerHost Build(string connectionString, Action<LedgerHostOptions>? configure = null)
    {
        var options = new LedgerHostOptions();
        configure?.Invoke(options);

        var values = new Dictionary<string, string?>
        {
            [$"{AuditLedgerOptions.SectionName}:CurrentKeyId"] = options.CurrentKeyId,
            [$"{AuditLedgerOptions.SectionName}:Sealer:Enabled"] = options.SealerEnabled ? "true" : "false",
            [$"{AuditLedgerOptions.SectionName}:Sealer:Interval"] = "00:00:00.100",
            [$"{AuditLedgerOptions.SectionName}:Sealer:CheckpointInterval"] = options.CheckpointInterval.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            [$"{AuditLedgerOptions.SectionName}:Sealer:BatchSize"] = options.BatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{AuditLedgerOptions.SectionName}:SelfCheck"] = "Off",
            [$"{AuditLedgerOptions.SectionName}:CheckpointSigningKeyId"] = options.CheckpointSigningKeyId,
            [$"{AuditLedgerOptions.SectionName}:Sealer:DataSourceName"] = options.SealerDataSourceName,
        };
        foreach (var (id, key) in options.Keys)
        {
            values[$"{AuditLedgerOptions.SectionName}:Keys:{id}:Material"] = key.Material;
            values[$"{AuditLedgerOptions.SectionName}:Keys:{id}:Order"] = key.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        for (var i = 0; i < options.AcceptedCheckpointSigningKeyIds.Count; i++)
            values[$"{AuditLedgerOptions.SectionName}:AcceptedCheckpointSigningKeyIds:{i}"] = options.AcceptedCheckpointSigningKeyIds[i];

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var context = new TestRequestContext();
        var scope = new TestCrossTenantScope();
        var ambient = new TestAmbientTransaction();
        var clock = new TestClock();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IRequestContext>(context);
        services.AddSingleton<ICrossTenantScope>(scope);
        services.AddSingleton<IAmbientDbTransaction>(ambient);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IDbConnectionFactory>(new TestConnectionFactory(connectionString));
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        if (options.SigningKeys is { } signingKeys)
        {
            services.AddSingleton(signingKeys);
            services.AddSingleton<IAsymmetricSignatureService, AsymmetricSignatureService>();
        }

        options.ConfigureServices?.Invoke(services);
        services.AddSharedKernelAuditLedger(configuration);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return new LedgerHost(provider, connectionString, context, scope, ambient, clock);
    }

    public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var serviceScope = Provider.CreateAsyncScope();
        return await action(serviceScope.ServiceProvider);
    }

    public ValueTask DisposeAsync() => Provider.DisposeAsync();
}
