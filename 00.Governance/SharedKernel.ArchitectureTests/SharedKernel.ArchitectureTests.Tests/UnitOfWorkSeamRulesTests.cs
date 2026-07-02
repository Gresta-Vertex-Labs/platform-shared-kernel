using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct"/> introduced by
/// WO-037 P-229 phase <c>SK.00.CryptoDelegationAndUowSeamGuard</c>.
/// </summary>
/// <remarks>
/// T-157: Fire path — one IUnitOfWork-named interface inherits from the other.
/// T-158: Fire path — only one IUnitOfWork-named type exists across both assemblies.
/// T-159: Pass path — two independently-declared, non-inheriting IUnitOfWork interfaces.
/// </remarks>
public class UnitOfWorkSeamRulesTests
{
    // ---------------------------------------------------------------------------
    // T-157 — Fire path: one IUnitOfWork interface's base list contains the other
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-157: Two contrived fixture assemblies where
    /// <c>SharedKernel.Application.Behaviors.IUnitOfWork</c> inherits from
    /// <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c> must fail
    /// <see cref="UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct"/>.
    /// </summary>
    [Fact]
    public void UnitOfWorkInterfacesRemainDistinct_ApplicationInheritsFromPersistence_RuleFails()
    {
        // Build a combined fixture where both namespaces live in one assembly so the cross-
        // namespace base-interface check fires without needing two compilation passes.
        const string combinedSource = """
            namespace SharedKernel.Persistence.Abstractions
            {
                public interface IUnitOfWork
                {
                    System.Threading.Tasks.Task CommitAsync(System.Threading.CancellationToken ct = default);
                }
            }

            namespace SharedKernel.Application.Behaviors
            {
                // Violation: application IUnitOfWork extends persistence IUnitOfWork.
                public interface IUnitOfWork : SharedKernel.Persistence.Abstractions.IUnitOfWork
                {
                    // Additional application-layer members
                }
            }
            """;

        // For the two-assembly variant (T-157 proper): compile persistence first, then compile
        // application that references it. We put both IUnitOfWork types in one assembly to
        // keep the fixture self-contained; Mono.Cecil reads the same module for both, so the
        // bidirectional check fires correctly.
        var combinedAssembly = CompileInMemory(
            "Fixture.UoW.T157.Combined",
            combinedSource,
            new[] { Assembly.Load("System.Runtime").Location });

        // Use the combined assembly as both the "application" and "persistence" side.
        // The predicate will find SharedKernel.Application.Behaviors.IUnitOfWork extending
        // SharedKernel.Persistence.Abstractions.IUnitOfWork — check 3a fires.
        var result = UnitOfWorkSeamRules
            .UnitOfWorkInterfacesRemainDistinct(combinedAssembly, combinedAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "SharedKernel.Application.Behaviors.IUnitOfWork extends " +
                     "SharedKernel.Persistence.Abstractions.IUnitOfWork in its base-interface list — " +
                     "this is the exact future merge pattern the seam guard must catch");
    }

    // ---------------------------------------------------------------------------
    // T-158 — Fire path: only one IUnitOfWork-named type exists across both supplied assemblies
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-158: A contrived fixture where only one <c>IUnitOfWork</c>-named type exists (in the
    /// application-behaviors assembly) but none in the persistence-abstractions assembly must
    /// fail <see cref="UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct"/> because the
    /// existence check (check 1) fails for the missing persistence-side type.
    /// </summary>
    [Fact]
    public void UnitOfWorkInterfacesRemainDistinct_PersistenceIUnitOfWorkMissing_RuleFails()
    {
        // Application assembly has the correct IUnitOfWork.
        const string applicationSource = """
            namespace SharedKernel.Application.Behaviors
            {
                public interface IUnitOfWork
                {
                    System.Threading.Tasks.Task CommitAsync(System.Threading.CancellationToken ct = default);
                }
            }
            """;

        // Persistence assembly has NO IUnitOfWork — simulates renaming or accidental removal.
        const string persistenceSource = """
            namespace SharedKernel.Persistence.Abstractions
            {
                // IUnitOfWork is missing — renamed or removed.
                public interface IRepository { }
            }
            """;

        var applicationAssembly = CompileInMemory(
            "Fixture.UoW.T158.ApplicationOnly",
            applicationSource,
            new[] { Assembly.Load("System.Runtime").Location });

        var persistenceAssembly = CompileInMemory(
            "Fixture.UoW.T158.PersistenceNoUoW",
            persistenceSource,
            new[] { Assembly.Load("System.Runtime").Location });

        var result = UnitOfWorkSeamRules
            .UnitOfWorkInterfacesRemainDistinct(applicationAssembly, persistenceAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "SharedKernel.Persistence.Abstractions.IUnitOfWork is absent from the " +
                     "persistence assembly — existence check 1 must fail to prevent silent removal " +
                     "of the seam-pattern interface without governance review");
    }

    // ---------------------------------------------------------------------------
    // T-159 — Pass path: two independently-declared, non-inheriting IUnitOfWork interfaces
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-159: Two contrived fixture assemblies where
    /// <c>SharedKernel.Application.Behaviors.IUnitOfWork</c> and
    /// <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c> are independently declared
    /// with no inheritance between them must pass
    /// <see cref="UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct"/>.
    /// </summary>
    [Fact]
    public void UnitOfWorkInterfacesRemainDistinct_TwoIndependentIUnitOfWorkInterfaces_RulePasses()
    {
        // Application assembly: its own IUnitOfWork, no reference to persistence.
        const string applicationSource = """
            namespace SharedKernel.Application.Behaviors
            {
                // Compliant: independently declared, no inheritance from persistence IUnitOfWork.
                public interface IUnitOfWork
                {
                    System.Threading.Tasks.Task CommitAsync(System.Threading.CancellationToken ct = default);
                }
            }
            """;

        // Persistence assembly: its own IUnitOfWork, no reference to application.
        const string persistenceSource = """
            namespace SharedKernel.Persistence.Abstractions
            {
                // Compliant: independently declared, no inheritance from application IUnitOfWork.
                public interface IUnitOfWork
                {
                    System.Threading.Tasks.Task CommitAsync(System.Threading.CancellationToken ct = default);
                    System.Threading.Tasks.Task<int> CommitAndReturnCountAsync(System.Threading.CancellationToken ct = default);
                }
            }
            """;

        var applicationAssembly = CompileInMemory(
            "Fixture.UoW.T159.Application",
            applicationSource,
            new[] { Assembly.Load("System.Runtime").Location });

        var persistenceAssembly = CompileInMemory(
            "Fixture.UoW.T159.Persistence",
            persistenceSource,
            new[] { Assembly.Load("System.Runtime").Location });

        var result = UnitOfWorkSeamRules
            .UnitOfWorkInterfacesRemainDistinct(applicationAssembly, persistenceAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Both IUnitOfWork interfaces are independently declared with no inheritance " +
                     "between them — this is the compliant local-seam pattern where the bridge is " +
                     "a concrete adapter at the composition root, never interface inheritance");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        string[]? extraAssemblyLocations = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var refList = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        if (extraAssemblyLocations is not null)
        {
            foreach (var loc in extraAssemblyLocations)
                refList.Add(MetadataReference.CreateFromFile(loc));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: refList,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var fs = System.IO.File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);
            if (!emitResult.Success)
            {
                var errors = string.Join(
                    System.Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
