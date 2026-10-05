#pragma warning disable EF1001 // NpgsqlDesignTimeServices is what 'dotnet ef' itself loads for this provider.
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

/// <summary>
/// Finding A10: a normalization delegate stored as a model annotation made <c>dotnet ef migrations add</c> fail
/// ("Cannot scaffold C# literals of type Func"). Normalization is now flags and a name.
/// </summary>
public sealed class DesignTimeTests
{
    private const string NoDatabase = "Host=localhost;Port=1;Database=none;Username=x;Password=y";

    private static IServiceProvider DesignTimeServices(DbContext context)
    {
        var services = new ServiceCollection()
            .AddEntityFrameworkDesignTimeServices()
            .AddDbContextDesignTimeServices(context);
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void MigrationScaffolding_Succeeds_AndCarriesTheEncryptionAnnotations()
    {
        using var host = EncryptionHost.Build<CustomerDbContext>(NoDatabase);
        using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();

        var migration = DesignTimeServices(context).GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration("Initial", "Test.Migrations");

        migration.SnapshotCode.Should().Contain("SharedKernel:Persistence:Encrypt")
            .And.Contain("customer.billing.iban")
            .And.Contain("SharedKernel:Persistence:Encrypt:BlindIndex:Normalizer")
            .And.Contain("Billing_Bank_IbanBlindIndex");
        migration.MigrationCode.Should().Contain("email_blind_index");
    }

    [Fact]
    public void CompiledModelScaffolding_Succeeds_AndKeepsTheAnnotationsTheRuntimeReads()
    {
        using var host = EncryptionHost.Build<SupplierDbContext>(NoDatabase);
        using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SupplierDbContext>();
        var output = Path.Combine(Path.GetTempPath(), "sk-enc-compiled-" + Guid.NewGuid().ToString("N"));

        try
        {
            var files = DesignTimeServices(context).GetRequiredService<ICompiledModelScaffolder>().ScaffoldModel(
                context.GetService<IDesignTimeModel>().Model,
                output,
                new CompiledModelCodeGenerationOptions
                {
                    ContextType = typeof(SupplierDbContext),
                    ModelNamespace = "Test.CompiledModels",
                    Language = "C#",
                });

            var code = string.Concat(files.Select(File.ReadAllText));
            code.Should().Contain("SharedKernel:Persistence:Encrypt:Applied")
                .And.Contain("SharedKernel:Persistence:Encrypt:BlindIndex:Property")
                .And.Contain("supplier.billing.iban");
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }
}
