using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Extensions;

/// <summary>
/// <c>AddSharedKernelNpgsql</c> against a real PostgreSQL: <c>ConnectionStrings:{name}</c>, the TLS mode of the
/// connection string, eager validation, the keyed secondary data sources, and the row-level security
/// privilege check at startup.
/// </summary>
public sealed class NpgsqlRegistrationTests : IAsyncLifetime
{
    private const string AppRole = "sk_reg_app";
    private const string AppPassword = "sk_reg_app_pw";

    private readonly PostgreSqlContainerFixture _fixture = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var command = dataSource.CreateCommand($"""
            CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}';
            CREATE TABLE reg_owned (id int);
            ALTER TABLE reg_owned ENABLE ROW LEVEL SECURITY;
            """);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ConnectionStringName_ReadsConnectionStrings_AndHonoursItsSslModeForLoopback()
    {
        // No SslMode option and no acknowledgement: the connection string's own "SSL Mode=Disable" is
        // honoured, and allowed because the container host is on this machine.
        var provider = Build(new()
        {
            ["ConnectionStrings:orders"] = _fixture.ConnectionString + ";SSL Mode=Disable",
        }, connectionStringName: "orders");

        provider.GetRequiredService<IStartupValidator>().Validate();

        await using var connection = await provider.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public void MissingConnectionString_FailsTheStartupValidation()
    {
        var provider = Build(new(), connectionStringName: "missing");

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionStrings:missing*");
    }

    [Fact]
    public void RowLevelSecurity_WithMultiplexing_FailsTheStartupValidation()
    {
        var provider = Build(new()
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = _fixture.ConnectionString + ";SSL Mode=Disable;Multiplexing=true",
            ["SharedKernel:Persistence:Npgsql:RowLevelSecurity:Enabled"] = "true",
        });

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*Multiplexing*");
    }

    [Fact]
    public async Task SecondaryDataSources_AreAbsentUntilConfigured_AndReadOnlyFallsBackToThePrimary()
    {
        var provider = Build(new()
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = _fixture.ConnectionString + ";SSL Mode=Disable",
        });

        provider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.ReadOnly).Should().BeNull();
        provider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.CrossTenant).Should().BeNull();
        provider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.Migration).Should().BeNull();
        provider.GetKeyedService<IDbConnectionFactory>(NpgsqlDataSourceKeys.CrossTenant).Should().BeNull();

        await using var readOnly = await provider
            .GetRequiredKeyedService<IDbConnectionFactory>(NpgsqlDataSourceKeys.ReadOnly)
            .CreateConnectionAsync();
        readOnly.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public void ReadOnly_OnAMultiHostConnectionString_UsesAPreferStandbyDataSource()
    {
        var provider = Build(new()
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] =
                "Host=localhost:5432,127.0.0.1:5433;Database=x;Username=u;Password=p;SSL Mode=Disable",
        });

        provider.GetRequiredService<NpgsqlDataSource>().Should().BeAssignableTo<NpgsqlMultiHostDataSource>();
        provider.GetRequiredKeyedService<IDbConnectionFactory>(NpgsqlDataSourceKeys.ReadOnly).Should().NotBeNull();
    }

    [Fact]
    public async Task MigrationConnectionString_IsTheDataSourceOfTheMigrationLock()
    {
        var provider = Build(new()
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = _fixture.ConnectionString + ";SSL Mode=Disable",
            ["SharedKernel:Persistence:Npgsql:MigrationConnectionString"] =
                _fixture.ConnectionString + ";SSL Mode=Disable;Application Name=sk-migrator",
        });

        await using var handle = await provider.GetRequiredService<IMigrationLock>()
            .AcquireAsync("registration-test", TimeSpan.FromSeconds(5));

        await using var command = provider.GetRequiredService<NpgsqlDataSource>().CreateCommand(
            "SELECT count(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid "
                + "WHERE l.locktype = 'advisory' AND a.application_name = 'sk-migrator'");
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(1);
    }

    [Fact]
    public async Task CrossTenantConnectionString_RegistersTheKeyedDataSourceAndFactory()
    {
        var provider = Build(new()
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = _fixture.ConnectionString + ";SSL Mode=Disable",
            ["SharedKernel:Persistence:Npgsql:RowLevelSecurity:CrossTenantConnectionString"] =
                _fixture.ConnectionString + ";SSL Mode=Disable",
        });

        provider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.CrossTenant).Should().NotBeNull();
        await using var connection = await provider
            .GetRequiredKeyedService<IDbConnectionFactory>(NpgsqlDataSourceKeys.CrossTenant)
            .CreateConnectionAsync();
        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    // A3: a superuser (the container's own user) silently bypasses every policy.
    [Fact]
    public async Task PrivilegeCheck_Superuser_FailsTheHostStart()
    {
        var provider = BuildWithRowLevelSecurity(_fixture.ConnectionString, privilegeCheck: null);

        var act = () => StartHostedServicesAsync(provider);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*superuser*");
    }

    [Fact]
    public async Task PrivilegeCheck_Superuser_InWarnMode_Starts()
    {
        var provider = BuildWithRowLevelSecurity(_fixture.ConnectionString, privilegeCheck: "Warn");

        var act = () => StartHostedServicesAsync(provider);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PrivilegeCheck_UnprivilegedRole_Starts()
    {
        var provider = BuildWithRowLevelSecurity(AppConnectionString(), privilegeCheck: null);

        var act = () => StartHostedServicesAsync(provider);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PrivilegeCheck_OwnerOfAnRlsTable_IsReported()
    {
        await using (var admin = NpgsqlDataSource.Create(_fixture.ConnectionString))
        await using (var command = admin.CreateCommand($"CREATE ROLE sk_reg_owner LOGIN PASSWORD 'pw'; ALTER TABLE reg_owned OWNER TO sk_reg_owner;"))
            await command.ExecuteNonQueryAsync();

        var ownerConnectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Username = "sk_reg_owner",
            Password = "pw",
        }.ConnectionString;

        await using var dataSource = NpgsqlDataSource.Create(ownerConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        var report = await RowLevelSecurityPrivileges.CheckAsync(connection);

        report.IsSubjectToRowLevelSecurity.Should().BeFalse();
        report.OwnedRowLevelSecurityTables.Should().Contain("public.reg_owned");
        report.Problems.Should().ContainSingle().Which.Should().Contain("reg_owned");
    }

    [Fact]
    public async Task PrivilegeCheck_UnreachableDatabase_IsSkipped()
    {
        var provider = BuildWithRowLevelSecurity(
            "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=2", privilegeCheck: null);

        var act = () => StartHostedServicesAsync(provider);

        await act.Should().NotThrowAsync();
    }

    private string AppConnectionString() => new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
    {
        Username = AppRole,
        Password = AppPassword,
    }.ConnectionString;

    private static ServiceProvider BuildWithRowLevelSecurity(string connectionString, string? privilegeCheck)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = connectionString + ";SSL Mode=Disable",
            ["SharedKernel:Persistence:Npgsql:RowLevelSecurity:Enabled"] = "true",
        };
        if (privilegeCheck is not null)
            settings["SharedKernel:Persistence:Npgsql:RowLevelSecurity:PrivilegeCheck"] = privilegeCheck;

        return Build(settings);
    }

    private static async Task StartHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var hostedService in provider.GetServices<IHostedService>())
            await hostedService.StartAsync(CancellationToken.None);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, string? connectionStringName = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        if (connectionStringName is null)
            services.AddSharedKernelNpgsql(configuration);
        else
            services.AddSharedKernelNpgsql(configuration, connectionStringName);

        return services.BuildServiceProvider();
    }
}
