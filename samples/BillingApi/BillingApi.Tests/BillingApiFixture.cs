using System.Net;
using BillingApi.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SharedKernel.Persistence.Testing;
using Xunit;

namespace BillingApi.Tests;

/// <summary>
/// One PostgreSQL (Testcontainers) with the production role split, one database, one running BillingApi — exactly
/// as deployed: the Production environment (every startup check fails closed), migrations applied at startup by
/// <c>app_migrator</c>, the service connected as <c>app_runtime</c>, the audit sealer as <c>app_audit_sealer</c>.
/// </summary>
public sealed class BillingApiFixture : IAsyncLifetime
{
    // Test-only key material.
    private static readonly Dictionary<string, string?> Keys = new()
    {
        ["SharedKernel:Persistence:Auditing:Keys:k1:Material"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray()),
        ["SharedKernel:Persistence:Encryption:Keys:Keys:root-1"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, 32).ToArray()),
        ["SharedKernel:Persistence:Encryption:BlindIndexKeys:Keys:v1"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x33, 32).ToArray()),
        ["Billing:MasterKey:Material"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x44, 32).ToArray()),
    };

    private PostgresTestServer _server = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public PostgresTestDatabase Database { get; private set; } = null!;

    public IServiceProvider Services => _factory.Services;

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.StartAsync("postgres:17-alpine");
        Database = await _server.CreateDatabaseAsync();

        var configuration = new Dictionary<string, string?>(Database.ConfigurationFor("billing", separateAuditSealer: true));
        foreach (var (key, value) in Keys)
            configuration[key] = value;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Production");
            host.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
        });

        // Starting the host runs the migrations, the seeders, the RLS privilege and coverage checks and the audit
        // self-check; readiness turns 200 once they are done.
        using var client = _factory.CreateClient();
        for (var attempt = 0; ; attempt++)
        {
            using var ready = await client.GetAsync("/health/ready");
            if (ready.StatusCode == HttpStatusCode.OK)
                break;
            if (attempt == 120)
                throw new InvalidOperationException($"BillingApi never became ready: {await ready.Content.ReadAsStringAsync()}");
            await Task.Delay(250);
        }
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Database.DisposeAsync();
        await _server.DisposeAsync();
    }

    /// <summary>A client acting as <paramref name="user"/> of <paramref name="tenant"/> with <paramref name="permissions"/>.</summary>
    public HttpClient Client(string user, Guid? tenant, params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DemoHeaders.User, user);
        if (tenant is { } id)
            client.DefaultRequestHeaders.Add(DemoHeaders.Tenant, id.ToString());
        if (permissions.Length > 0)
            client.DefaultRequestHeaders.Add(DemoHeaders.Permissions, string.Join(',', permissions));
        return client;
    }

    public HttpClient Anonymous() => _factory.CreateClient();

    /// <summary>Reads the database as the superuser, bypassing row-level security — what an attacker with the disk would see.</summary>
    public async Task<T?> ScalarAsAdminAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Database.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }
}

[CollectionDefinition(Name)]
public sealed class BillingApiCollection : ICollectionFixture<BillingApiFixture>
{
    public const string Name = "BillingApi";
}
