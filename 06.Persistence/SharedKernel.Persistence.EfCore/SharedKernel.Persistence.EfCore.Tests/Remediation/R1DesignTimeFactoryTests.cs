using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;

namespace SharedKernel.Persistence.EfCore.Tests.Remediation;

/// <summary>F7: the design-time factory builds the context on the migration connection (split roles).</summary>
public sealed class R1DesignTimeFactoryTests
{
    private sealed class PlainFactory() : PostgresDesignTimeDbContextFactory<R1PlainContext>("orders")
    {
        protected override R1PlainContext Create(DbContextOptions<R1PlainContext> options, PersistenceContextDependencies dependencies) =>
            new(options, dependencies);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void TheMigrationConnectionString_WinsOverTheRuntimeOne()
    {
        var configuration = Configuration(
            ("ConnectionStrings:orders", "Host=db;Database=orders;Username=app"),
            ("SharedKernel:Persistence:orders:MigrationConnectionString", "Host=db;Database=orders;Username=owner"));

        new PlainFactory().ResolveConnectionString([], configuration).Should().Contain("Username=owner");
    }

    [Fact]
    public void TheConnectionArgument_WinsOverConfiguration_AndTheRuntimeOneIsTheFallback()
    {
        var configuration = Configuration(("ConnectionStrings:orders", "Host=db;Database=orders;Username=app"));
        var factory = new PlainFactory();

        factory.ResolveConnectionString(["--connection", "Host=cli;Database=orders;Username=owner"], configuration)
            .Should().Contain("Host=cli");
        factory.ResolveConnectionString([], configuration).Should().Contain("Username=app");
        factory.ResolveConnectionString([], Configuration()).Should().BeNull();
    }

    [Fact]
    public void CreateDbContext_BuildsANpgsqlContext_WithoutTouchingTheDatabase()
    {
        using var context = new PlainFactory().CreateDbContext(["--connection", "Host=localhost;Database=design;Username=owner"]);

        context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).Username.Should().Be("owner");
        context.Model.FindEntityType(typeof(R1Item)).Should().NotBeNull();
    }
}
