using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

/// <summary>
/// T-113 (WO-053/P-337, C-140): <see cref="SharedKernel.Persistence.EfCore.Extensions.EfCorePersistenceBuilder{TContext}.WithCommandTimeout(int)"/>
/// against a REAL PostgreSQL Testcontainer — proves the configured timeout is genuinely applied to
/// issued commands (a deliberately slow <c>pg_sleep(...)</c> query), not merely accepted and stored
/// as inert metadata. <see cref="WithCommandTimeoutTests"/> (SharedKernel.Persistence.EfCore.Tests,
/// SQLite) already proves the metadata is wired via <c>DbContextOptionsBuilder.CommandTimeout</c>;
/// this class proves the resulting behavior.
/// </summary>
/// <remarks>
/// Shares the <see cref="PostgreSqlContainerFixture"/> registered by <see cref="PostgreSqlTestCollection"/>
/// (WO-053/P-336), targeting its own uniquely-named database — reuses <see cref="ConcurrencyTestDbContext"/>/
/// <see cref="ConcurrentPgAggregate"/> from <see cref="ConcurrencyIntegrationTests"/> purely as a
/// schema-bearing context; no entity data is required for a raw <c>pg_sleep(...)</c> command.
/// </remarks>
[Collection("PostgreSQL")]
public sealed class CommandTimeoutIntegrationTests
{
    private const string DatabaseName = "sk_persistence_command_timeout";

    private readonly PostgreSqlContainerFixture _fixture;

    public CommandTimeoutIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    [Fact]
    public async Task WithCommandTimeout_SlowQueryExceedingConfiguredTimeout_ThrowsTimeoutRelatedException()
    {
        // Arrange
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<ConcurrencyTestDbContext>(opts => opts.UsePostgreSQL(ConnectionString))
            .WithCommandTimeout(1)
            .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<ConcurrencyTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        // Act — a query that deliberately takes far longer than the configured 1-second timeout.
        Func<Task> act = () => ctx.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5);");

        // Assert — Npgsql surfaces the exceeded command timeout as a genuine failure, never a
        // silently-ignored configuration value.
        await act.Should().ThrowAsync<System.Data.Common.DbException>();
    }

    [Fact]
    public async Task WithCommandTimeout_SlowQueryWithinConfiguredTimeout_CompletesSuccessfully()
    {
        // Arrange — the SAME kind of slow query, but comfortably within a generous timeout,
        // proving the configured value genuinely governs the outcome rather than the query itself
        // being unconditionally doomed.
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<ConcurrencyTestDbContext>(opts => opts.UsePostgreSQL(ConnectionString))
            .WithCommandTimeout(30)
            .Build();

        var provider = services.BuildServiceProvider();
        await using var ctx = provider.GetRequiredService<ConcurrencyTestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        // Act
        Func<Task> act = () => ctx.Database.ExecuteSqlRawAsync("SELECT pg_sleep(1);");

        // Assert
        await act.Should().NotThrowAsync();
    }
}
