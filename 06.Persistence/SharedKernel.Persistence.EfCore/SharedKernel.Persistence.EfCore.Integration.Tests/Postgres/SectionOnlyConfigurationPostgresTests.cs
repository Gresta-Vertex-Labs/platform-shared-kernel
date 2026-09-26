using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Transactions;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

/// <summary>
/// The default data source of <c>AddSharedKernelPostgres</c> configured only through
/// <c>SharedKernel:Persistence:{name}</c> — no <c>ConnectionStrings:{name}</c> entry — works end to end: the section's
/// connection string, its session settings and its migration connection string all reach the data sources.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class SectionOnlyConfigurationPostgresTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task ADefaultDataSourceConfiguredOnlyThroughItsSection_WritesReadsAndAppliesTheSectionSettings()
    {
        var database = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"sk_section_{Guid.NewGuid():N}" }.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Persistence:crm:ConnectionString"] = database,
                ["SharedKernel:Persistence:crm:StatementTimeoutMilliseconds"] = "12345",
                ["SharedKernel:Persistence:crm:MigrationConnectionString"] = database,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestContext>(_ => new FakeAuditActorContext());
        services.AddSharedKernelPostgres<R1AContext>(configuration, "crm", p =>
            p.ConfigureDbContext((_, o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        provider.GetRequiredService<NpgsqlDataSource>().ConnectionString.Should().Contain("sk_section_",
            "the default (unkeyed) data source reads the section's connection string");
        provider.GetKeyedService<NpgsqlDataSource>(NpgsqlDataSourceKeys.Migration).Should().NotBeNull(
            "the section's MigrationConnectionString registers the migration data source");

        var id = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<R1AContext>().Database.EnsureCreatedAsync();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<R1AThing, Guid>>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .ExecuteInTransactionAsync(ct => repository.AddAsync(new R1AThing(id, new SystemClock()) { Name = "section" }, ct));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<R1AContext>();
            (await context.As.SingleAsync(x => x.Id == id)).Name.Should().Be("section");

            var timeout = await context.Database.SqlQueryRaw<string>("SELECT current_setting('statement_timeout') AS \"Value\"").SingleAsync();
            timeout.Should().Be("12345ms", "session settings come from the same section");
        }
    }
}
