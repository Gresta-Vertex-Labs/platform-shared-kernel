using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>Tests for <see cref="PersistenceNamespaceConventionRules"/>.</summary>
public sealed class PersistenceNamespaceConventionRulesTests
{
    [Fact]
    public void RealPersistenceAssemblies_FollowTheNamespaceConvention()
    {
        var violations = PersistenceNamespaceConventionRules.FindMisplacedExtensions(
            typeof(SharedKernel.Persistence.Abstractions.Context.ICrossTenantScope).Assembly,
            typeof(SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext).Assembly,
            typeof(SharedKernel.Persistence.EfCorePersistenceBuilderAuditingExtensions).Assembly,
            typeof(SharedKernel.Persistence.EfCorePersistenceBuilderEncryptionExtensions).Assembly,
            typeof(SharedKernel.Persistence.NpgsqlPersistenceExtensions).Assembly,
            typeof(SharedKernel.Persistence.Dapper.Sessions.IDbSessionFactory).Assembly);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void RealPersistenceAssemblies_DeclareTheirEntryPointsInTheSharedNamespaces()
    {
        // Guards the pass path against vacuity: the rule really sees these extension classes.
        typeof(SharedKernel.Persistence.PostgresPersistenceExtensions).Namespace
            .Should().Be(PersistenceNamespaceConventionRules.RegistrationNamespace);
        typeof(SharedKernel.Persistence.EfCore.PropertyBuilderEncryptExtensions).Namespace
            .Should().Be(PersistenceNamespaceConventionRules.EfCoreHelpersNamespace);
        typeof(SharedKernel.Persistence.EfCore.AuditImmutabilityMigrationBuilderExtensions).Namespace
            .Should().Be(PersistenceNamespaceConventionRules.EfCoreHelpersNamespace);
    }

    [Fact]
    public void RegistrationExtension_InAFeatureNamespace_IsReported()
    {
        var assembly = CompileInMemory("MisplacedRegistration", """
            namespace Microsoft.Extensions.DependencyInjection { public interface IServiceCollection { } }
            namespace SharedKernel.Persistence.Widgets.Extensions
            {
                using Microsoft.Extensions.DependencyInjection;
                public static class WidgetExtensions
                {
                    public static IServiceCollection AddWidgets(this IServiceCollection services) => services;
                }
            }
            """);

        PersistenceNamespaceConventionRules.FindMisplacedExtensions(assembly)
            .Should().ContainSingle().Which.Should().Contain("AddWidgets").And.Contain("'SharedKernel.Persistence'");
    }

    [Fact]
    public void ModelConfigurationExtension_OutsideTheEfCoreNamespace_IsReported()
    {
        var assembly = CompileInMemory("MisplacedModelHelper", """
            namespace Microsoft.EntityFrameworkCore.Metadata.Builders { public class EntityTypeBuilder<T> { } }
            namespace SharedKernel.Persistence.EfCore.Widgets
            {
                using Microsoft.EntityFrameworkCore.Metadata.Builders;
                public static class WidgetModelExtensions
                {
                    public static EntityTypeBuilder<T> HasWidget<T>(this EntityTypeBuilder<T> builder) => builder;
                }
            }
            namespace SharedKernel.Persistence.EfCore
            {
                using Microsoft.EntityFrameworkCore.Metadata.Builders;
                public static class GoodModelExtensions
                {
                    public static EntityTypeBuilder<T> HasGood<T>(this EntityTypeBuilder<T> builder) => builder;
                }
            }
            """);

        PersistenceNamespaceConventionRules.FindMisplacedExtensions(assembly)
            .Should().ContainSingle().Which.Should().Contain("HasWidget").And.Contain("'SharedKernel.Persistence.EfCore'");
    }

    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile: "
                + string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return Assembly.Load(stream.ToArray());
    }
}
