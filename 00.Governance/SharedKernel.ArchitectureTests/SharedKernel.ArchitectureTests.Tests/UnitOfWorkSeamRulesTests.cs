using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="UnitOfWorkSeamRules.SharedContractsAreNotRedeclared"/> (P-558): the unit of
/// work, request context and audit writer are declared once, in
/// <c>SharedKernel.Execution</c>.
/// </summary>
public class UnitOfWorkSeamRulesTests
{
    [Fact]
    public void SharedContractsAreNotRedeclared_ARedeclaredUnitOfWork_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Persistence.Abstractions.UnitOfWork
            {
                public interface IUnitOfWork { }
            }
            """;

        var result = UnitOfWorkSeamRules
            .SharedContractsAreNotRedeclared(CompileInMemory("UowRedeclared", source))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "a second IUnitOfWork brings the composition-root adapters back");
    }

    [Theory]
    [InlineData("ITransactionalUnitOfWork")]
    [InlineData("ICurrentActorContext")]
    [InlineData("ICurrentTenantContext")]
    [InlineData("IAuditTrailWriter")]
    [InlineData("IRequestContext")]
    public void SharedContractsAreNotRedeclared_ARetiredOrDuplicateSeam_RuleFails(string interfaceName)
    {
        var source = $$"""
            namespace SharedKernel.Persistence.Abstractions.Context
            {
                public interface {{interfaceName}} { }
            }
            """;

        var result = UnitOfWorkSeamRules
            .SharedContractsAreNotRedeclared(CompileInMemory($"Redeclared{interfaceName}", source))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: $"{interfaceName} may only be declared in SharedKernel.Execution");
    }

    [Fact]
    public void SharedContractsAreNotRedeclared_UnrelatedInterfaces_RulePasses()
    {
        const string source = """
            namespace SharedKernel.Persistence.Abstractions.Repositories
            {
                public interface IRepository { }
                public interface IUnitOfWorkFactory { }
            }
            """;

        var result = UnitOfWorkSeamRules
            .SharedContractsAreNotRedeclared(CompileInMemory("UowClean", source))
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    public static IEnumerable<object[]> RealAssembliesThatMustNotRedeclare()
    {
        yield return [typeof(SharedKernel.Application.Messaging.ICommand).Assembly];
        yield return [typeof(SharedKernel.Application.Pipeline.Extensions.ApplicationBehaviorsBuilder).Assembly];
        yield return [typeof(SharedKernel.Application.Mediator.MediatR.MediatRServiceCollectionExtensions).Assembly];
        yield return [typeof(SharedKernel.Persistence.Abstractions.Context.ICrossTenantScope).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCorePersistenceBuilderAuditingExtensions).Assembly];
    }

    [Theory]
    [MemberData(nameof(RealAssembliesThatMustNotRedeclare))]
    public void SharedContractsAreNotRedeclared_RealAssemblies_RulePasses(Assembly assembly)
    {
        var result = UnitOfWorkSeamRules.SharedContractsAreNotRedeclared(assembly).GetResult();

        result.IsSuccessful.Should().BeTrue(because: $"'{assembly.GetName().Name}' must consume the shared contracts, not redeclare them");
    }

    [Fact]
    public void SharedContracts_AreDeclaredInApplicationAbstractions()
    {
        var abstractions = typeof(SharedKernel.Execution.Transactions.IUnitOfWork).Assembly;

        abstractions.GetName().Name.Should().Be("SharedKernel.Execution");
        typeof(SharedKernel.Execution.Context.IRequestContext).Assembly.Should().BeSameAs(abstractions);
        typeof(SharedKernel.Execution.Auditing.IAuditTrailWriter).Assembly.Should().BeSameAs(abstractions);
    }

    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");
        using (var fs = File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);
            if (!emitResult.Success)
            {
                throw new InvalidOperationException(
                    $"Test fixture '{assemblyName}' failed to compile: " +
                    string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
