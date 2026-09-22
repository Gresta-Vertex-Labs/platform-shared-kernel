using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Remediation;

/// <summary>
/// F16 (final review): a context whose assembly declares no <c>IEntityTypeConfiguration&lt;T&gt;</c> must not make EF Core
/// log <see cref="CoreEventId.NoEntityTypeConfigurationsWarning"/> on every model build.
/// </summary>
public sealed class NoEntityTypeConfigurationsWarningTests
{
    // The warning is turned into an exception, so logging it fails the model build.
    private static DbContextOptions Options(SqliteConnection connection) =>
        new DbContextOptionsBuilder()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w
                .Throw(CoreEventId.NoEntityTypeConfigurationsWarning)
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

    [Fact]
    public void AContextInAnAssemblyWithoutConfigurations_BuildsItsModelWithoutTheWarning()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        var contextType = EmitContextInAnEmptyAssembly();

        using var context = (SharedKernelDbContext)Activator.CreateInstance(
            contextType, Options(connection), PersistenceContextDependencies.Create(new FakeAuditActorContext()))!;

        FluentActions.Invoking(() => context.Model).Should().NotThrow();
    }

    [Fact]
    public void Control_ScanningAnAssemblyWithoutConfigurations_LogsTheWarning()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using var context = new ScansAnEmptyAssemblyContext(
            Options(connection), PersistenceContextDependencies.Create(new FakeAuditActorContext()));

        FluentActions.Invoking(() => context.Model).Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(CoreEventId.NoEntityTypeConfigurationsWarning)}*");
    }

    // A context type in a dynamic assembly that holds nothing else: "public sealed class NoConfigurationsContext :
    // SharedKernelDbContext" with the (options, dependencies) constructor.
    private static Type EmitContextInAnEmptyAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"SkNoConfigurations{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("Main").DefineType(
            "NoConfigurationsContext", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(SharedKernelDbContext));

        Type[] parameters = [typeof(DbContextOptions), typeof(PersistenceContextDependencies)];
        var baseConstructor = typeof(SharedKernelDbContext).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, parameters)!;

        var il = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, parameters).GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, baseConstructor);
        il.Emit(OpCodes.Ret);

        return type.CreateType();
    }

    private sealed class ScansAnEmptyAssemblyContext(DbContextOptions options, PersistenceContextDependencies dependencies)
        : SharedKernelDbContext(options, dependencies)
    {
        protected override bool ShouldApplyConfiguration(Type configurationType) => false;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(IClock).Assembly);
        }
    }
}
