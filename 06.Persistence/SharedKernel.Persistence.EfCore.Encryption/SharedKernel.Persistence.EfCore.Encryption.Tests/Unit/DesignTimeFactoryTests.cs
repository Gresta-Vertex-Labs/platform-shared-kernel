using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

/// <summary>
/// <c>dotnet ef migrations add</c> builds the context through <see cref="PostgresDesignTimeDbContextFactory{TContext}"/>.
/// Found by the BillingApi sample: the factory built the model without the capability conventions, so a model with
/// <c>.Encrypt(...)</c> could not produce a migration at all (and without the guard, the migration would have lacked
/// the blind-index columns the running service uses).
/// </summary>
public sealed class DesignTimeFactoryTests
{
    private static readonly string[] DesignTimeArgs = ["--connection", "Host=localhost;Port=1;Database=design;Username=owner"];

    private sealed class WiredFactory() : PostgresDesignTimeDbContextFactory<CustomerDbContext>("customers")
    {
        protected override CustomerDbContext Create(DbContextOptions<CustomerDbContext> options, PersistenceContextDependencies dependencies) =>
            new(options, dependencies);

        protected override void ConfigurePersistence(EfCorePersistenceBuilder<CustomerDbContext> persistence) =>
            persistence.UseFieldEncryption(k => k.AddBlindIndexNormalizer<IbanNormalizer>());
    }

    private sealed class UnwiredFactory() : PostgresDesignTimeDbContextFactory<CustomerDbContext>("customers")
    {
        protected override CustomerDbContext Create(DbContextOptions<CustomerDbContext> options, PersistenceContextDependencies dependencies) =>
            new(options, dependencies);
    }

    [Fact]
    public void WithConfigurePersistence_TheDesignTimeModelIsTheRuntimeModel()
    {
        using var design = new WiredFactory().CreateDbContext(DesignTimeArgs);
        using var services = EncryptionHost.Build<CustomerDbContext>("Host=localhost;Port=1;Database=none;Username=x;Password=y");
        using var scope = services.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();

        design.Model.FindEntityType(typeof(Customer))!.FindProperty("EmailBlindIndex").Should().NotBeNull();

        var differ = design.GetService<IMigrationsModelDiffer>();
        differ.HasDifferences(
                design.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                runtime.GetService<IDesignTimeModel>().Model.GetRelationalModel())
            .Should().BeFalse("a migration generated at design time must describe the schema the service runs against");
    }

    [Fact]
    public void WithoutConfigurePersistence_AnEncryptedModel_NamesTheFix()
    {
        var act = () =>
        {
            using var design = new UnwiredFactory().CreateDbContext(DesignTimeArgs);
            _ = design.Model;
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*UseFieldEncryption*ConfigurePersistence*");
    }
}
