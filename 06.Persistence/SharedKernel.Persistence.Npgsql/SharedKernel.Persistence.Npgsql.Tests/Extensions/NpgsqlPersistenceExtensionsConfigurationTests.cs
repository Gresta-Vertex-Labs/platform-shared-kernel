using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Extensions;

/// <summary>
/// <c>AddSharedKernelNpgsql(IServiceCollection, IConfiguration,...)</c> against a real
/// PostgreSQL Testcontainer: options binding/validation, server-side timeout effectiveness, and the
/// <see cref="IMigrationLock"/>/<see cref="ITenantSessionBinder"/> registrations it adds.
/// </summary>
public sealed class NpgsqlPersistenceExtensionsConfigurationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public void AddSharedKernelNpgsql_Configuration_RegistersIMigrationLockAndITenantSessionBinder()
    {
        var provider = BuildProvider(statementTimeoutMs: null);

        provider.GetService<IMigrationLock>().Should().NotBeNull();
        provider.GetService<ITenantSessionBinder>().Should().NotBeNull();
    }

    [Fact]
    public async Task AddSharedKernelNpgsql_Configuration_DefaultSslMode_RejectsANonTlsServer()
    {
        // The Testcontainers PostgreSQL image has no TLS certificate configured, so the secure
        // VerifyFull default must genuinely refuse the connection rather than silently
        // downgrading — this is the real-world proof the opt-down mechanism exists for.
        var provider = BuildProviderWithDefaultSsl();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        var act = async () => await dataSource.OpenConnectionAsync();

        await act.Should().ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public void AddSharedKernelNpgsql_Configuration_MissingConnectionString_ThrowsAtValidateOnStart()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Persistence:Npgsql:ConnectionString"] = string.Empty,
            })
                .Build();

        services.AddSharedKernelNpgsql(configuration);
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<NpgsqlDataSource>();

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
    }

    [Fact]
    public async Task AddSharedKernelNpgsql_Configuration_StatementTimeout_IsEffectiveOnTheServer()
    {
        var provider = BuildProvider(statementTimeoutMs: 5000);
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW statement_timeout";
        var value = (string?)await command.ExecuteScalarAsync();

        value.Should().Be("5s");
    }

    [Fact]
    public async Task AddSharedKernelNpgsql_Configuration_LockTimeout_IsEffectiveOnTheServer()
    {
        var provider = BuildProvider(statementTimeoutMs: null, lockTimeoutMs: 2500);
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW lock_timeout";
        var value = (string?)await command.ExecuteScalarAsync();

        value.Should().Be("2500ms");
    }

    [Fact]
    public async Task AddSharedKernelNpgsql_Configuration_PersistSecurityInfo_IsAlwaysForcedFalse()
    {
        // Deliberately request Persist Security Info=true in the raw connection string — the
        // registered data source must override it to false regardless.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Persistence:Npgsql:ConnectionString"] =
                    _fixture.ConnectionString + ";Persist Security Info=true",
                // The Testcontainers PostgreSQL image has no TLS certificate configured — every real
                // connection-opening test in this file must explicitly opt down from the secure
                // VerifyFull default, which is exactly what AcknowledgeInsecureSslMode exists for.
                ["SharedKernel:Persistence:Npgsql:SslMode"] = "Disable",
                ["SharedKernel:Persistence:Npgsql:AcknowledgeInsecureSslMode"] = "true",
            })
                .Build();

        services.AddSharedKernelNpgsql(configuration);
        var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        dataSource.ConnectionString.Should().Contain("Persist Security Info=False");

        await using var connection = await dataSource.OpenConnectionAsync();
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    private ServiceProvider BuildProvider(int? statementTimeoutMs, int? lockTimeoutMs = null)
    {
        var services = new ServiceCollection();

        var settings = new Dictionary<string, string?>
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = _fixture.ConnectionString,
            // See the comment in AddSharedKernelNpgsql_Configuration_PersistSecurityInfo_IsAlwaysForcedFalse.
            ["SharedKernel:Persistence:Npgsql:SslMode"] = "Disable",
            ["SharedKernel:Persistence:Npgsql:AcknowledgeInsecureSslMode"] = "true",
        };

        if (statementTimeoutMs is not null)
            settings["SharedKernel:Persistence:Npgsql:StatementTimeoutMilliseconds"] = statementTimeoutMs.Value.ToString();

        if (lockTimeoutMs is not null)
            settings["SharedKernel:Persistence:Npgsql:LockTimeoutMilliseconds"] = lockTimeoutMs.Value.ToString();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        services.AddSharedKernelNpgsql(configuration);
        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildProviderWithDefaultSsl()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Persistence:Npgsql:ConnectionString"] = _fixture.ConnectionString,
            })
                .Build();

        services.AddSharedKernelNpgsql(configuration);
        return services.BuildServiceProvider();
    }
}
