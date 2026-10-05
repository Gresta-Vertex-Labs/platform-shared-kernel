#pragma warning disable EF1001 // NpgsqlDesignTimeServices is what 'dotnet ef' itself loads for this provider.
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

/// <summary>
/// Found by the BillingApi sample: <c>migrationBuilder.EnableTenantRowLevelSecurityForModel(TargetModel!)</c> — the call
/// every README prescribes — created no policy at all in a real migration. A migration's <c>TargetModel</c> is rebuilt
/// from its Designer file as property bags without CLR types, so the <c>IHasTenant</c> type test never matched. The
/// tests before this one passed the live model instead. This test compiles a scaffolded migration and uses its own
/// <c>TargetModel</c>, exactly as <c>Migrate()</c> does.
/// </summary>
public sealed class MigrationTargetModelTests
{
    private const string NoDatabase = "Host=localhost;Port=1;Database=none;Username=x;Password=y";

    [Fact]
    public void RowLevelSecurityForModel_OnAScaffoldedMigrationsTargetModel_ProtectsEveryTenantTable()
    {
        var targetModel = CompileScaffoldedMigration().TargetModel!;
        targetModel.GetEntityTypes().Should().OnlyContain(e => e.ClrType == typeof(Dictionary<string, object>),
            "a migration's target model has no CLR types — that is the case under test");

        var migration = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        migration.EnableTenantRowLevelSecurityForModel(targetModel);

        var sql = string.Join('\n', migration.Operations.OfType<SqlOperation>().Select(o => o.Sql));
        sql.Should().Contain("ALTER TABLE \"customers\" FORCE ROW LEVEL SECURITY")
            .And.Contain("CREATE POLICY \"customers_tenant_isolation\"");
    }

    [Fact]
    public void RowLevelSecurityForModel_WithoutAnyTenantTable_FailsInsteadOfDoingNothing()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity("Plain", b =>
        {
            b.Property<Guid>("Id");
            b.ToTable("plain");
        });

        var act = () => new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL")
            .EnableTenantRowLevelSecurityForModel(modelBuilder.FinalizeModel());

        act.Should().Throw<InvalidOperationException>().WithMessage("*found no tenant tables*");
    }

    private static Migration CompileScaffoldedMigration()
    {
        using var host = EncryptionHost.Build<CustomerDbContext>(NoDatabase);
        using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();

        var services = new ServiceCollection()
            .AddEntityFrameworkDesignTimeServices()
            .AddDbContextDesignTimeServices(context);
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
        var scaffolded = services.BuildServiceProvider().GetRequiredService<IMigrationsScaffolder>()
            .ScaffoldMigration("Initial", "Scaffolded.Migrations");

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create(
            "ScaffoldedMigration",
            [CSharpSyntaxTree.ParseText(scaffolded.MigrationCode), CSharpSyntaxTree.ParseText(scaffolded.MetadataCode)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        emitted.Success.Should().BeTrue(string.Join('\n', emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var migrationType = Assembly.Load(image.ToArray()).GetTypes().Single(t => typeof(Migration).IsAssignableFrom(t));
        return (Migration)Activator.CreateInstance(migrationType)!;
    }
}
