using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.Integration;

/// <summary>
/// The command timeout of a context registered with <c>AddSharedKernelPostgres</c> is the connection string's
/// <c>Command Timeout</c> (there is no separate builder switch): proven against a real PostgreSQL with a
/// deliberately slow <c>pg_sleep(...)</c> command.
/// </summary>
[Collection("PostgreSQL")]
public sealed class CommandTimeoutIntegrationTests
{
    private const string DatabaseName = "sk_persistence_command_timeout";

    private readonly PostgreSqlContainerFixture _fixture;

    public CommandTimeoutIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private ServiceProvider BuildProvider(int commandTimeoutSeconds)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = DatabaseName,
            CommandTimeout = commandTimeoutSeconds,
        }.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:timeouts"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelPostgres<ConcurrencyTestDbContext>(configuration, "timeouts", p => p
            .ConfigureDbContext((_, o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ConnectionStringCommandTimeout_SlowQueryExceedingIt_ThrowsTimeoutRelatedException()
    {
        await using var provider = BuildProvider(commandTimeoutSeconds: 1);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        Func<Task> act = () => ctx.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5);");

        await act.Should().ThrowAsync<Exception>(
            "an exceeded command timeout is a genuine failure (possibly after the retrying strategy gave up)");
    }

    [Fact]
    public async Task ConnectionStringCommandTimeout_SlowQueryWithinIt_CompletesSuccessfully()
    {
        await using var provider = BuildProvider(commandTimeoutSeconds: 30);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        Func<Task> act = () => ctx.Database.ExecuteSqlRawAsync("SELECT pg_sleep(1);");

        await act.Should().NotThrowAsync();
    }
}
